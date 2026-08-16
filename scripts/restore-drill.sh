#!/usr/bin/env bash
#
# restore-drill.sh — prove a backup can be restored, by doing it.
#
# A backup nobody has restored is a hope. This runs the whole thing end to end, using exactly the
# commands docs/RUNBOOK.md tells an operator to use:
#
#   1. stands up a deployment (Postgres + the API image) with its own database and attachment volume
#   2. does a shop's work through the HTTP API — customer, job, visit, photograph, invoice
#   3. backs it up: pg_dump of the database, tar of the attachment volume
#   4. restores both into a *scratch* database and volume, and serves them with a second API
#   5. checks the restored system by reading the same business back — including the photograph's
#      bytes, which are the half that does not live in Postgres
#
# Everything it creates is named `opendispatch-drill-*` and removed on exit, including on failure.
# It touches nothing belonging to `docker compose` or to a developer's own database.
#
# Needs: docker, curl, jq. Takes a few minutes, most of it waiting for containers.

set -euo pipefail

readonly IMAGE="${IMAGE:-opendispatch-api:local}"
readonly POSTGRES_IMAGE="imresamu/postgis:17-3.6"

readonly PREFIX="opendispatch-drill"
readonly NETWORK="${PREFIX}-net"
readonly SOURCE_DB="${PREFIX}-source-db"
readonly SOURCE_API="${PREFIX}-source-api"
readonly SOURCE_FILES="${PREFIX}-source-files"
readonly RESTORED_DB="${PREFIX}-restored-db"
readonly RESTORED_API="${PREFIX}-restored-api"
readonly RESTORED_FILES="${PREFIX}-restored-files"

readonly SOURCE_PORT=8091
readonly RESTORED_PORT=8092

# Credentials for the drill only. They exist for the length of this script and are destroyed with
# the containers; nothing here is a secret worth protecting, and nothing outside can reach it.
readonly DB_PASSWORD="drill"
readonly SIGNING_KEY="restore-drill-signing-key-not-for-production-use"
readonly ADMIN="drill-admin@example.test"
readonly TECHNICIAN="drill-tech@example.test"
readonly PASSWORD="restore-drill-password"
readonly ORGANIZATION="Drill Heating"

readonly WORK="$(mktemp -d "${TMPDIR:-/tmp}/opendispatch-drill.XXXXXX")"

say() { printf '\n\033[1m== %s\033[0m\n' "$*"; }
note() { printf '   %s\n' "$*"; }
die() { printf '\n\033[31mFAILED: %s\033[0m\n' "$*" >&2; exit 1; }

cleanup() {
    local status=$?

    say "Cleaning up"
    docker rm -f "$SOURCE_API" "$RESTORED_API" "$SOURCE_DB" "$RESTORED_DB" >/dev/null 2>&1 || true
    docker volume rm "$SOURCE_FILES" "$RESTORED_FILES" >/dev/null 2>&1 || true
    docker network rm "$NETWORK" >/dev/null 2>&1 || true
    rm -rf "$WORK"

    if [ "$status" -eq 0 ]; then
        printf '\n\033[32mRestore drill passed.\033[0m A backup taken from a serving deployment was restored into an empty database and served the same business back, photograph included.\n'
    fi

    return "$status"
}
trap cleanup EXIT

for tool in docker curl jq; do
    command -v "$tool" >/dev/null 2>&1 || die "$tool is not installed."
done

# ── The deployment under test ──────────────────────────────────────────────────────────────────

say "Building the image"
docker image inspect "$IMAGE" >/dev/null 2>&1 || docker build -t "$IMAGE" .
note "$IMAGE"

docker network create "$NETWORK" >/dev/null

# One function for both halves of the drill, because the point is that a restored deployment is
# started exactly the way the original was — nothing special, no repair step.
start_database() {
    local name=$1

    docker run -d --name "$name" --network "$NETWORK" \
        -e POSTGRES_DB=opendispatch -e POSTGRES_USER=opendispatch -e POSTGRES_PASSWORD="$DB_PASSWORD" \
        "$POSTGRES_IMAGE" >/dev/null

    # Over TCP rather than `pg_isready`, and the difference matters: the Postgres entrypoint runs a
    # temporary server on the unix socket while it initialises, which answers `pg_isready` several
    # seconds before the real one is listening. Asking for a query over the port is asking the
    # server the API will actually connect to.
    for _ in $(seq 1 60); do
        if docker exec "$name" psql -h 127.0.0.1 -U opendispatch -d opendispatch -c 'select 1' >/dev/null 2>&1; then
            return 0
        fi
        sleep 1
    done

    die "$name never became ready."
}

