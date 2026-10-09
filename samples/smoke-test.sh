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

# Cursor feed: follow the cursors through the whole table in both directions, one SQL command per page.
walk() { # walk <query> <cursor param> <cursor field> [start cursor] → prints the ids in visiting order, one page per line
  local query=$1 param=$2 field=$3 cursor=${4:-} headers body pages=0
  while :; do
    headers=$(mktemp)
    body=$(curl -s -D "$headers" "$URL/products/feed?$query${cursor:+&$param=$cursor}")
    [[ $(awk -F': ' 'tolower($1)=="x-db-commands"{gsub("\r","",$2); print $2}' "$headers") == 1 ]] || echo "MORE_THAN_ONE_QUERY"
    rm -f "$headers"
    jq -c '[.items[].id]' <<<"$body"
    cursor=$(jq -r ".$field // empty" <<<"$body")
    pages=$((pages + 1))
    [[ -n $cursor && $pages -lt 100 ]] || break
  done
}
expect() { # expect <description> <condition result>
  if [[ $2 == true ]]; then echo "  PASS  $1"; else echo "  FAIL  $1"; FAILED=1; fi
}

forward=$(walk "pageSize=10" after nextCursor)
expect "feed: forward walk visits ids 1..95 once, in 10 pages" "$(jq -s 'add == [range(1;96)] and length == 10' <<<"$forward" 2>/dev/null)"
expect "feed: every forward page runs exactly 1 SQL command" "$([[ $forward != *MORE_THAN_ONE_QUERY* ]] && echo true)"
after_90=$(curl -s "$URL/products/feed?pageSize=90" | jq -r .nextCursor)
last_page_previous=$(curl -s "$URL/products/feed?pageSize=10&after=$after_90" | jq -r .previousCursor)
backward=$(walk "pageSize=10" before previousCursor "$last_page_previous")
expect "feed: backward walk from the last page visits ids 90..1 in 9 pages" \
  "$(jq -s 'reverse | add == [range(1;91)] and length == 9' <<<"$backward" 2>/dev/null)"
sorted=$(walk "pageSize=40&sort=-price" after nextCursor)
expect "feed: sort=-price walks 95..1" "$(jq -s 'add == [range(95;0;-1)]' <<<"$sorted" 2>/dev/null)"
check "feed: first page has no previous cursor" \
  '(.items | map(.id)) == [1,2,3] and .hasNextPage and (.hasPreviousPage | not) and .previousCursor == null' "/products/feed?pageSize=3" 200 1
next=$(curl -s "$URL/products/feed?pageSize=3" | jq -r .nextCursor)
check "feed: next page via after" '(.items | map(.id)) == [4,5,6] and .hasPreviousPage' "/products/feed?pageSize=3&after=$next" 200 1
check "feed: a tampered cursor is a 400" '.status == 400 and (.errors | has("after"))' "/products/feed?after=${next}x" 400
check "feed: a cursor from another sort is a 400" \
  '.status == 400 and (.detail | test("different sort"))' "/products/feed?after=$next&sort=-price" 400
check "feed: after and before together is a 400" \
  '.status == 400 and (.errors | has("before"))' "/products/feed?after=$next&before=$next" 400

# Auditing + soft delete. Runs last: it changes the data the checks above rely on.
send() { # send <method> <path> → prints the status code; acts as user "smoke-tester"
  curl -s -o /dev/null -w '%{http_code}' -X "$1" -H "X-User: smoke-tester" "$URL$2"
}
check "audit: seeded rows have creation stamps and no user" \
  '.createdAt != null and .updatedAt == .createdAt and .createdBy == null and (.isDeleted | not)' "/products/95/audit"
expect "audit: PATCH renames (204)" "$([[ $(send PATCH "/products/95?name=Renamed") == 204 ]] && echo true)"
check "audit: update stamps updatedBy and keeps createdAt" \
  '.name == "Renamed" and .updatedBy == "smoke-tester" and .updatedAt > .createdAt and .createdBy == null' "/products/95/audit"
expect "soft delete: DELETE returns 204" "$([[ $(send DELETE /products/95) == 204 ]] && echo true)"
check "soft delete: the row is hidden from lists" '.totalCount == 94 and ((.items | map(.id)) | index(95) == null)' "/products?pageSize=100"
check "soft delete: the row is still in the table, flagged with who and when" \
  '.isDeleted and .deletedBy == "smoke-tester" and .deletedAt != null and .name == "Renamed"' "/products/95/audit"
expect "soft delete: a deleted row can't be deleted again (404)" "$([[ $(send DELETE /products/95) == 404 ]] && echo true)"

if [[ $FAILED == 0 ]]; then echo "All smoke checks passed."; else echo "Some checks failed — app log: samples/sample.log"; exit 1; fi
