#!/usr/bin/env bash
#
# measure.sh — what this system's read paths cost on a shop with a year behind it.
#
# Against a *running* host, seeded with `seed --scale big`:
#
#   dotnet run -c Release --project src/Api --no-launch-profile     # a host on :5000
#   dotnet run --project src/Api -- seed --scale big                # ~12,000 jobs of history
#   scripts/measure.sh
#
# It measures the four paths a shop waits on — a phone's first sync, a phone's routine sync, the
# dispatch board, and planning a day — plus the export, whose cost is its duration and the memory it
# holds rather than its latency. Then it does the four again with ten phones polling in the
# background, because a number measured on an idle host is a number nobody will ever see.
#
# Percentiles rather than averages: a mean hides the request that made somebody reload the page.
#
# Needs: curl, jq, python3. Nothing it does writes to the database except /schedule/optimize, which
# re-plans the same day it planned last time.

set -euo pipefail

# Job control off: the background pullers below are killed at the end, and an interactive shell
# announces every one of them ("Terminated"), which reads like a failure at the bottom of a report.
set +m

API="${API:-http://localhost:5000}"
RUNS="${RUNS:-20}"
PULLERS="${PULLERS:-10}"
PASSWORD="${PASSWORD:-demo}"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/opendispatch-measure.XXXXXX")"
trap 'rm -rf "$WORK"; jobs -p | xargs -r kill 2>/dev/null || true' EXIT

say() { printf '\n\033[1m%s\033[0m\n' "$*"; }
die() { printf '\n\033[31mFAILED: %s\033[0m\n' "$*" >&2; exit 1; }

for tool in curl jq python3; do
    command -v "$tool" >/dev/null 2>&1 || die "$tool is not installed."
done

login() {
    local out="$WORK/login" code
    code="$(curl -s -o "$out" -w '%{http_code}' -X POST "$API/auth/login" \
        -H 'Content-Type: application/json' -d "{\"username\":\"$1\",\"password\":\"$PASSWORD\"}")"

    [ "$code" = "200" ] || die "signing in as $1 answered $code: $(head -c 300 "$out"). Is the host running and seeded?"

    jq -r .token < "$out"
}

# One timed request. Prints the seconds curl measured, and dies on anything that is not a 2xx —
# a fast 500 would otherwise make a beautiful graph.
timed() {
    local method=$1 token=$2 path=$3 body=${4:-} out="$WORK/response" result
    local args=(-s -o "$out" -w '%{http_code} %{time_total}' -X "$method" "$API$path" -H "Authorization: Bearer $token")

    if [ -n "$body" ]; then
        args+=(-H 'Content-Type: application/json' -d "$body")
    fi

    result="$(curl "${args[@]}")"

    case "${result%% *}" in
        2*) printf '%s\n' "${result##* }" ;;
        *) die "$method $path answered ${result%% *}: $(head -c 300 "$out")" ;;
    esac
}

# Runs one path RUNS times and prints `label p50 p95 max` in milliseconds.
sample() {
    local label=$1 method=$2 token=$3 path=$4 body=${5:-}

    : > "$WORK/samples"

    # One warm-up outside the sample: the first request through a path pays for a compiled query
    # plan and a connection nobody else will pay for again.
    timed "$method" "$token" "$path" "$body" >/dev/null

    for _ in $(seq 1 "$RUNS"); do
        timed "$method" "$token" "$path" "$body" >> "$WORK/samples"
    done

    python3 "$WORK/percentiles.py" "$label" < "$WORK/samples"
}

# Written to a file rather than piped in as a heredoc: `python3 -` reads its *program* from stdin,
# so a heredoc there leaves nothing for the samples to arrive on.
percentiles_script() {
    cat > "$WORK/percentiles.py" <<'PYTHON'
import sys

label = sys.argv[1]
values = sorted(float(line) * 1000 for line in sys.stdin if line.strip())


def percentile(sorted_values, fraction):
    """Nearest-rank, which is what a small sample can honestly claim."""
    rank = max(1, min(len(sorted_values), round(fraction * len(sorted_values) + 0.5)))
    return sorted_values[rank - 1]


print(f"{label:<34} {percentile(values, 0.50):8.0f} {percentile(values, 0.95):8.0f} {values[-1]:8.0f}")
PYTHON
}

header() {
    printf '\n%-34s %8s %8s %8s\n' "$1" "p50 ms" "p95 ms" "max ms"
    printf '%s\n' "----------------------------------------------------------------"
}