start_api() {
    local name=$1 database=$2 files=$3 port=$4

    docker run -d --name "$name" --network "$NETWORK" -p "$port:8080" -v "$files:/var/lib/opendispatch/attachments" \
        -e ASPNETCORE_ENVIRONMENT=Production \
        -e Database__ConnectionString="Host=$database;Port=5432;Database=opendispatch;Username=opendispatch;Password=$DB_PASSWORD" \
        -e Jwt__SigningKey="$SIGNING_KEY" \
        "$IMAGE" >/dev/null

    for _ in $(seq 1 60); do
        if [ "$(curl -s -o /dev/null -w '%{http_code}' "http://localhost:$port/health/ready" || true)" = "200" ]; then
            return 0
        fi
        sleep 1
    done

    docker logs "$name" | tail -30 >&2
    die "$name never became ready."
}

# The two verbs a deployment runs: the same image with a different command.
migrate() {
    docker run --rm --network "$NETWORK" \
        -e ASPNETCORE_ENVIRONMENT=Production \
        -e Database__ConnectionString="Host=$1;Port=5432;Database=opendispatch;Username=opendispatch;Password=$DB_PASSWORD" \
        -e Jwt__SigningKey="$SIGNING_KEY" \
        "$IMAGE" migrate >/dev/null
}

create_user() {
    docker run --rm --network "$NETWORK" \
        -e ASPNETCORE_ENVIRONMENT=Production \
        -e Database__ConnectionString="Host=$1;Port=5432;Database=opendispatch;Username=opendispatch;Password=$DB_PASSWORD" \
        -e Jwt__SigningKey="$SIGNING_KEY" \
        "$IMAGE" create-user --username "$2" --org "$ORGANIZATION" --role "$3" --password "$PASSWORD" ${4:+--technician "$4"} >/dev/null
}

# One request, with the response body kept: `curl -sf` throws away exactly what a failing drill
# needs to say, which is what the API answered and why.
http() {
    local method=$1 port=$2 token=$3 path=$4 body=${5:-}
    local out="$WORK/response" code

    if [ -n "$body" ]; then
        code="$(curl -s -o "$out" -w '%{http_code}' -X "$method" "http://localhost:$port$path" \
            -H "Authorization: Bearer $token" -H 'Content-Type: application/json' -d "$body")"
    else
        code="$(curl -s -o "$out" -w '%{http_code}' -X "$method" "http://localhost:$port$path" \
            -H "Authorization: Bearer $token")"
    fi

    case "$code" in
        2*) cat "$out" ;;
        *) die "$method $path answered $code: $(head -c 400 "$out")" ;;
    esac
}

login() {
    local out="$WORK/login" code

    code="$(curl -s -o "$out" -w '%{http_code}' -X POST "http://localhost:$1/auth/login" \
        -H 'Content-Type: application/json' \
        -d "{\"username\":\"$2\",\"password\":\"$PASSWORD\"}")"

    [ "$code" = "200" ] || die "signing in as $2 answered $code: $(head -c 400 "$out")"

    jq -r .token < "$out"
}

post() { http POST "$1" "$2" "$3" "$4"; }
get() { http GET "$1" "$2" "$3"; }

say "Standing up the source deployment"
start_database "$SOURCE_DB"
migrate "$SOURCE_DB"
create_user "$SOURCE_DB" "$ADMIN" Admin
docker volume create "$SOURCE_FILES" >/dev/null
start_api "$SOURCE_API" "$SOURCE_DB" "$SOURCE_FILES" "$SOURCE_PORT"
note "serving on http://localhost:$SOURCE_PORT"

# ── A shop's day, through the API ──────────────────────────────────────────────────────────────

say "Doing a shop's work"
admin_token="$(login "$SOURCE_PORT" "$ADMIN")"
[ -n "$admin_token" ] || die "could not sign in as the admin."

window_start="$(date -u -v+1H '+%Y-%m-%dT%H:00:00Z' 2>/dev/null || date -u -d '+1 hour' '+%Y-%m-%dT%H:00:00Z')"
window_end="$(date -u -v+5H '+%Y-%m-%dT%H:00:00Z' 2>/dev/null || date -u -d '+5 hours' '+%Y-%m-%dT%H:00:00Z')"

