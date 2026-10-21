#!/bin/sh
set -eu
cd "$(dirname "$0")"
dotnet build EffectsFilter -c Release --nologo
# Output stays local; this script never connects to a game server.
plugin=dist/addons/counterstrikesharp/plugins/4ill.EffectsFilter
shared=dist/addons/counterstrikesharp/shared/FxFilterApi
mkdir -p "$plugin" "$shared" dist/cfg
cp EffectsFilter/bin/Release/net10.0/4ill.EffectsFilter.dll "$plugin/"
cp EffectsFilter/bin/Release/net10.0/4ill.EffectsFilter.deps.json "$plugin/"
cp EffectsFilter/bin/Release/net10.0/MySqlConnector.dll "$plugin/"
cp FxFilterApi/bin/Release/net10.0/FxFilterApi.dll "$shared/"
cp cfg/4ill_fx.cfg dist/cfg/
