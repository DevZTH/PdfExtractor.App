#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
version="26.09.0-0"
checksum="7a6f256a0ddf7536182246a5733331bf4677cbcc34f4663774947ad34556c8d0"
destination="$project_dir/vendor/poppler-windows"
if [[ -f "$destination/.verified-sha256" ]] && [[ "$(cat "$destination/.verified-sha256")" == "$checksum" ]] && [[ -f "$destination/Library/bin/pdfimages.exe" ]]; then
  exit 0
fi
for tool in curl unzip sha256sum; do command -v "$tool" >/dev/null || { printf 'Missing command: %s\n' "$tool" >&2; exit 1; }; done
temporary="$(mktemp -d)"
trap 'rm -rf -- "$temporary"' EXIT
curl --fail --location --retry 3 \
  "https://github.com/oschwartz10612/poppler-windows/releases/download/v$version/Release-$version.zip" \
  --output "$temporary/poppler.zip"
printf '%s  %s\n' "$checksum" "$temporary/poppler.zip" | sha256sum --check --status
unzip -q "$temporary/poppler.zip" -d "$temporary/unpacked"
test -f "$temporary/unpacked/poppler-26.09.0/Library/bin/pdfimages.exe"
mkdir -p "$destination"
cp -a "$temporary/unpacked/poppler-26.09.0"/. "$destination/"
printf '%s\n' "$checksum" > "$destination/.verified-sha256"
