#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
output="$repo_root/artifacts/gog-disc-companion-linux-x64"
umu_version="1.4.4"
umu_sha256="eb590691841f7fad3fc3ad8fd5db4ccb87849fe7948e62b28ece7a4ee48cc851"
umu_archive="$repo_root/artifacts/umu-launcher-$umu_version-zipapp.tar"

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

if [[ ! -f "$umu_archive" ]]; then
  curl --fail --location --silent --show-error \
    "https://github.com/Open-Wine-Components/umu-launcher/releases/download/$umu_version/umu-launcher-$umu_version-zipapp.tar" \
    -o "$umu_archive"
fi
echo "$umu_sha256  $umu_archive" | sha256sum --check --status
mkdir -p "$output/umu"
tar -xf "$umu_archive" -C "$output" umu/umu-run umu/umu_run.py
chmod +x "$output/umu/umu-run"
"$output/umu/umu-run" --version

mkdir -p "$output/linux"
cp "$repo_root"/linux/*.desktop "$output/linux/"
cp "$repo_root/install-linux.sh" "$output/install.sh"
cp "$repo_root/linux/install-gui.sh" "$output/install-gui.sh"
cp "$repo_root/linux/Install GOG Disc Companion.desktop" "$output/Install GOG Disc Companion.desktop"
chmod +x "$output/Install GOG Disc Companion.desktop"
cp "$repo_root/docs/LINUX-COMPANION.md" "$output/README.md"
cp "$repo_root/LICENSE" "$output/LICENSE"
archive="$repo_root/artifacts/gog-disc-companion-linux-x64-preview.tar.gz"
tar -czf "$archive" -C "$repo_root/artifacts" gog-disc-companion-linux-x64
(cd "$repo_root/artifacts" && sha256sum gog-disc-companion-linux-x64-preview.tar.gz > gog-disc-companion-linux-x64-preview.tar.gz.sha256)
echo "Published to $output"
echo "Preview archive: $archive"
