#!/usr/bin/env python3
"""Stage the static website with Hub and main-commit source downloads."""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile


def git(repository: Path, *arguments: str) -> bytes:
    result = subprocess.run(
        ["git", "-C", str(repository), *arguments],
        check=False, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    )
    if result.returncode:
        raise ValueError(f"Git failed: {result.stderr.decode('utf-8', errors='replace').strip()}")
    return result.stdout


def source_snapshot(repository: Path) -> tuple[str, list[str]]:
    commit = git(repository, "rev-parse", "--verify", "HEAD^{commit}").decode("ascii").strip()
    entries = git(repository, "ls-tree", "-r", "--name-only", "-z", commit, "--", "src", "LICENSE")
    files = [entry.decode("utf-8") for entry in entries.split(b"\0") if entry]
    if "LICENSE" not in files or not any(name.startswith("src/") for name in files):
        raise ValueError("The source commit must contain src/ and the root LICENSE.")
    if any({"bin", "obj"} & {part.casefold() for part in Path(name).parts} for name in files):
        raise ValueError("Tracked bin/obj output must be removed before publishing the source download.")
    return commit, files


def archive_source(repository: Path, download: Path, commit: str, expected_files: list[str]) -> None:
    partial = download.with_suffix(".zip.part")
    try:
        git(repository, "-c", "core.autocrlf=false", "-c", "core.eol=lf", "archive",
            "--format=zip", f"--output={partial}", commit, "--", "src", "LICENSE")
        with zipfile.ZipFile(partial) as archive:
            actual_files = [entry.filename for entry in archive.infolist() if not entry.is_dir()]
            if sorted(actual_files) != sorted(expected_files):
                raise ValueError("The source archive does not contain every tracked src file and LICENSE.")
            damaged = archive.testzip()
            if damaged is not None:
                raise ValueError(f"The source archive failed verification: {damaged}")
        partial.replace(download)
    finally:
        partial.unlink(missing_ok=True)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as payload:
        for chunk in iter(lambda: payload.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def build_site(output: Path) -> Path:
    pages = Path(__file__).resolve().parents[1]
    repository = pages.parent
    hub = repository / "Engine" / "BEngine.Hub.exe"
    output = output.resolve()
    if output == pages or pages in output.parents:
        raise ValueError("The output directory must be outside Pages.")
    obsolete = [pages / "documentation", pages / "data" / "documentation.json"]
    if any(path.exists() for path in obsolete):
        raise ValueError("Detailed manuals belong to Hub and must not be included in the public website.")
    if output.exists():
        raise ValueError("The output directory already exists. Choose a new directory.")
    if not hub.is_file():
        raise ValueError(f"The exported Hub is missing: {hub}")
    with hub.open("rb") as source:
        if source.read(2) != b"MZ":
            raise ValueError(f"The exported Hub is not a Windows executable: {hub}")
    commit, source_files = source_snapshot(repository)

    def ignore_maintenance(directory: str, names: list[str]) -> list[str]:
        return [name for name in names if name in {"tools", "README.md"}] if Path(directory) == pages else []

    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=f".{output.name}-", dir=output.parent) as temporary:
        staged = Path(temporary) / "site"
        shutil.copytree(pages, staged, ignore=ignore_maintenance)
        downloads = staged / "downloads"
        downloads.mkdir(exist_ok=True)
        shutil.copyfile(hub, downloads / "BEngine.Hub.exe")
        archive_source(repository, downloads / "BEngine-main-src.zip", commit, source_files)
        staged.rename(output)
    download = output / "downloads" / "BEngine.Hub.exe"
    source_download = output / "downloads" / "BEngine-main-src.zip"
    print(f"Website: {output}")
    print(f"Hub download: {download} ({download.stat().st_size:,} bytes)")
    print(f"Hub SHA-256: {sha256(download)}")
    print(f"Source commit: {commit}")
    print(f"Source download: {source_download} ({source_download.stat().st_size:,} bytes; {len(source_files):,} files)")
    print(f"Source SHA-256: {sha256(source_download)}")
    return output


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True, help="New directory outside Pages for the deployable website.")
    arguments = parser.parse_args()
    try:
        build_site(arguments.output)
    except (OSError, ValueError) as error:
        parser.exit(1, f"Website build failed: {error}\n")


if __name__ == "__main__":
    main()
