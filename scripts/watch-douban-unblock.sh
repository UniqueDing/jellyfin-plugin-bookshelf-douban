#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RESULT_FILE="${ROOT_DIR}/douban-unblocked-at.txt"
LOG_FILE="${ROOT_DIR}/douban-unblock-check.log"
INTERVAL_SECONDS=3600
MAX_ATTEMPTS=0
LOOKUP_KIND="douban-id"
LOOKUP_VALUE="6082808"

usage() {
  cat <<'USAGE'
Usage:
  scripts/watch-douban-unblock.sh [options]

Options:
  --interval-seconds <seconds>  Wait time between checks. Default: 3600.
  --max-attempts <count>        Stop after count attempts. Default: 0, meaning unlimited.
  --result-file <path>          File written when Douban works again. Default: ./douban-unblocked-at.txt.
  --log-file <path>             Append every check result here. Default: ./douban-unblock-check.log.
  --douban-id <id>              Check a Douban subject page. Default: 6082808.
  --isbn <isbn>                 Check Douban search by ISBN.
  --title <title>               Check Douban search by title.
  -h, --help                    Show this help.

Examples:
  scripts/watch-douban-unblock.sh --interval-seconds 1800
  scripts/watch-douban-unblock.sh --douban-id 6082808 --result-file /tmp/douban-ok.txt
  scripts/watch-douban-unblock.sh --isbn 9787544253994 --max-attempts 24
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --interval-seconds)
      INTERVAL_SECONDS="$2"
      shift 2
      ;;
    --max-attempts)
      MAX_ATTEMPTS="$2"
      shift 2
      ;;
    --result-file)
      RESULT_FILE="$2"
      shift 2
      ;;
    --log-file)
      LOG_FILE="$2"
      shift 2
      ;;
    --douban-id)
      LOOKUP_KIND="douban-id"
      LOOKUP_VALUE="$2"
      shift 2
      ;;
    --isbn)
      LOOKUP_KIND="isbn"
      LOOKUP_VALUE="$2"
      shift 2
      ;;
    --title)
      LOOKUP_KIND="title"
      LOOKUP_VALUE="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if ! [[ "$INTERVAL_SECONDS" =~ ^[0-9]+$ ]] || [[ "$INTERVAL_SECONDS" -lt 1 ]]; then
  echo "--interval-seconds must be a positive integer." >&2
  exit 2
fi

if ! [[ "$MAX_ATTEMPTS" =~ ^[0-9]+$ ]]; then
  echo "--max-attempts must be a non-negative integer." >&2
  exit 2
fi

mkdir -p "$(dirname "$RESULT_FILE")" "$(dirname "$LOG_FILE")"

attempt=1
while true; do
  timestamp="$(date -Iseconds)"
  output_file="$(mktemp)"
  set +e
  dotnet run --project "${ROOT_DIR}/tools/DoubanDebugRunner" -- "--${LOOKUP_KIND}" "$LOOKUP_VALUE" >"$output_file" 2>&1
  status=$?
  set -e

  if [[ "$status" -eq 0 ]]; then
    {
      echo "Douban reachable at ${timestamp}"
      echo "Lookup: --${LOOKUP_KIND} ${LOOKUP_VALUE}"
      echo "Attempt: ${attempt}"
    } >"$RESULT_FILE"
    printf '%s attempt=%s status=ok lookup=--%s %s result_file=%s\n' "$timestamp" "$attempt" "$LOOKUP_KIND" "$LOOKUP_VALUE" "$RESULT_FILE" | tee -a "$LOG_FILE"
    rm -f "$output_file"
    exit 0
  fi

  if grep -Eq 'NETSDK1045|The current \.NET SDK does not support targeting|The build failed|MSBUILD : error|error NETSDK' "$output_file"; then
    printf '%s attempt=%s status=error lookup=--%s %s\n' "$timestamp" "$attempt" "$LOOKUP_KIND" "$LOOKUP_VALUE" | tee -a "$LOG_FILE"
    sed 's/^/  /' "$output_file" >>"$LOG_FILE"
    rm -f "$output_file"
    echo "Douban check could not run. Use a .NET 9 SDK, or run through: nix-shell --run './scripts/watch-douban-unblock.sh ...'" | tee -a "$LOG_FILE"
    exit 2
  fi

  printf '%s attempt=%s status=blocked lookup=--%s %s\n' "$timestamp" "$attempt" "$LOOKUP_KIND" "$LOOKUP_VALUE" | tee -a "$LOG_FILE"
  sed 's/^/  /' "$output_file" >>"$LOG_FILE"
  rm -f "$output_file"

  if [[ "$MAX_ATTEMPTS" -gt 0 && "$attempt" -ge "$MAX_ATTEMPTS" ]]; then
    echo "Douban still blocked after ${attempt} attempt(s)." | tee -a "$LOG_FILE"
    exit 1
  fi

  attempt=$((attempt + 1))
  sleep "$INTERVAL_SECONDS"
done
