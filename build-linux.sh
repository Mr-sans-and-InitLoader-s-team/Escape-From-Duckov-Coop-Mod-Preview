#!/bin/bash

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ -z "${DUCKOV_GAME_DIRECTORY:-}" ]; then
    echo "Error: DUCKOV_GAME_DIRECTORY is not set"
    echo "Example: DUCKOV_GAME_DIRECTORY='/home/tarsin/Game/Escape from Duckov'"
    exit 1
fi

cd "$SCRIPT_DIR"

echo "Building with DUCKOV_GAME_DIRECTORY=$DUCKOV_GAME_DIRECTORY"
echo ""

dotnet build EscapeFromDuckovCoopMod.sln -c Release \
    -p:OutputPath="$SCRIPT_DIR/artifacts" \
    -p:DUCKOV_GAME_DIRECTORY="$DUCKOV_GAME_DIRECTORY"

echo ""
echo "Build completed. DLLs:"
ls -la "$SCRIPT_DIR/artifacts/"*.dll
