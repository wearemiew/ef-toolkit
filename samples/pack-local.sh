#!/usr/bin/env bash
# Packs the library as Miew.EntityFramework.Toolkit 0.0.0-local into samples/.localfeed
# and clears the sample-local package cache so the sample restores the fresh build.
set -euo pipefail
cd "$(dirname "$0")"

rm -rf .localfeed .packages PaginationSample/obj PaginationSample/bin
dotnet pack ../EntityFrameworkToolKit/EntityFrameworkToolKit/EntityFrameworkToolKit.csproj \
  --configuration Release -p:PackageVersion=0.0.0-local --output .localfeed --nologo -v quiet
echo "Packed $(ls .localfeed)"
