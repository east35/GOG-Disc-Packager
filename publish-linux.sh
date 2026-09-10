#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
output="$repo_root/artifacts/gog-disc-companion-linux-x64"

if command -v dotnet >/dev/null 2>&1; then
  dotnet publish "$repo_root/src/GogDisc.Companion/GogDisc.Companion.csproj" \
    -c Release -r linux-x64 --self-contained true -o "$output"
else
  podman run --rm --security-opt label=disable \
    -v "$repo_root:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 \
    dotnet publish src/GogDisc.Companion/GogDisc.Companion.csproj \
      -c Release -r linux-x64 --self-contained true \
      -o /src/artifacts/gog-disc-companion-linux-x64
fi

mkdir -p "$output/linux"
cp "$repo_root"/linux/*.desktop "$output/linux/"
cp "$repo_root/install-linux.sh" "$output/install.sh"
cp "$repo_root/docs/LINUX-COMPANION.md" "$output/README.md"
cp "$repo_root/LICENSE" "$output/LICENSE"
archive="$repo_root/artifacts/gog-disc-companion-linux-x64-preview.tar.gz"
tar -czf "$archive" -C "$repo_root/artifacts" gog-disc-companion-linux-x64
(cd "$repo_root/artifacts" && sha256sum gog-disc-companion-linux-x64-preview.tar.gz > gog-disc-companion-linux-x64-preview.tar.gz.sha256)
echo "Published to $output"
echo "Preview archive: $archive"
