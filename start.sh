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

echo "Verwende SDK: $DOTNET"
"$DOTNET" build MaxCube.slnx --nologo -v:minimal
echo "Starte MAX! Cube Local App ..."
exec "$DOTNET" src/MaxCube.UI.Desktop/bin/Debug/net10.0/MaxCube.UI.Desktop.dll "$@"
