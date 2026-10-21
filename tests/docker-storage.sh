#!/bin/sh
set -eu
site=${1:?Usage: docker-storage.sh /path/to/4ill}
repo=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
site=$(CDPATH= cd -- "$site" && pwd)
validation_db=fx_validation_$(date +%Y%m%d%H%M%S)_$$
cd "$site"
cleanup() { docker compose exec -T db mysql -uroot -proot -e "DROP DATABASE IF EXISTS $validation_db"; }
trap cleanup EXIT
# Only the local Docker service from docker/README.md is used.
docker compose exec -T db mysql -uroot -proot -e "CREATE DATABASE $validation_db"
docker compose exec -T db mysql -uroot -proot "$validation_db" < "$repo/sql/20261022_0001_fx_storage.sql"
docker compose exec -T db mysql -uroot -proot "$validation_db" < "$repo/tests/StoreValidation/known_players.sql"
FX_TEST_CONNECTION="Server=127.0.0.1;Port=3307;Database=$validation_db;User ID=root;Password=root"
export FX_TEST_CONNECTION
dotnet run --project "$repo/tests/StoreValidation" -c Release
