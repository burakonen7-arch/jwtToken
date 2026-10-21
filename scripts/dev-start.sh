#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -d /workspace/.dotnet ]]; then
  export PATH="/workspace/.dotnet:$PATH"
  export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/workspace/.dotnet-home}"
fi
set -a
source .env
set +a
docker compose up -d --wait
exec dotnet run --no-build --project src/JwtToken.Api