technician_id="$(post "$SOURCE_PORT" "$admin_token" /technicians \
    "{\"name\":\"Sam Rivera\",\"skills\":[\"hvac\"],\"shiftStart\":\"$window_start\",\"shiftEnd\":\"$window_end\",\"latitude\":51.5074,\"longitude\":-0.1278}" | jq -r .id)"

create_user "$SOURCE_DB" "$TECHNICIAN" Technician "$technician_id"
technician_token="$(login "$SOURCE_PORT" "$TECHNICIAN")"

customer_id="$(post "$SOURCE_PORT" "$admin_token" /customers \
    '{"name":"Dana Whitlock","email":"dana@whitlock.example","phone":"+44 20 7946 0000"}' | jq -r .id)"

location_id="$(post "$SOURCE_PORT" "$admin_token" "/customers/$customer_id/locations" \
    '{"label":"Home","address":"12 Rillington Place, London","latitude":51.5074,"longitude":-0.1278}' | jq -r .id)"

job_id="$(post "$SOURCE_PORT" "$admin_token" /jobs \
    "{\"customerId\":\"$customer_id\",\"locationId\":\"$location_id\",\"requiredSkill\":\"hvac\",\"priority\":\"Normal\",\"windowStart\":\"$window_start\",\"windowEnd\":\"$window_end\",\"estimatedDuration\":\"01:00:00\"}" | jq -r .id)"

post "$SOURCE_PORT" "$admin_token" "/jobs/$job_id/assign" \
    "{\"technicianId\":\"$technician_id\",\"scheduledStart\":\"$window_start\"}" >/dev/null

for status in Dispatched EnRoute InProgress Completed; do
    post "$SOURCE_PORT" "$admin_token" "/jobs/$job_id/status" "{\"status\":\"$status\"}" >/dev/null
done

# The photograph: the one piece of a shop's data that is not in Postgres, and therefore the one a
# database-only backup silently loses.
attachment_id="$(uuidgen | tr '[:upper:]' '[:lower:]')"
printf 'a photograph of somebody boiler cupboard, %s' "$(date -u +%s)" > "$WORK/photo.jpg"
photo_digest="$(shasum -a 256 "$WORK/photo.jpg" | cut -d' ' -f1)"

upload_code="$(curl -s -o "$WORK/upload" -w '%{http_code}' -X POST \
    "http://localhost:$SOURCE_PORT/jobs/$job_id/attachments" \
    -H "Authorization: Bearer $technician_token" \
    -F "AttachmentId=$attachment_id" -F "JobId=$job_id" -F "Kind=Photo" \
    -F "file=@$WORK/photo.jpg;type=image/jpeg")"

case "$upload_code" in
    2*) ;;
    *) die "uploading the photograph answered $upload_code: $(head -c 400 "$WORK/upload")" ;;
esac

invoice="$(post "$SOURCE_PORT" "$admin_token" "/jobs/$job_id/invoice" \
    '{"lines":[{"kind":"Labor","description":"Two hours","quantity":2,"unitPrice":65},{"kind":"Part","description":"Thermostat","quantity":1,"unitPrice":120}]}')"

invoice_id="$(echo "$invoice" | jq -r .id)"
invoice_total="$(echo "$invoice" | jq -r .total)"

note "customer $customer_id, job $job_id, invoice $invoice_id totalling $invoice_total"
note "photograph $attachment_id, sha256 ${photo_digest:0:16}…"

# ── The backup ─────────────────────────────────────────────────────────────────────────────────
#
# Exactly the two commands the runbook gives, and they are two because a shop's data is in two
# places. Taken while the source is still serving, which is the case a backup has to survive.

say "Taking a backup"

# The whole database, extensions included. Not `-n public`, which looks tidier and is a trap: schema
# filtering drops the `CREATE EXTENSION postgis` with everything else, and the restore then fails on
# the first column of type `geography` — a backup that appears to work and cannot be restored.
docker exec "$SOURCE_DB" pg_dump -U opendispatch -d opendispatch -Fc > "$WORK/opendispatch.dump"
docker run --rm -v "$SOURCE_FILES:/data:ro" -v "$WORK:/backup" busybox \
    tar czf /backup/attachments.tar.gz -C /data . >/dev/null

