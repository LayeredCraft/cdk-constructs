#!/usr/bin/env bash
#
# verify-log-groups.sh - T7 acceptance harness for companion ".local" log groups
# (design section 19.1, item 3.9).
#
# Read-only: it never mutates AWS. It reads the app stack's LogGroupExpectations output, inspects the live
# CloudWatch Logs groups through the AWS API, and asserts that every expected centralized/companion group
# exists with the exact retention it was configured with, that no unexpected sandbox log group exists, and
# (after traffic) that each group actually holds recent events. Writes a machine-readable summary to
# build/log-group-summary.json.
#
# Usage:
#   SANDBOX_ALLOW_AWS=1 SANDBOX_ID=wfsbx SANDBOX_ACCOUNT=123456789012 SANDBOX_REGION=us-east-1 \
#       ./verify-log-groups.sh [--expect-delivery] [--attempts <n>] [--no-retry]
#
# Flags:
#   --expect-delivery   after real traffic: require every expected group to exist and to hold >0 recent log
#                       events (queried with logs filter-log-events, not storedBytes; the companion group
#                       Lambda creates on first request is only required in this phase)
#   --attempts <n>      retry attempts for eventual-consistency checks (1-60, default 8)
#   --no-retry          disable retries (equivalent to --attempts 1)

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

expect_delivery=0
max_attempts="${SMOKE_ATTEMPTS:-8}"
retry=1

while [ "$#" -gt 0 ]; do
    case "$1" in
        --expect-delivery)
            expect_delivery=1
            shift
            ;;
        --attempts)
            [ "$#" -ge 2 ] || die "--attempts requires a value."
            max_attempts="$2"
            shift 2
            ;;
        --no-retry)
            retry=0
            shift
            ;;
        -h | --help)
            sed -n '2,24p' "${BASH_SOURCE[0]}"
            exit 0
            ;;
        *) die "unknown argument: $1" ;;
    esac
done

case "${max_attempts}" in
    '' | *[!0-9]*) die "--attempts must be a positive integer." ;;
esac
[ "${max_attempts}" -ge 1 ] || die "--attempts must be at least 1."
[ "${max_attempts}" -le 60 ] || die "--attempts must be at most 60 (retries are bounded)."
[ "${retry}" -eq 1 ] || max_attempts=1

validate_sandbox_env
require_aws_allowed
verify_aws_identity
require_cmd aws
require_cmd python3

app_stack="$(stack_name_app)"

app_ownership="$(stack_ownership "${app_stack}")"
case "${app_ownership}" in
    owned) ;;
    absent) die "app stack ${app_stack} does not exist; deploy the sandbox before verifying log groups." ;;
    foreign:*) die "app stack ${app_stack} is not owned by sandbox '${SANDBOX_ID}' (${app_ownership#foreign:}); refusing to inspect it." ;;
    error:*) die "could not determine ownership of app stack ${app_stack}; refusing to continue (fail closed)." ;;
    *) die "unexpected ownership result for ${app_stack}: ${app_ownership}" ;;
esac

VG_TMP="${SANDBOX_DIR}/build/.verify-log-groups"
mkdir -p "${VG_TMP}"
GROUPS_FILE="${VG_TMP}/groups.json"
EXPECT_FILE="${VG_TMP}/expectations.json"
EVENTS_FILE="${VG_TMP}/events.json"
SUMMARY_FILE="${SANDBOX_DIR}/build/log-group-summary.json"

expectations="$(stack_output "${app_stack}" LogGroupExpectations)" ||
    die "app stack is missing the LogGroupExpectations output; redeploy the sandbox with the current construct."
[ -n "${expectations}" ] || die "app stack LogGroupExpectations output is empty."

printf '%s' "${expectations}" >"${EXPECT_FILE}"

# --- Describe the sandbox groups (bounded retries for eventual consistency) -----------------------

# Query real log events for every expected group. Writes a JSON object mapping each group name to the
# number of events found in the lookback window. Uses logs filter-log-events because storedBytes is an
# unreliable delivery signal (it can report 0 while events exist).
collect_events() {
    python3 - "${EXPECT_FILE}" "${EVENTS_FILE}" <<'PY'
import json, os, subprocess, sys

expect_path, out_path = sys.argv[1], sys.argv[2]
expectations = json.load(open(expect_path))
region = os.environ.get("SANDBOX_REGION", "us-east-1")
start_ms = int(__import__("time").time() * 1000) - 4 * 3600 * 1000


def event_count(name):
    try:
        proc = subprocess.run(
            ["aws", "--region", region, "logs", "filter-log-events",
             "--log-group-name", name, "--start-time", str(start_ms),
             "--limit", "1", "--output", "json"],
            capture_output=True, text=True)
        if proc.returncode != 0:
            return 0
        return len(json.loads(proc.stdout or "{}").get("events", []))
    except Exception:
        return 0


counts = {}
for e in expectations:
    for key in ("Centralized", "Companion"):
        counts[e[key]] = event_count(e[key])
json.dump(counts, open(out_path, "w"), indent=2)
PY
}

