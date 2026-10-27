#!/usr/bin/env python3
"""Stage the website, playable Web examples, Hub, and source downloads."""

from __future__ import annotations

import argparse
import base64
import gzip
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
from urllib.parse import urlsplit
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


def validate_web_player(root: Path) -> tuple[int, int]:
    """Reject incomplete or obsolete layouts before publishing playable links."""
    expected = {"index.html", "player.bin", "framework.bin", "resources", "StreamingAssets"}
    if root.is_symlink() or not root.is_dir() or {path.name for path in root.iterdir()} != expected:
        raise ValueError(f"Incomplete Web player layout: {root}. Expected {', '.join(sorted(expected))}.")
    for name in ("index.html", "player.bin", "framework.bin"):
        if not (root / name).is_file() or not (root / name).stat().st_size:
            raise ValueError(f"Missing or empty Web player file: {root / name}")
    for name in ("resources", "StreamingAssets"):
        if not (root / name).is_dir() or not any(path.is_file() for path in (root / name).rglob("*")):
            raise ValueError(f"Missing Web player assets: {root / name}")
    files = []
    forbidden = {"temp", "library", "logs", "obj", "bin", "verification", "__pycache__", ".git"}
    for path in root.rglob("*"):
        relative = path.relative_to(root)
        if path.is_symlink() or any(part.casefold() in forbidden for part in relative.parts):
            raise ValueError(f"Temporary data or linked content in Web player: {path}")
        if path.is_file():
            files.append(path)
    player_bytes = (root / "player.bin").read_bytes()
    player_integrity = "sha256-" + base64.b64encode(hashlib.sha256(player_bytes).digest()).decode("ascii")
    entry = (root / "index.html").read_text(encoding="utf-8")
    if player_integrity not in entry or "./player.bin?benginev=" not in entry:
        raise ValueError(f"Web entry does not match player.bin: {root}")
    try:
        player = gzip.decompress(player_bytes).decode("utf-8")
        descriptor = json.loads(re.search(r"const descriptor = (\{[^\n]*\});", player)[1])
        content = json.loads(re.search(r"const contentManifest = (\{[^\n]*\});", player)[1])
    except (EOFError, OSError, UnicodeError, TypeError, ValueError) as error:
        raise ValueError(f"Invalid packed Web player: {root}") from error
    framework = root / "framework.bin"
    framework_integrity = "sha256-" + base64.b64encode(bytes.fromhex(sha256(framework))).decode("ascii")
    if (descriptor.get("url", "").split("?", 1)[0] != "./framework.bin" or
            descriptor.get("integrity") != framework_integrity or descriptor.get("bytes") != framework.stat().st_size):
        raise ValueError(f"Web runtime does not match framework.bin: {root}")
    if content.get("streamingAssetsTransport") != "http":
        raise ValueError(f"Web examples must read StreamingAssets directly over HTTP: {root}")
    if not content.get("files"):
        raise ValueError(f"Web player resource manifest is empty: {root}")
    for resource in content["files"]:
        url = resource.get("url", "")
        parsed = urlsplit(url)
        relative = Path(parsed.path)
        if (parsed.scheme or parsed.netloc or not url.startswith("./resources/") or
                ".." in relative.parts or not (root / relative).is_file()):
            raise ValueError(f"Invalid or missing built-in Web resource {url!r}: {root}")
    return len(files), sum(path.stat().st_size for path in files)


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
    web_players = {name: validate_web_player(pages / "plays" / name) for name in ("snake", "example")}
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
        # Static hosting must preserve the packed players and their asset paths.
        for name in web_players:
            validate_web_player(staged / "plays" / name)
        staged.rename(output)
    download = output / "downloads" / "BEngine.Hub.exe"
    source_download = output / "downloads" / "BEngine-main-src.zip"
    print(f"Website: {output}")
    print(f"Hub download: {download} ({download.stat().st_size:,} bytes)")
    print(f"Hub SHA-256: {sha256(download)}")
    print(f"Source commit: {commit}")
    print(f"Source download: {source_download} ({source_download.stat().st_size:,} bytes; {len(source_files):,} files)")
    print(f"Source SHA-256: {sha256(source_download)}")
    for name, (count, size) in web_players.items():
        print(f"Playable {name}: plays/{name}/ ({count:,} files; {size:,} bytes)")
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
