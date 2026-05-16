#!/usr/bin/env python3
import hashlib
import json
import os
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import urlopen


ROOT_DIR = Path(__file__).resolve().parents[1]
BUILD_YAML = ROOT_DIR / "build.yaml"


def read_yaml_value(key: str) -> str:
    pattern = re.compile(rf"^{re.escape(key)}:\s*['\"]?(.*?)['\"]?\s*$")
    for line in BUILD_YAML.read_text(encoding="utf-8").splitlines():
        match = pattern.match(line)
        if match:
            return match.group(1)
    raise RuntimeError(f"Missing {key} in {BUILD_YAML}")


def read_block_value(key: str) -> str:
    lines = BUILD_YAML.read_text(encoding="utf-8").splitlines()
    for index, line in enumerate(lines):
        if line.strip() in {f"{key}: >", f"{key}: |-"}:
            values: list[str] = []
            for next_line in lines[index + 1:]:
                if next_line.startswith("  "):
                    values.append(next_line[2:])
                elif not next_line.strip():
                    values.append("")
                else:
                    break
            return "\n".join(values).strip()
    raise RuntimeError(f"Missing block {key} in {BUILD_YAML}")


def sha256sum(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as file:
        for chunk in iter(lambda: file.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def get_tag_changelog(tag: str) -> str:
    result = subprocess.run(
        ["git", "tag", "-l", "--format=%(contents)", tag],
        check=False,
        stdout=subprocess.PIPE,
        text=True,
    )
    changelog = result.stdout.strip()
    return changelog if changelog else read_block_value("changelog")


def load_existing_manifest(repository: str) -> list[dict[str, object]]:
    url = f"https://github.com/{repository}/releases/download/manifest/manifest.json"
    try:
        with urlopen(url) as response:
            return json.load(response)
    except HTTPError as error:
        if error.code == 404:
            return [base_manifest_entry()]
        raise


def base_manifest_entry() -> dict[str, object]:
    return {
        "guid": read_yaml_value("guid"),
        "name": read_yaml_value("name"),
        "description": read_block_value("description"),
        "overview": read_yaml_value("overview"),
        "owner": read_yaml_value("owner"),
        "category": read_yaml_value("category"),
        "versions": [],
    }


def generate_version(package_path: Path, tag: str, repository: str) -> dict[str, str]:
    version = read_yaml_value("version")
    return {
        "version": version,
        "changelog": get_tag_changelog(tag),
        "targetAbi": read_yaml_value("targetAbi"),
        "sourceUrl": f"https://github.com/{repository}/releases/download/{tag}/{package_path.name}",
        "checksum": sha256sum(package_path),
        "timestamp": datetime.now(timezone.utc).isoformat(timespec="seconds"),
    }


def main() -> None:
    if len(sys.argv) != 3:
        raise SystemExit("Usage: generate_manifest.py <package.zip> <tag>")

    package_path = Path(sys.argv[1]).resolve()
    tag = sys.argv[2]
    repository = os.environ.get("GITHUB_REPOSITORY")
    if not repository:
        raise SystemExit("GITHUB_REPOSITORY is required")
    if not package_path.is_file():
        raise SystemExit(f"Package not found: {package_path}")

    manifest = load_existing_manifest(repository)
    if not manifest:
        manifest = [base_manifest_entry()]

    version = read_yaml_value("version")
    current_version = generate_version(package_path, tag, repository)
    versions = [item for item in manifest[0].get("versions", []) if item.get("version") != version]
    versions.insert(0, current_version)
    manifest[0].update(base_manifest_entry())
    manifest[0]["versions"] = versions

    output_path = ROOT_DIR / "manifest.json"
    output_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
