#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Both targets, including the Windows installer, are built on Linux.
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$project_dir"
target="${1:-all}"
dotnet_cmd="${DOTNET:-dotnet}"
makensis_cmd="${MAKENSIS:-makensis}"
case "$target" in all|linux|windows) ;; *) printf 'Usage: %s [all|linux|windows]\n' "$0" >&2; exit 2;; esac
command -v "$dotnet_cmd" >/dev/null || { printf 'Install .NET 10 SDK or set DOTNET.\n' >&2; exit 1; }
if [[ "$target" == all || "$target" == windows ]]; then
  command -v "$makensis_cmd" >/dev/null || { printf 'Install NSIS (makensis) or set MAKENSIS.\n' >&2; exit 1; }
  bash scripts/fetch-poppler.sh
fi
mkdir -p artifacts

publish() {
  local rid="$1"
  "$dotnet_cmd" publish src/PdfExtractor.App/PdfExtractor.App.csproj \
    -c Release -r "$rid" --self-contained true -p:PublishAot=false -p:PublishTrimmed=false \
    -p:DebugType=None -p:DebugSymbols=false -o "artifacts/$rid"
  cp packaging/THIRD-PARTY-NOTICES.txt "artifacts/$rid/"
  cp LICENSE COPYRIGHT README.md "artifacts/$rid/"
}

# Include the corresponding application source, without caches, build outputs or local credentials.
tar --exclude='*/bin' --exclude='*/obj' \
  -czf artifacts/PdfImageExtractor-source.tar.gz \
  LICENSE COPYRIGHT README.md Directory.Build.props global.json PdfImageExtractor.sln \
  src tests scripts packaging .github .gitignore

if [[ "$target" == all || "$target" == linux ]]; then
  publish linux-x64
  cp packaging/linux/{install.sh,pdf-image-extractor.desktop} artifacts/linux-x64/
  cp src/PdfExtractor.App/Assets/app.svg artifacts/linux-x64/
  chmod +x artifacts/linux-x64/{PdfImageExtractor,install.sh}
  tar -czf artifacts/PdfImageExtractor-linux-x64.tar.gz -C artifacts/linux-x64 .
fi

if [[ "$target" == all || "$target" == windows ]]; then
  publish win-x64
  # Keep DLLs beside pdfimages.exe: no PATH changes or separate Poppler installation.
  poppler_dir="artifacts/win-x64/poppler"
  mkdir -p "$poppler_dir/Library/bin"
  cp vendor/poppler-windows/Library/bin/pdfimages.exe "$poppler_dir/Library/bin/"
  cp vendor/poppler-windows/Library/bin/*.dll "$poppler_dir/Library/bin/"
  cp -a vendor/poppler-windows/share "$poppler_dir/"
  if [[ -d vendor/poppler-windows/Library/share ]]; then
    cp -a vendor/poppler-windows/Library/share "$poppler_dir/Library/"
  fi
  # Enumerate installed files so uninstalling does not delete user-created images.
  uninstall_include="$project_dir/artifacts/uninstall-files.nsh"
  : > "$uninstall_include"
  while IFS= read -r -d '' file; do
    relative="${file#artifacts/win-x64/}"
    relative="${relative//\//\\}"
    printf 'Delete "$INSTDIR\\%s"\n' "$relative" >> "$uninstall_include"
  done < <(find artifacts/win-x64 -type f -print0)
  while IFS= read -r -d '' folder; do
    [[ "$folder" == artifacts/win-x64 ]] && continue
    relative="${folder#artifacts/win-x64/}"
    relative="${relative//\//\\}"
    printf 'RMDir "$INSTDIR\\%s"\n' "$relative" >> "$uninstall_include"
  done < <(find artifacts/win-x64 -depth -type d -print0)
  "$makensis_cmd" -V2 \
    "-DPUBLISH_DIR=$project_dir/artifacts/win-x64" \
    "-DOUTPUT_FILE=$project_dir/artifacts/PdfImageExtractor-Setup-x64.exe" \
    "-DUNINSTALL_INCLUDE=$uninstall_include" \
    packaging/windows/installer.nsi
fi
checksum_files=(artifacts/PdfImageExtractor-source.tar.gz)
if [[ "$target" == all || "$target" == linux ]]; then checksum_files+=(artifacts/PdfImageExtractor-linux-x64.tar.gz); fi
if [[ "$target" == all || "$target" == windows ]]; then checksum_files+=(artifacts/PdfImageExtractor-Setup-x64.exe); fi
sha256sum "${checksum_files[@]}" > artifacts/SHA256SUMS
printf 'Build complete: %s/artifacts\n' "$project_dir"
