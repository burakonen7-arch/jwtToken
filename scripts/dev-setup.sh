#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -d /workspace/.dotnet ]]; then
  export PATH="/workspace/.dotnet:$PATH"
  export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/workspace/.dotnet-home}"
fi
if [[ ! -f .env ]]; then
  umask 077
  python3 - <<'ENV'
import secrets
from pathlib import Path
password = secrets.token_hex(24)
Path('.env').write_text('POSTGRES_PASSWORD='+password+'\nConnectionStrings__DefaultConnection=\"Host=localhost;Port=5432;Database=jwt_learning;Username=jwtapp;Password='+password+'\"\nJwt__SigningKey='+secrets.token_hex(64)+'\nASPNETCORE_ENVIRONMENT=Development\nASPNETCORE_URLS=http://127.0.0.1:5080\n')
ENV
fi
set -a
source .env
set +a
docker compose up -d --wait
dotnet tool restore
dotnet restore --locked-mode
dotnet build --no-restore
dotnet run --no-build --project src/JwtToken.Api -- --migrate
