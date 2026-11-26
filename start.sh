#!/usr/bin/env bash
# Startet die MAX! Cube Local App unter Linux (Gegenstück zu start.cmd).
# Voraussetzung: ein .NET-10-SDK (dotnet im PATH oder in einem der unten genannten Orte) und ein
# Desktop mit X11 oder Wayland. Ohne Desktop genügt: ./start.sh --selfcheck
set -euo pipefail
cd "$(dirname "$0")"

find_dotnet() {
  local candidate version
  for candidate in dotnet "$HOME/.dotnet/dotnet" "$HOME/dotnet/dotnet" /usr/share/dotnet/dotnet /usr/lib/dotnet/dotnet /opt/dotnet/dotnet; do
    if command -v "$candidate" >/dev/null 2>&1; then
      version="$("$candidate" --list-sdks 2>/dev/null | cut -d. -f1 | grep -x '10' | head -n1 || true)"
      if [ "$version" = "10" ]; then
        echo "$candidate"
        return 0
      fi
    fi
  done
  return 1
}

if ! DOTNET="$(find_dotnet)"; then
  echo "Kein .NET 10 SDK gefunden. Bitte installieren: https://dotnet.microsoft.com/download/dotnet/10.0" >&2
  exit 1
fi

# Ohne libicu (schlanke Container/Distributionen) startet .NET nur im Invariant-Modus.
if ! ldconfig -p 2>/dev/null | grep -q libicuuc; then
  export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
fi

# SkiaSharp/Avalonia laden Systembibliotheken zur Laufzeit; das Release-ZIP enthält
# keine Systembibliotheken. Der fensterlose Selbsttest braucht sie nicht.
# xdg-utils liefert xdg-open für externe Links; ohne Opener zeigt die App die URL als Status.
APT_PACKAGES="libfontconfig1 libx11-6 libxcursor1 libxext6 libxfixes3 libxi6 libxrandr2 libice6 libsm6 libgl1 libvulkan1 xdg-utils"
missing_libraries() {
  local lib missing=""
  for lib in libfontconfig.so.1 libX11.so.6 libXcursor.so.1 libXext.so.6 libXfixes.so.3 libXi.so.6 libXrandr.so.2 libICE.so.6 libSM.so.6 libGL.so.1 libvulkan.so.1; do
    ldconfig -p 2>/dev/null | grep -q "$lib" || missing="$missing $lib"
  done
  printf '%s' "$missing"
}

# Opt-in: der Nutzer gibt sein sudo-Passwort selbst ein; ohne Flag wird nie installiert.
if [[ " $* " == *" --install-deps "* ]]; then
  if [ ! -f /etc/debian_version ] || ! command -v apt-get >/dev/null 2>&1; then
    echo "--install-deps wird nur auf Debian/Ubuntu unterstützt." >&2
    exit 2
  fi
  echo "Installiere Systembibliotheken (sudo-Passwort erforderlich): $APT_PACKAGES"
  sudo apt-get update && sudo apt-get install -y $APT_PACKAGES || exit 2
  args=()
  for arg in "$@"; do
    [ "$arg" = "--install-deps" ] || args+=("$arg")
  done
  set -- "${args[@]}"
fi

if [[ " $* " != *" --selfcheck "* && " $* " != *" --check-linux-deps "* ]]; then
  missing="$(missing_libraries)"
  if [ -n "$missing" ]; then
    echo "FEHLER: fehlende Linux-Bibliotheken:$missing" >&2
    echo "Debian/Ubuntu: ./start.sh --install-deps (sudo-Passwort erforderlich)" >&2
    echo "oder: sudo apt-get update && sudo apt-get install -y $APT_PACKAGES" >&2
    exit 2
  fi
fi

echo "Verwende SDK: $DOTNET"
"$DOTNET" build MaxCube.slnx --nologo -v:minimal
echo "Starte MAX! Cube Local App ..."
exec "$DOTNET" src/MaxCube.UI.Desktop/bin/Debug/net10.0/MaxCube.UI.Desktop.dll "$@"