percentiles_script

say "Signing in"
admin="$(login admin)"
phone="$(login marisol)"
day="$(date -u '+%Y-%m-%d')"

# What a shop this size actually holds, so the numbers below have a denominator.
jobs="$(curl -s "$API/jobs?page=1&pageSize=1" -H "Authorization: Bearer $admin" | jq -r .total)"
customers="$(curl -s "$API/customers?page=1&pageSize=1" -H "Authorization: Bearer $admin" | jq -r .total)"
printf '   %s jobs, %s customers, %s runs per measurement\n' "$jobs" "$customers" "$RUNS"

# The cursor a phone that has been syncing all along would hold: today's. A cold pull is `since=0`,
# which is what a phone that has just been set up — or wiped — asks for.
warm_cursor="$(curl -s "$API/sync/pull?since=0" -H "Authorization: Bearer $phone" | jq -r .cursor)"

measure_all() {
    sample "GET /sync/pull (cold, since=0)" GET "$phone" "/sync/pull?since=0"
    sample "GET /sync/pull (warm)" GET "$phone" "/sync/pull?since=$warm_cursor"
    sample "GET /dispatch/board (today)" GET "$admin" "/dispatch/board?day=$day"
    sample "POST /schedule/optimize (a day)" POST "$admin" "/schedule/optimize" \
        "{\"from\":\"${day}T06:00:00Z\",\"to\":\"${day}T20:00:00Z\"}"
}

say "Idle host"
header "path"
measure_all

# ── Planning a day nobody has planned yet ──────────────────────────────────────────────────────
#
# The sample above re-plans a day that is already planned, which is what a dispatcher does through
# the morning as work arrives. The first plan of the day is a different cost — every stop is an
# insert rather than a comparison — and it is the one somebody waits on with an empty board in front
# of them, so it is worth its own number.

say "The first plan of a day"

for job in $(curl -s "$API/dispatch/board?day=$day" -H "Authorization: Bearer $admin" \
    | jq -r '.routes[].stops[].job.jobId'); do
    curl -s -o /dev/null -X POST "$API/jobs/$job/status" -H "Authorization: Bearer $admin" \
        -H 'Content-Type: application/json' -d '{"status":"Unscheduled"}'
done

first_plan="$(timed POST "$admin" "/schedule/optimize" \
    "{\"from\":\"${day}T06:00:00Z\",\"to\":\"${day}T20:00:00Z\"}")"

printf '   %.0f ms, planning a day with every stop still to be created\n' \
    "$(python3 -c "import sys; print(float(sys.argv[1]) * 1000)" "$first_plan")"

# ── The export, which is a different question ──────────────────────────────────────────────────
#
# Nobody waits on it the way they wait on a board, and its p95 is not the interesting number: what
# matters is whether a shop's whole history can be handed over without the host holding it. So it is
# measured once, for duration, size, and the resident memory the API grew by while writing it.

say "The export"
api_pid="$(pgrep -f 'OpenDispatch.Api' | head -1 || true)"
rss_before=0

if [ -n "$api_pid" ]; then
    rss_before="$(ps -o rss= -p "$api_pid" | tr -d ' ')"
fi

export_seconds="$(timed GET "$admin" /export)"
export_bytes="$(wc -c < "$WORK/response" | tr -d ' ')"

printf '   %s bytes in %s s\n' "$export_bytes" "$export_seconds"

if [ -n "$api_pid" ]; then
    rss_after="$(ps -o rss= -p "$api_pid" | tr -d ' ')"
    printf '   host RSS %s MB before, %s MB after (grew %s MB)\n' \
        "$((rss_before / 1024))" "$((rss_after / 1024))" "$(((rss_after - rss_before) / 1024))"
else
    printf '   (host RSS not measured: no local OpenDispatch.Api process found)\n'
fi

# ── The same four, with a fleet on the road ────────────────────────────────────────────────────

say "With $PULLERS phones polling"

for _ in $(seq 1 "$PULLERS"); do
    (
        while :; do
            curl -s -o /dev/null "$API/sync/pull?since=$warm_cursor" -H "Authorization: Bearer $phone" || true
        done
    ) &
done

sleep 2
header "path (under load)"
measure_all

# Quietly: the shell announces every terminated background job otherwise, and thirty lines of
# "Terminated" after a measurement reads like a failure.
kill $(jobs -p) 2>/dev/null || true
wait 2>/dev/null || true

printf '\nMeasured against %s at %s.\n' "$API" "$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
