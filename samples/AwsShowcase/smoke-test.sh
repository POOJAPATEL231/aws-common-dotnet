#!/usr/bin/env bash
# End-to-end smoke test: calls every AwsShowcase endpoint against a running
# API (http://localhost:8080) backed by LocalStack. Prints PASS/FAIL per call.
BASE="http://localhost:8080"
PASS=0; FAIL=0
declare -a FAILURES

# call METHOD PATH [BODY] — passes on HTTP 2xx
call() {
  local method="$1" path="$2" body="${3:-}"
  local code
  if [ -n "$body" ]; then
    code=$(curl -s -o /tmp/resp.txt -w "%{http_code}" -X "$method" "$BASE$path" \
      -H "Content-Type: application/json" -d "$body")
  else
    code=$(curl -s -o /tmp/resp.txt -w "%{http_code}" -X "$method" "$BASE$path")
  fi
  if [[ "$code" =~ ^2 ]]; then
    PASS=$((PASS+1)); printf "  PASS %-4s %s (%s)\n" "$method" "$path" "$code"
  else
    FAIL=$((FAIL+1)); FAILURES+=("$method $path -> $code: $(head -c 200 /tmp/resp.txt)")
    printf "  FAIL %-4s %s (%s)\n" "$method" "$path" "$code"
  fi
  cat /tmp/resp.txt
}

echo "### ORDERS (CQRS + outbox + cache)"
ORDER=$(curl -s -X POST "$BASE/api/orders" -H "Content-Type: application/json" \
  -d '{"customerEmail":"alice@example.com","productName":"Laptop","quantity":2,"price":999.99,"tags":["vip"]}')
echo "$ORDER"
OID=$(echo "$ORDER" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
echo "  created order id: $OID"
call GET "/api/orders/$OID"
call GET "/api/orders/by-customer/alice@example.com"
call GET "/api/orders?page=1&pageSize=10"
call GET "/api/orders/all?newestFirst=true"
call GET "/api/orders/search?minQuantity=1"
call GET "/api/orders/by-status/Pending"
call GET "/api/orders/latest/alice@example.com"
call GET "/api/orders/exists/alice@example.com"
call GET "/api/orders/partition/order"
call GET "/api/orders/report"
call GET "/api/orders/find?partitionKey=order&id=$OID"
call PUT "/api/orders/$OID/status?status=Paid"
call POST "/api/orders/batch" '[{"customerEmail":"bob@example.com","productName":"Mouse","quantity":3,"price":25}]'
call POST "/api/orders/discount/alice@example.com?percent=10"

echo "### DYNAMO SET (direct set + QueryExpression + change tracking)"
call GET "/api/dynamo-set/list"
call GET "/api/dynamo-set/count?minQuantity=1"
call GET "/api/dynamo-set/paged?minQuantity=1&page=1&pageSize=5"
call GET "/api/dynamo-set/items?partitionKey=order&minQuantity=1&limit=10"
call GET "/api/dynamo-set/query-expression?minQuantity=1&limit=25"
call POST "/api/dynamo-set/add?email=direct@example.com"
call POST "/api/dynamo-set/context-tracking-demo"

echo "### FILES (S3 + presigned)"
call POST "/api/files/showcase-files/batch?prefix=demo" '{"hello.txt":"hello world"}'
call GET "/api/files/showcase-files"
call GET "/api/files/presigned/download?path=showcase-files/demo/hello.txt&minutes=15"
call GET "/api/files/presigned/upload?path=showcase-files/up.txt&minutes=15"

echo "### CACHE"
call PUT "/api/cache/k1" '{"value":"cached-value"}'
call GET "/api/cache/k1"
call DELETE "/api/cache/k1"

echo "### CRYPTO"
call POST "/api/crypto/keys"
CIPHER=$(curl -s -X POST "$BASE/api/crypto/encrypt" -H "Content-Type: application/json" -d '{"value":"secret"}' | grep -o '"cipherText":"[^"]*"' | cut -d'"' -f4)
call POST "/api/crypto/decrypt" "{\"cipherText\":\"$CIPHER\"}"
call POST "/api/crypto/base64" '{"value":"text"}'
call POST "/api/crypto/hash" '{"value":"text"}'
call POST "/api/crypto/password/hash" '{"value":"p@ss"}'
call GET "/api/crypto/random-password?length=16&strong=true"

echo "### SECRETS (Secrets Manager + KMS)"
call PUT "/api/secrets/my-secret" '{"value":"super-secret-value"}'
call GET "/api/secrets/my-secret"

echo "### QUEUE (SQS direct)"
call POST "/api/queue/send" '{"text":"hi","number":1}'
call POST "/api/queue/send-batch" '[{"text":"a","number":1},{"text":"b","number":2}]'

echo "### SNS BUS (subscribe + publish + consume)"
call POST "/api/sns-bus/subscribe"
call POST "/api/sns-bus/publish?orderId=o1&email=x@y.z&product=Widget"

echo "### EVENTS (EventBridge)"
call POST "/api/events/order-created?orderId=o1&email=x@y.z&product=Widget"

echo "### TABLES (admin)"
call GET "/api/tables"
call POST "/api/tables/ensure"
call PUT "/api/tables/capacity?read=10&write=10"

echo "### STREAMS (Kinesis/Firehose)"
call POST "/api/streams/kinesis/showcase-stream" '{"event":"click"}'

echo "### LOCKS (DynamoDB distributed lock)"
call POST "/api/locks/nightly-job/run?leaseSeconds=30&workMs=100"

echo "### FEATURES / METRICS / UTILS"
call GET "/api/features"
call GET "/api/features/new-checkout"
call POST "/api/metrics/demo?name=Smoke"
call GET "/api/utils/json"
call GET "/api/utils/strings?sample=alpha,beta,gamma"
call GET "/api/utils/numbers"
call GET "/api/utils/bytes-and-files"

echo "### SUBSCRIPTION MANAGER"
call POST "/api/subscription-manager/lifecycle-demo"

echo ""
echo "================ SUMMARY ================"
echo "PASS: $PASS   FAIL: $FAIL"
if [ "$FAIL" -gt 0 ]; then
  echo "---- failures ----"
  printf '%s\n' "${FAILURES[@]}"
fi
