#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
published="$repo_root/artifacts/gog-disc-companion-linux-x64"
if [[ -x "$repo_root/gog-disc-companion" ]]; then published="$repo_root"; fi
install_prefix="${GOG_DISC_INSTALL_PREFIX:-$HOME/.local}"
app_root="$install_prefix/lib/gog-disc-companion"
bin_root="$install_prefix/bin"
data_root="${XDG_DATA_HOME:-$HOME/.local/share}"
applications="$data_root/applications"
autostart="${XDG_CONFIG_HOME:-$HOME/.config}/autostart"
icons="$data_root/icons/hicolor/256x256/apps"

if [[ ! -x "$published/gog-disc-companion" ]]; then
  echo "Run ./publish-linux.sh first." >&2
  exit 1
fi
command -v python3 >/dev/null || { echo "Python 3 is required to install desktop entries." >&2; exit 1; }
mkdir -p "$app_root" "$bin_root" "$applications" "$autostart" "$icons"
cp -a "$published/." "$app_root/"
ln -sfn "$app_root/gog-disc-companion" "$bin_root/gog-disc-companion"
install -m 0644 "$published/AppIcon.png" "$icons/gog-disc-companion.png"
python3 - "$repo_root/linux" "$app_root/gog-disc-companion" "$applications" "$autostart" <<'PY'
import pathlib, sys
source, executable, applications, autostart = sys.argv[1:]
def value(s):
    return s.replace('\\', '\\\\').replace('\n', '\\n').replace('\r', '\\r').replace('\t', '\\t')
def argument(s):
    s = s.replace('%', '%%').replace('\\', '\\\\').replace('"', '\\"').replace('`', '\\`').replace('$', '\\$')
    return value('"' + s + '"')
for name, destination, suffix in [('gog-disc-companion.desktop', applications, ''),
                                   ('gog-disc-companion-autostart.desktop', autostart, ' --watch')]:
    text = (pathlib.Path(source) / name).read_text()
    text = '\n'.join('Exec=' + argument(executable) + suffix if line.startswith('Exec=') else line for line in text.splitlines()) + '\n'
    (pathlib.Path(destination) / 'gog-disc-companion.desktop').write_text(text)
PY
if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database "$applications" || true; fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then gtk-update-icon-cache -f -t "$data_root/icons/hicolor" || true; fi

echo "Installed GOG Disc Companion. Close a running companion before using an updated build."
echo "Open it from the application menu. Its media watcher starts at the next login."
