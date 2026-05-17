#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_PATH="${ROOT_DIR}/Jellyfin.Plugin.DoubanBookshelf/Jellyfin.Plugin.DoubanBookshelf.csproj"
PUBLISH_DIR="${ROOT_DIR}/bin"
PACKAGE_DIR="${ROOT_DIR}/dist/package"
DIST_DIR="${ROOT_DIR}/dist"
BUILD_YAML="${ROOT_DIR}/build.yaml"

read_yaml_value() {
  local key="$1"
  python3 - "$BUILD_YAML" "$key" <<'PYREAD'
from pathlib import Path
import re
import sys

path = Path(sys.argv[1])
key = sys.argv[2]
pattern = re.compile(rf"^{re.escape(key)}:\s*['\"]?(.*?)['\"]?\s*$")
for line in path.read_text(encoding="utf-8").splitlines():
    match = pattern.match(line)
    if match:
        print(match.group(1))
        break
else:
    raise SystemExit(f"Missing {key} in {path}")
PYREAD
}

read_block_value() {
  local key="$1"
  python3 - "$BUILD_YAML" "$key" <<'PYBLOCK'
from pathlib import Path
import sys

lines = Path(sys.argv[1]).read_text(encoding="utf-8").splitlines()
key = sys.argv[2]
for index, line in enumerate(lines):
    if line.strip() in {f"{key}: >", f"{key}: |-"}:
        values = []
        for next_line in lines[index + 1:]:
            if next_line.startswith("  "):
                values.append(next_line[2:])
            elif not next_line.strip():
                values.append("")
            else:
                break
        print("\n".join(values).strip())
        break
else:
    raise SystemExit(f"Missing block {key} in {sys.argv[1]}")
PYBLOCK
}

read_artifacts() {
  python3 - "$BUILD_YAML" <<'PYARTIFACTS'
from pathlib import Path
import sys

lines = Path(sys.argv[1]).read_text(encoding="utf-8").splitlines()
in_artifacts = False
for line in lines:
    stripped = line.strip()
    if stripped == "artifacts:":
        in_artifacts = True
        continue
    if in_artifacts:
        if stripped.startswith("-"):
            print(stripped[1:].strip().strip('"\''))
        elif stripped and not line.startswith(" "):
            break
PYARTIFACTS
}

NAME="$(read_yaml_value name)"
VERSION="${VERSION:-$(read_yaml_value version)}"
FRAMEWORK="$(read_yaml_value framework)"
BUILD_OUTPUT_DIR="${ROOT_DIR}/Jellyfin.Plugin.DoubanBookshelf/bin/Release/${FRAMEWORK}"
PACKAGE_BASENAME="Douban-Bookshelf-${VERSION}"
ZIP_PATH="${DIST_DIR}/${PACKAGE_BASENAME}.zip"

rm -rf "$PACKAGE_DIR"
mkdir -p "$PACKAGE_DIR" "$DIST_DIR"

dotnet publish "$PROJECT_PATH" --configuration Release --output "$PUBLISH_DIR" -p:Version="$VERSION" -p:AssemblyVersion="$VERSION" -p:FileVersion="$VERSION"

if [[ -f "${BUILD_OUTPUT_DIR}/Jellyfin.Plugin.DoubanBookshelf.dll" ]]; then
  cp "${BUILD_OUTPUT_DIR}/Jellyfin.Plugin.DoubanBookshelf.dll" "${PUBLISH_DIR}/Jellyfin.Plugin.DoubanBookshelf.dll"
fi

mapfile -t ARTIFACTS < <(read_artifacts)
for artifact in "${ARTIFACTS[@]}"; do
  if [[ ! -f "${PUBLISH_DIR}/${artifact}" ]]; then
    echo "Missing artifact: ${PUBLISH_DIR}/${artifact}" >&2
    exit 1
  fi
  cp "${PUBLISH_DIR}/${artifact}" "$PACKAGE_DIR/"
done

(
  cd "$PACKAGE_DIR"
  rm -f "$ZIP_PATH"
  zip -q -9 "$ZIP_PATH" "${ARTIFACTS[@]}"
)

sha256sum "$ZIP_PATH" > "${ZIP_PATH}.sha256"
echo "Created ${ZIP_PATH}"
echo "Created ${ZIP_PATH}.sha256"
