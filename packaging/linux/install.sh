#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
set -euo pipefail
source_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
install_dir="${XDG_DATA_HOME:-$HOME/.local/share}/pdf-image-extractor"
data_dir="${XDG_DATA_HOME:-$HOME/.local/share}"
mkdir -p "$install_dir" "$data_dir/applications" "$data_dir/icons/hicolor/scalable/apps"
if [[ "$source_dir" != "$install_dir" ]]; then
  cp -a "$source_dir"/. "$install_dir"/
fi
chmod +x "$install_dir/PdfImageExtractor"
cp "$install_dir/app.svg" "$data_dir/icons/hicolor/scalable/apps/pdf-image-extractor.svg"
# Escape the reserved characters used in a quoted desktop-entry Exec argument.
escaped_dir=${install_dir//\\/\\\\}
escaped_dir=${escaped_dir//\"/\\\"}
escaped_dir=${escaped_dir//\$/\\\$}
escaped_dir=${escaped_dir//\`/\\\`}
escaped_dir=${escaped_dir//%/%%}
desktop_file="$data_dir/applications/pdf-image-extractor.desktop"
while IFS= read -r line; do
  if [[ "$line" == Exec=* ]]; then
    printf 'Exec="%s/PdfImageExtractor" %%F\n' "$escaped_dir"
  else
    printf '%s\n' "$line"
  fi
done < "$install_dir/pdf-image-extractor.desktop" > "$desktop_file"
if command -v update-desktop-database >/dev/null; then update-desktop-database "$data_dir/applications"; fi
printf 'Installed: %s\n' "$install_dir/PdfImageExtractor"
printf 'Poppler is required: apt install poppler-utils / pacman -S poppler / dnf install poppler-utils\n'
