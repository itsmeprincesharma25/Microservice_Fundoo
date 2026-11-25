#!/bin/bash
# ============================================================
# Fundoo Microservices – Full Endpoint Test Suite
# Run on Azure VM as: bash scripts/test-endpoints.sh
# ============================================================
set -e

BASE="http://localhost:30080"
PASS=0
FAIL=0

check() {
    local label="$1"
    local expected="$2"
    local actual="$3"
    if echo "$actual" | grep -q "HTTP/$expected\|HTTP/1.1 $expected\|HTTP/2 $expected"; then
        echo "  ✅ $label → $expected"
        ((PASS++))
    else
        STATUS=$(echo "$actual" | grep -oP 'HTTP/\S+ \K\d+' | head -1)
        echo "  ❌ $label → expected $expected, got ${STATUS:-unknown}"
        ((FAIL++))
    fi
}

echo "======================================================"
echo "  Fundoo API Endpoint Test Suite"
echo "  Base URL: $BASE"
echo "======================================================"

# ── Auth ──────────────────────────────────────────────────
echo ""
echo "[ AUTH ENDPOINTS ]"

TEST_EMAIL="e2e_$(date +%s)@fundoo.local"
TEST_PASS="Password@123"

REGISTER=$(curl -si -X POST "$BASE/api/auth/register" \
  -H "Content-Type: application/json" \
  -d "{\"fullName\":\"E2E User\",\"email\":\"$TEST_EMAIL\",\"password\":\"$TEST_PASS\"}")
check "POST /api/auth/register → 201" "201" "$REGISTER"

LOGIN=$(curl -si -X POST "$BASE/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$TEST_EMAIL\",\"password\":\"$TEST_PASS\"}")
check "POST /api/auth/login → 200" "200" "$LOGIN"

LOGIN_BODY=$(echo "$LOGIN" | sed -n '/^\r\?$/,$p' | tail -n +2)
TOKEN=$(echo "$LOGIN_BODY" | python3 -c 'import sys,json; d=json.load(sys.stdin); print(d.get("accessToken",""))' 2>/dev/null)
REFRESH=$(echo "$LOGIN_BODY" | python3 -c 'import sys,json; d=json.load(sys.stdin); print(d.get("refreshToken",""))' 2>/dev/null)

if [ -z "$TOKEN" ]; then
    echo "  ⚠️  Could not extract JWT token — remaining tests will fail"
    exit 1
fi
echo "  🔑 Token acquired (len=${#TOKEN})"

# Wrong password
WRONG=$(curl -si -X POST "$BASE/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$TEST_EMAIL\",\"password\":\"WrongPass\"}")
check "POST /api/auth/login (wrong pass) → 401" "401" "$WRONG"

# Sessions
SESSIONS=$(curl -si "$BASE/api/auth/sessions" \
  -H "Authorization: Bearer $TOKEN")
check "GET /api/auth/sessions → 200" "200" "$SESSIONS"

# Refresh token
REFRESH_RESP=$(curl -si -X POST "$BASE/api/auth/refresh-token" \
  -H "Content-Type: application/json" \
  -d "{\"refreshToken\":\"$REFRESH\"}")
check "POST /api/auth/refresh-token → 200" "200" "$REFRESH_RESP"

# ── Notes ─────────────────────────────────────────────────
echo ""
echo "[ NOTES ENDPOINTS ]"

# Create note
CREATE=$(curl -si -X POST "$BASE/api/notes" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"title":"E2E Note","content":"Testing all endpoints"}')
check "POST /api/notes → 201" "201" "$CREATE"

NOTE_ID=$(echo "$CREATE" | sed -n '/^\r\?$/,$p' | tail -n +2 | python3 -c 'import sys,json; print(json.load(sys.stdin).get("id",""))' 2>/dev/null)
echo "  📝 Note ID: $NOTE_ID"

# GET all notes
GET_ALL=$(curl -si "$BASE/api/notes" -H "Authorization: Bearer $TOKEN")
check "GET /api/notes → 200" "200" "$GET_ALL"

# GET by ID
GET_ONE=$(curl -si "$BASE/api/notes/$NOTE_ID" -H "Authorization: Bearer $TOKEN")
check "GET /api/notes/{id} → 200" "200" "$GET_ONE"

# Update note
UPDATE=$(curl -si -X PUT "$BASE/api/notes/$NOTE_ID" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"title":"Updated E2E Note","content":"Updated content","isPinned":false}')
check "PUT /api/notes/{id} → 204" "204" "$UPDATE"

# Archive / Unarchive
ARCH=$(curl -si -X PUT "$BASE/api/notes/$NOTE_ID/archive" -H "Authorization: Bearer $TOKEN")
check "PUT /api/notes/{id}/archive → 204" "204" "$ARCH"

GET_ARCH=$(curl -si "$BASE/api/notes/archived" -H "Authorization: Bearer $TOKEN")
check "GET /api/notes/archived → 200" "200" "$GET_ARCH"

UNARCH=$(curl -si -X PUT "$BASE/api/notes/$NOTE_ID/unarchive" -H "Authorization: Bearer $TOKEN")
check "PUT /api/notes/{id}/unarchive → 204" "204" "$UNARCH"

