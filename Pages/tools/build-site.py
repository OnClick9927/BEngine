#!/usr/bin/env python3
"""Stage the static website with the current exported Hub download."""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import shutil


def build_site(output: Path) -> Path:
    pages = Path(__file__).resolve().parents[1]
    repository = pages.parent
    hub = repository / "Engine" / "BEngine.Hub.exe"
    output = output.resolve()
    if output == pages or pages in output.parents:
        raise ValueError("The output directory must be outside Pages.")
    if output.exists():
        raise ValueError("The output directory already exists. Choose a new directory.")
    if not hub.is_file():
        raise ValueError(f"The exported Hub is missing: {hub}")
    with hub.open("rb") as source:
        if source.read(2) != b"MZ":
            raise ValueError(f"The exported Hub is not a Windows executable: {hub}")

    def ignore_maintenance(directory: str, names: list[str]) -> list[str]:
        return [name for name in names if name in {"tools", "README.md"}] if Path(directory) == pages else []

    shutil.copytree(pages, output, ignore=ignore_maintenance)
    downloads = output / "downloads"
    downloads.mkdir(exist_ok=True)
    download = downloads / "BEngine.Hub.exe"
    shutil.copyfile(hub, download)
    digest = hashlib.sha256()
    with download.open("rb") as payload:
        for chunk in iter(lambda: payload.read(1024 * 1024), b""):
            digest.update(chunk)
    print(f"Website: {output}")
    print(f"Hub download: {download} ({download.stat().st_size:,} bytes)")
    print(f"Hub SHA-256: {digest.hexdigest()}")
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
