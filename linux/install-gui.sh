#!/usr/bin/env bash
set -u

bundle="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
installer="$bundle/install.sh"
prompt=$'Install GOG Disc Companion for this user?\n\nIt will watch for discs in the background now and at future logins. Open it from the application menu to manage your library.'

if command -v kdialog >/dev/null 2>&1; then
  dialog=kdialog
  kdialog --title "GOG Disc Companion" --yesno "$prompt" || exit 0
elif command -v zenity >/dev/null 2>&1; then
  dialog=zenity
  zenity --question --title="GOG Disc Companion" --text="$prompt" || exit 0
else
  echo "A graphical dialog tool (kdialog or zenity) is required. Run ./install.sh from a terminal instead." >&2
  exit 1
fi

log="$(mktemp)" || exit 1
trap 'rm -f "$log"' EXIT
if bash "$installer" >"$log" 2>&1; then
  installed="${GOG_DISC_INSTALL_PREFIX:-$HOME/.local}/lib/gog-disc-companion/gog-disc-companion"
  nohup "$installed" --watch >/dev/null 2>&1 </dev/null &
  if [[ "$dialog" == kdialog ]]; then
    kdialog --title "GOG Disc Companion" --msgbox "Installed successfully. The companion is watching for discs in the background. Open it from the application menu whenever you want to manage your library."
  else
    zenity --info --title="GOG Disc Companion" --text="Installed successfully. The companion is watching for discs in the background. Open it from the application menu whenever you want to manage your library."
  fi
else
  result=$?
  failure="Installation failed (exit $result)."$'\n\n'"$(cat "$log")"
  if [[ "$dialog" == kdialog ]]; then
    kdialog --title "GOG Disc Companion" --error "$failure"
  else
    zenity --error --title="GOG Disc Companion" --text="$failure"
  fi
  exit "$result"
fi