# Pin note (via update)
PIN=$(curl -si -X PUT "$BASE/api/notes/$NOTE_ID" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"title":"Pinned Note","content":"Pinned","isPinned":true}')
check "PUT /api/notes/{id} (pin) → 204" "204" "$PIN"

# Trash / Restore / Delete
TRASH=$(curl -si -X PUT "$BASE/api/notes/$NOTE_ID/trash" -H "Authorization: Bearer $TOKEN")
check "PUT /api/notes/{id}/trash → 204" "204" "$TRASH"

GET_TRASH=$(curl -si "$BASE/api/notes/trash" -H "Authorization: Bearer $TOKEN")
check "GET /api/notes/trash → 200" "200" "$GET_TRASH"

RESTORE=$(curl -si -X PUT "$BASE/api/notes/$NOTE_ID/restore" -H "Authorization: Bearer $TOKEN")
check "PUT /api/notes/{id}/restore → 204" "204" "$RESTORE"

# Trash again then permanently delete
curl -s -X PUT "$BASE/api/notes/$NOTE_ID/trash" -H "Authorization: Bearer $TOKEN" > /dev/null
DEL=$(curl -si -X DELETE "$BASE/api/notes/$NOTE_ID" -H "Authorization: Bearer $TOKEN")
check "DELETE /api/notes/{id} (trashed) → 204" "204" "$DEL"

# ── Labels ────────────────────────────────────────────────
echo ""
echo "[ LABELS ENDPOINTS ]"

# Create label
CREATE_LBL=$(curl -si -X POST "$BASE/api/labels" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"name":"E2E Label"}')
check "POST /api/labels → 201" "201" "$CREATE_LBL"

LABEL_ID=$(echo "$CREATE_LBL" | sed -n '/^\r\?$/,$p' | tail -n +2 | python3 -c 'import sys,json; print(json.load(sys.stdin).get("id",""))' 2>/dev/null)
echo "  🏷  Label ID: $LABEL_ID"

# GET all labels
GET_LBLS=$(curl -si "$BASE/api/labels" -H "Authorization: Bearer $TOKEN")
check "GET /api/labels → 200" "200" "$GET_LBLS"

# GET label by ID
GET_LBL=$(curl -si "$BASE/api/labels/$LABEL_ID" -H "Authorization: Bearer $TOKEN")
check "GET /api/labels/{id} → 200" "200" "$GET_LBL"

# Update label
UPD_LBL=$(curl -si -X PUT "$BASE/api/labels/$LABEL_ID" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"name":"E2E Label Updated"}')
check "PUT /api/labels/{id} → 204" "204" "$UPD_LBL"

# Create a note, attach label, detach label
NOTE2_BODY=$(curl -s -X POST "$BASE/api/notes" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"title":"Labeled Note","content":"For label attach test"}')
NOTE2_ID=$(echo "$NOTE2_BODY" | python3 -c 'import sys,json; print(json.load(sys.stdin).get("id",""))' 2>/dev/null)

ATTACH=$(curl -si -X PUT "$BASE/api/notes/$NOTE2_ID/labels/$LABEL_ID" -H "Authorization: Bearer $TOKEN")
check "PUT /api/notes/{id}/labels/{labelId} → 204" "204" "$ATTACH"

DETACH=$(curl -si -X DELETE "$BASE/api/notes/$NOTE2_ID/labels/$LABEL_ID" -H "Authorization: Bearer $TOKEN")
check "DELETE /api/notes/{id}/labels/{labelId} → 204" "204" "$DETACH"

# Delete label
DEL_LBL=$(curl -si -X DELETE "$BASE/api/labels/$LABEL_ID" -H "Authorization: Bearer $TOKEN")
check "DELETE /api/labels/{id} → 204" "204" "$DEL_LBL"

# ── Notifications ─────────────────────────────────────────
echo ""
echo "[ NOTIFICATION ENDPOINTS ]"

INTERNAL_KEY="FundooNotesSuperSecretInternalKey12345"

# Without key → 401
NO_KEY=$(curl -si -X POST "$BASE/api/notifications/email" \
  -H "Content-Type: application/json" \
  -d '{"to":"test@example.com","subject":"Test","body":"Hello"}')
check "POST /api/notifications/email (no key) → 401" "401" "$NO_KEY"

# With key → 202 (email delivery may fail but API accepts it)
WITH_KEY=$(curl -si -X POST "$BASE/api/notifications/email" \
  -H "Content-Type: application/json" \
  -H "X-Internal-Key: $INTERNAL_KEY" \
  -d '{"to":"test@example.com","subject":"E2E Test","body":"Hello from E2E"}')
check "POST /api/notifications/email (with key) → 202" "202" "$WITH_KEY"

# ── Summary ───────────────────────────────────────────────
echo ""
echo "======================================================"
TOTAL=$((PASS + FAIL))
echo "  RESULTS: $PASS/$TOTAL passed"
if [ "$FAIL" -eq 0 ]; then
    echo "  🎉 All endpoints working correctly!"
else
    echo "  ⚠️  $FAIL endpoint(s) failed — check logs above"
fi
echo "======================================================"