collect_groups() {
    local attempt=1
    local central custom
    while [ "${attempt}" -le "${max_attempts}" ]; do
        central="$(aws_cmd logs describe-log-groups --log-group-name-prefix "/aws/lambda/web/${SANDBOX_ID}-" --output json 2>/dev/null || printf '{"logGroups":[]}')"
        custom="$(aws_cmd logs describe-log-groups --log-group-name-prefix "/${SANDBOX_NAMESPACE}/${SANDBOX_ID}-" --output json 2>/dev/null || printf '{"logGroups":[]}')"
        printf '%s\n%s\n' "${central}" "${custom}" >"${GROUPS_FILE}"
        [ "${expect_delivery}" -eq 0 ] && return 0
        collect_events
        # In the delivery phase, keep probing until every expected group holds real events (bounded).
        if python3 - "${EXPECT_FILE}" "${EVENTS_FILE}" <<'PY'
import json, sys

expectations = json.load(open(sys.argv[1]))
counts = json.load(open(sys.argv[2]))
for e in expectations:
    for key in ("Centralized", "Companion"):
        if int(counts.get(e[key]) or 0) <= 0:
            sys.exit(1)
sys.exit(0)
PY
        then
            return 0
        fi
        attempt=$((attempt + 1))
        [ "${attempt}" -le "${max_attempts}" ] && sleep 5
    done
    return 0
}

collect_groups

# --- Evaluate the expectations and write the summary ----------------------------------------------

python3 - "${EXPECT_FILE}" "${GROUPS_FILE}" "${expect_delivery}" "${EVENTS_FILE}" "${SUMMARY_FILE}" <<'PY'
import json, os, sys

expect_path, groups_path, delivery, events_path, out_path = sys.argv[1:6]
delivery = delivery == "1"

expectations = json.load(open(expect_path))
try:
    events_by_name = json.load(open(events_path))
except Exception:
    events_by_name = {}


def read_groups(path):
    # The AWS CLI pretty-prints --output json across many lines, so decode a
    # stream of concatenated JSON objects rather than assuming one object per line.
    data = open(path).read()
    decoder = json.JSONDecoder()
    idx, end, out = 0, len(data), []
    while idx < end:
        while idx < end and data[idx] in " \t\r\n":
            idx += 1
        if idx >= end:
            break
        obj, idx = decoder.raw_decode(data, idx)
        out += obj.get("logGroups", [])
    return out


groups = read_groups(groups_path)
by_name = {g.get("logGroupName"): g for g in groups}

results = []
counts = {"PASS": 0, "FAIL": 0, "SKIP": 0, "BLOCKED": 0}


def record(status, name, detail=""):
    counts[status] = counts.get(status, 0) + 1
    results.append({"status": status, "name": name, "detail": detail})


def retention_of(group):
    value = group.get("retentionInDays")
    return None if value in (None, "") else int(value)


def check_group(label, name, expected_days, must_exist, managed):
    group = by_name.get(name)
    if group is None:
        if must_exist:
            record("FAIL", f"{label} exists", f"expected log group {name} is missing")
        else:
            record("SKIP", f"{label} exists",
                   f"{name} not created yet (Lambda creates it on first request); nothing to inspect")
        return
    record("PASS", f"{label} exists", name)
    actual = retention_of(group)
    if expected_days is None:
        if actual is None:
            record("PASS", f"{label} retention", f"{name} has no expiry (unlimited), as expected")
        else:
            record("FAIL", f"{label} retention",
                   f"{name} should have no expiry but has retentionInDays={actual}")
    else:
        if actual == expected_days:
            record("PASS", f"{label} retention",
                   f"{name} retentionInDays={actual} (managed={str(managed).lower()})")
        else:
            record("FAIL", f"{label} retention",
                   f"{name} expected retentionInDays={expected_days}, got {actual}")
    if delivery:
        events = int(events_by_name.get(name) or 0)
        if events > 0:
            record("PASS", f"{label} delivery", f"{name} holds {events} recent log event(s)")
        else:
            record("FAIL", f"{label} delivery",
                   f"{name} holds no log events yet; invoke the function first")


expected_names = set()
for e in expectations:
    fn = e["Function"]
    expected_names.add(e["Centralized"])
    expected_names.add(e["Companion"])
    check_group(f"{fn} centralized", e["Centralized"], e["CentralizedRetentionDays"], True, True)
    # The companion Lambda creates on first request is only required to exist after traffic.
    companion_required = bool(e["CompanionManaged"]) or delivery
    check_group(f"{fn} companion", e["Companion"], e["CompanionRetentionDays"],
                companion_required, bool(e["CompanionManaged"]))

unexpected = sorted(g for g in by_name if g not in expected_names)
if unexpected:
    record("FAIL", "no unexpected sandbox log groups",
           "found groups not in the expectations: " + ", ".join(unexpected))
else:
    record("PASS", "no unexpected sandbox log groups",
           f"{len(expected_names)} expected group(s), none extra")

summary = {
    "sandboxId": os.environ.get("SANDBOX_ID", ""),
    "phase": "delivery" if delivery else "pre-traffic",
    "pass": counts["PASS"],
    "fail": counts["FAIL"],
    "skip": counts["SKIP"],
    "blocked": counts["BLOCKED"],
    "results": results,
}
with open(out_path, "w") as fh:
    json.dump(summary, fh, indent=2)

for r in results:
    line = f"[{r['status']}] {r['name']}"
    if r["detail"]:
        line += f" - {r['detail']}"
    sys.stderr.write(line + "\n")
sys.stderr.write(
    "PASS=%d FAIL=%d SKIP=%d BLOCKED=%d\n"
    % (counts["PASS"], counts["FAIL"], counts["SKIP"], counts["BLOCKED"])
)
sys.exit(1 if (counts["FAIL"] or counts["BLOCKED"]) else 0)
PY
rc=$?

log "Log-group verification summary written to ${SUMMARY_FILE}."
exit "${rc}"
