#!/bin/bash
# start-maf.sh — lance les DEUX clones MAF (dev + prod) en parallèle
#
# Clone dev  : /home/gmagnier/.hermes/cache/scratch/gh-work/MiniMax-Maf-Host
# Clone prod : /home/gmagnier/.hermes/cache/scratch/gh-work/MiniMax-Maf-Host-prod
#
# Les deux tournent en mode Development (DevUI visible) sur des ports différents.
#
#   Dev  : http://localhost:5014/devui
#   Prod : http://localhost:5016/devui  (même commit, vue alternative)
#
# Usage : ./start-maf.sh [start|stop|status|logs]

set -euo pipefail

DEV_HOME="/home/gmagnier/.hermes/cache/scratch/gh-work/MiniMax-Maf-Host"
PROD_HOME="/home/gmagnier/.hermes/cache/scratch/gh-work/MiniMax-Maf-Host-prod"
LOG_DIR="/home/gmagnier/.hermes/cache/scratch/maf-logs"
mkdir -p "$LOG_DIR"

DEV_PORT=5014
PROD_PORT=5016

cmd="${1:-start}"
target="${2:-both}"  # both | dev | prod

start_one() {
  local home="$1" port="$2" logfile="$3" label="$4"
  pkill -f "dotnet.*MafMiniMaxAgent.*--urls.*:$port" 2>/dev/null || true
  sleep 1
  pkill -9 -f "dotnet.*MafMiniMaxAgent.*--urls.*:$port" 2>/dev/null || true

  cd "$home"

  ASPNETCORE_ENVIRONMENT=Development \
    nohup dotnet run --no-launch-profile --urls "http://0.0.0.0:$port" \
    > "$logfile" 2>&1 < /dev/null &
  local pid=$!
  disown $pid 2>/dev/null || true
  echo "$label  PID=$pid  → http://localhost:$port/devui  (logs: $logfile)"
}

case "$cmd" in
  start)
    if [[ "$target" == "both" || "$target" == "dev" ]]; then
      start_one "$DEV_HOME"  "$DEV_PORT"  "$LOG_DIR/dev.log"  "dev "
    fi
    if [[ "$target" == "both" || "$target" == "prod" ]]; then
      start_one "$PROD_HOME" "$PROD_PORT" "$LOG_DIR/prod.log" "prod"
    fi

    # Wait + verify only what we started
    wait_dev=0
    wait_prod=0
    [[ "$target" == "both" || "$target" == "dev" ]] && wait_dev=1
    [[ "$target" == "both" || "$target" == "prod" ]] && wait_prod=1

    echo
    echo "Waiting up to 90s for ports to listen..."
    for i in $(seq 1 90); do
      # Count listening sockets on each port. When we are not waiting for a
      # given port, treat it as already OK (1).
      if [[ $wait_dev -eq 1 ]]; then
        dev_ok=$(ss -ltn 2>/dev/null | grep -c ":$DEV_PORT ")
      else
        dev_ok=1
      fi
      if [[ $wait_prod -eq 1 ]]; then
        prod_ok=$(ss -ltn 2>/dev/null | grep -c ":$PROD_PORT ")
      else
        prod_ok=1
      fi
      if [[ "${dev_ok:-0}" -ge 1 && "${prod_ok:-0}" -ge 1 ]]; then
        echo
        echo "✓ up after ${i}s"
        echo
        [[ "$target" == "both" || "$target" == "dev" ]] && echo "  Dev  : http://localhost:$DEV_PORT/devui"
        [[ "$target" == "both" || "$target" == "prod" ]] && echo "  Prod : http://localhost:$PROD_PORT/devui"
        exit 0
      fi
      sleep 1
    done
    echo
    echo "⚠ timeout. Check logs:"
    echo "  $LOG_DIR/dev.log"
    echo "  $LOG_DIR/prod.log"
    exit 1
    ;;

  stop)
    pkill -TERM -f "dotnet.*MafMiniMaxAgent.*--urls.*:$DEV_PORT" 2>/dev/null || true
    pkill -TERM -f "dotnet.*MafMiniMaxAgent.*--urls.*:$PROD_PORT" 2>/dev/null || true
    sleep 2
    pkill -9 -f "dotnet.*MafMiniMaxAgent" 2>/dev/null || true
    echo "stopped"
    ;;

  status)
    echo "=== ports ==="
    ss -ltn 2>/dev/null | grep -E ":($DEV_PORT|$PROD_PORT) " || echo "  (none)"
    echo "=== processes ==="
    ps -ef | grep -E "MafMiniMaxAgent.*--urls" | grep -v grep || echo "  (none)"
    ;;

  logs)
    echo "=== dev.log (last 50) ==="
    tail -50 "$LOG_DIR/dev.log" 2>/dev/null || echo "  (no log)"
    echo
    echo "=== prod.log (last 50) ==="
    tail -50 "$LOG_DIR/prod.log" 2>/dev/null || echo "  (no log)"
    ;;

  *)
    echo "Usage: $0 [start|stop|status|logs] [both|dev|prod]"
    exit 1
    ;;
esac
