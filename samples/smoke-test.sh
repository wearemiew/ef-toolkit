#!/usr/bin/env bash
# End-to-end check of the packed library inside a real ASP.NET Core app:
# packs it, starts the sample, calls every endpoint and asserts on the JSON, status codes and SQL command counts.
# Requires curl and jq.
set -euo pipefail
cd "$(dirname "$0")"

URL=http://localhost:5081
./pack-local.sh
dotnet build PaginationSample --nologo -v quiet
dotnet run --project PaginationSample --no-build --urls "$URL" > sample.log 2>&1 &
APP_PID=$!
trap 'kill $APP_PID 2>/dev/null || true' EXIT

for _ in $(seq 1 60); do curl -s -o /dev/null "$URL/products/optional" && break; sleep 0.5; done

FAILED=0
check() { # check <description> <jq expression that must be true, or - to skip the body> <path> [expected status] [expected X-Db-Commands]
  local name=$1 expr=$2 path=$3 want_status=${4:-200} want_cmds=${5:-}
  local headers body status cmds
  headers=$(mktemp); body=$(curl -s -D "$headers" "$URL$path")
  status=$(awk 'NR==1{print $2}' "$headers")
  cmds=$(awk -F': ' 'tolower($1)=="x-db-commands"{gsub("\r","",$2); print $2}' "$headers")
  rm -f "$headers"
  if [[ $status == "$want_status" ]] && { [[ $expr == - ]] || jq -e "$expr" <<<"$body" >/dev/null 2>&1; } \
     && [[ -z $want_cmds || $cmds == "$want_cmds" ]]; then
    echo "  PASS  $name"
  else
    echo "  FAIL  $name  (status $status, sql commands ${cmds:-?})"; echo "        $body" | head -c 400; echo
    FAILED=1
  fi
}

echo "Smoke testing $URL"
check "page 2 has items 11-20 and full metadata" \
  '.items | map(.id) == [11,12,13,14,15,16,17,18,19,20]' "/products?page=2&pageSize=10" 200 2
check "metadata serialized (totalCount, totalPages, flags)" \
  '.page == 2 and .pageSize == 10 and .totalCount == 95 and .totalPages == 10 and .hasPreviousPage and .hasNextPage' \
  "/products?page=2&pageSize=10"
check "short last page skips the COUNT query" \
  '(.items | length) == 5 and .totalCount == 95 and (.hasNextPage | not)' "/products?page=10&pageSize=10" 200 1
check "page past the end is empty but keeps the total" \
  '(.items | length) == 0 and .totalCount == 95' "/products?page=50&pageSize=10" 200 2
check "invalid page is a 400 naming the parameter" '.status == 400 and (.errors | has("page")) and (.detail | contains("(Parameter") | not)' "/products?page=0&pageSize=10" 400
check "skip overflow is a 400" '.status == 400' "/products?page=2147483647&pageSize=100" 400
check "optional paging without params returns everything" \
  '(.items | length) == 95 and .page == 1 and .pageSize == 95 and .totalPages == 1' "/products/optional" 200 1
check "optional paging with params pages" \
  '(.items | length) == 5 and .totalCount == 95' "/products/optional?page=1&pageSize=5"
check "projection + Map keeps metadata" \
  '.items[0].label == "Product 095 (105.00 EUR)" and .totalCount == 95' "/products/summaries?page=1&pageSize=3"
check "unordered query is rejected with an explanation" \
  '.status == 500 and (.detail | test("OrderBy"))' "/products/unordered" 500
check "generated SQL orders and pages" \
  '.sql | test("ORDER BY") and test("LIMIT") and test("OFFSET")' "/debug/sql?page=3&pageSize=10"
check "PageRequest defaults: page 1, size 20, ordered by id" \
  '.page == 1 and .pageSize == 20 and (.items | map(.id)) == ([range(1;21)])' "/products/search"
check "oversized pageSize is clamped to 100" \
  '.pageSize == 100 and (.items | length) == 95' "/products/search?pageSize=500"
check "sort=-price,name orders by price descending" \
  '(.items | map(.id)) == [95,94,93]' "/products/search?sort=-price,name&pageSize=3"
check "sort=name orders by name" \
  '.items[0].name == "Product 001" and .items[1].name == "Product 002"' "/products/search?sort=name&pageSize=2"
check "search filter narrows the total" \
  '.totalCount == 7 and (.items | map(.id)) == [9,90,91,92,93,94,95]' "/products/search?search=09"
check "unknown sort field is a 400 listing allowed fields" \
  '.status == 400 and (.errors | has("sort")) and (.detail | test("name, price"))' "/products/search?sort=secret" 400
check "invalid page in PageRequest is a 400" '.status == 400' "/products/search?page=0" 400
check "double sign in sort is rejected" '.status == 400' "/products/search?sort=--price" 400
check "a server bug is NOT reported as a bad request" - "/products/summaries?page=0&pageSize=5" 500

if [[ $FAILED == 0 ]]; then echo "All smoke checks passed."; else echo "Some checks failed — app log: samples/sample.log"; exit 1; fi
