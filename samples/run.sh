#!/usr/bin/env bash
# Packs the library and runs the sample on http://localhost:5080 (try samples/PaginationSample/requests.http).
set -euo pipefail
cd "$(dirname "$0")"

./pack-local.sh
dotnet run --project PaginationSample --urls http://localhost:5080