note "database $(du -h "$WORK/opendispatch.dump" | cut -f1), attachments $(du -h "$WORK/attachments.tar.gz" | cut -f1)"

# ── The restore ────────────────────────────────────────────────────────────────────────────────

say "Restoring into a scratch deployment"
start_database "$RESTORED_DB"

# Into an *empty* database, made from template0 rather than from the image's initialised one. A
# restore over a database that already carries PostGIS fights the dump for the same objects and
# reports errors an operator then has to grade; restoring into nothing is unambiguous — either the
# whole dump applied or it did not. The dump brings its own `CREATE EXTENSION postgis` with it.
docker exec "$RESTORED_DB" psql -U opendispatch -d postgres -c 'DROP DATABASE opendispatch' >/dev/null
docker exec "$RESTORED_DB" createdb -U opendispatch -T template0 opendispatch

docker cp "$WORK/opendispatch.dump" "$RESTORED_DB:/tmp/opendispatch.dump"
# `--clean --if-exists` so the restore drops what it is about to create rather than colliding with
# it — a template0 database still has a `public` schema, and the dump carries its own. With that,
# `--exit-on-error` is honest: any error at all is a failed restore, and an operator never has to
# grade a list of them.
docker exec "$RESTORED_DB" pg_restore -U opendispatch -d opendispatch \
    --no-owner --clean --if-exists --exit-on-error /tmp/opendispatch.dump

docker volume create "$RESTORED_FILES" >/dev/null
docker run --rm -v "$RESTORED_FILES:/data" -v "$WORK:/backup:ro" busybox \
    tar xzf /backup/attachments.tar.gz -C /data >/dev/null

# No `migrate` here, deliberately: a restored dump carries the schema and the migration history, and
# running it would prove nothing about the backup. The API starting at all is the check that the two
# agree — it refuses to serve against a schema it does not recognise.
start_api "$RESTORED_API" "$RESTORED_DB" "$RESTORED_FILES" "$RESTORED_PORT"
note "serving on http://localhost:$RESTORED_PORT"

# ── Reading the business back out of the restored system ───────────────────────────────────────

say "Checking the restored system"

restored_token="$(login "$RESTORED_PORT" "$ADMIN")"
[ -n "$restored_token" ] && [ "$restored_token" != "null" ] || die "the restored system has no logins — the users table did not come across."
note "signed in with the same credentials"

restored_job="$(get "$RESTORED_PORT" "$restored_token" "/jobs/$job_id")"
[ "$(echo "$restored_job" | jq -r .status)" = "Invoiced" ] || die "the job came back as $(echo "$restored_job" | jq -r .status), not Invoiced."
[ "$(echo "$restored_job" | jq -r .customerId)" = "$customer_id" ] || die "the job points at a different customer."
note "the job is where it was left"

export_json="$(get "$RESTORED_PORT" "$restored_token" /export)"
restored_total="$(echo "$export_json" | jq -r ".invoices[] | select(.id==\"$invoice_id\") | .total")"
[ "$restored_total" = "$invoice_total" ] || die "the invoice totals $restored_total, not $invoice_total."
note "the invoice still totals $restored_total"

restored_customer="$(echo "$export_json" | jq -r ".customers[] | select(.id==\"$customer_id\") | .name")"
[ "$restored_customer" = "Dana Whitlock" ] || die "the customer came back as '$restored_customer'."
note "the customer is intact"

# The half a database-only backup loses. Compared byte for byte rather than by size: a truncated
# photograph and a whole one are both files.
curl -sf "http://localhost:$RESTORED_PORT/attachments/$attachment_id/content" \
    -H "Authorization: Bearer $restored_token" -o "$WORK/restored.jpg"

restored_digest="$(shasum -a 256 "$WORK/restored.jpg" | cut -d' ' -f1)"
[ "$restored_digest" = "$photo_digest" ] || die "the photograph came back different: $restored_digest, expected $photo_digest."
note "the photograph is byte-for-byte the one that was taken"

# And the restored system is a working deployment, not a museum: it can still be written to.
second_customer="$(post "$RESTORED_PORT" "$restored_token" /customers '{"name":"Whitlock Heating","email":null,"phone":null}' | jq -r .id)"
[ -n "$second_customer" ] && [ "$second_customer" != "null" ] || die "the restored system cannot take a new customer."
note "and it still accepts new work"
