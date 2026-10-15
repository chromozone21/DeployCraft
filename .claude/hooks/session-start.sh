#!/bin/bash
# Démarrage des sessions Claude Code dans le cloud (Linux) : installe le SDK .NET 10 et restaure
# les dépendances, pour compiler, tester et vérifier la mise en forme avant de pousser.
# Sur un poste Windows, ce script ne fait rien.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

DOTNET_DIR="$HOME/.dotnet"
export PATH="$DOTNET_DIR:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
# Compiler sous Linux les projets net10.0-windows (service, console, tests).
export EnableWindowsTargeting=true

if ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  installer="$(mktemp)"
  curl -sSL https://dot.net/v1/dotnet-install.sh -o "$installer"
  bash "$installer" --channel 10.0 --install-dir "$DOTNET_DIR" >/dev/null
  rm -f "$installer"
fi

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export PATH=\"$DOTNET_DIR:\$PATH\""
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1'
    echo 'export EnableWindowsTargeting=true'
  } >> "$CLAUDE_ENV_FILE"
fi

dotnet restore "$CLAUDE_PROJECT_DIR/DeployCraft.slnx" --locked-mode >/dev/null
