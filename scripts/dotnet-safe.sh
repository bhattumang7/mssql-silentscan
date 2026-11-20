#!/usr/bin/env bash
set -u

if [[ "$#" -eq 0 ]]; then
    echo "usage: $0 <dotnet-subcommand> [args...]" >&2
    exit 2
fi

export MSBUILDDISABLENODEREUSE=1

timeout_seconds="${DOTNET_SAFE_TIMEOUT:-900}"
log_dir="${TMPDIR:-/tmp}/dotnet-safe-logs"
mkdir -p "$log_dir"
log_file="$log_dir/$(date +%Y%m%d-%H%M%S 2>/dev/null || echo run)-$$.log"

wait_for_stray_build_processes() {
    local pattern='VBCSCompiler|MSBuild\.dll.*nodemode'
    local max_wait_seconds=10
    local waited=0
    while pgrep -u "$(id -u)" -f "$pattern" >/dev/null 2>&1; do
        if [[ "$waited" -ge "$max_wait_seconds" ]]; then
            pkill -KILL -u "$(id -u)" -f "$pattern" >/dev/null 2>&1 || true
            break
        fi

        sleep 1
        waited=$((waited + 1))
    done
    return 0
}

reset_build_servers() {
    dotnet build-server shutdown >/dev/null 2>&1 || true
    wait_for_stray_build_processes
    return 0
}

lock_dir="${DOTNET_SAFE_LOCK:-$(dirname "$0")/../.dotnet-safe.lock}"
lock_wait=0
until mkdir "$lock_dir" 2>/dev/null; do
    if [[ -f "$lock_dir/pid" ]] && ! kill -0 "$(cat "$lock_dir/pid" 2>/dev/null)" 2>/dev/null; then
        rm -rf "$lock_dir"
        continue
    fi
    lock_wait=$((lock_wait + 5))
    if [[ "$lock_wait" -ge "${DOTNET_SAFE_LOCK_WAIT:-1800}" ]]; then
        echo "timed out waiting for another dotnet-safe run to finish" >&2
        exit 3
    fi
    sleep 5
done
echo $$ > "$lock_dir/pid"

group_pid=""
ended_cleanly=0
cleanup() {
    rm -rf "$lock_dir"
    if [[ "$ended_cleanly" -eq 1 ]]; then
        return 0
    fi
    if [[ -n "$group_pid" ]]; then
        kill -TERM -- "-$group_pid" >/dev/null 2>&1 || true
        sleep 1
        kill -KILL -- "-$group_pid" >/dev/null 2>&1 || true
    fi
    reset_build_servers
    return 0
}
trap cleanup EXIT

group_launcher=()
if command -v setsid >/dev/null 2>&1; then
    group_launcher=(setsid)
fi

max_attempts=2
attempt=1
while :; do
    "${group_launcher[@]}" timeout --kill-after=30s "${timeout_seconds}s" dotnet "$@" > "$log_file" 2>&1 &
    group_pid=$!
    wait "$group_pid"
    exit_code=$?

    if [[ "$exit_code" -eq 134 ]] && [[ "$attempt" -lt "$max_attempts" ]] && grep -q "Internal CLR error" "$log_file" 2>/dev/null; then
        echo "dotnet $* hit the known VBCSCompiler race (attempt ${attempt}/${max_attempts}) - clearing stray processes and retrying..." >&2
        reset_build_servers
        attempt=$((attempt + 1))
        log_file="$log_dir/$(date +%Y%m%d-%H%M%S 2>/dev/null || echo run)-$$-retry.log"
        continue
    fi

    break
done

if [[ "$exit_code" -eq 124 ]] || [[ "$exit_code" -eq 137 ]]; then
    echo "dotnet $* TIMED OUT after ${timeout_seconds}s - killed. Full log: $log_file" >&2
elif [[ "$exit_code" -eq 134 ]]; then
    echo "dotnet $* CRASHED (exit $exit_code). Full log: $log_file" >&2
elif [[ "$exit_code" -ne 0 ]]; then
    ended_cleanly=1
    echo "dotnet $* FAILED (exit $exit_code). Full log: $log_file" >&2
else
    ended_cleanly=1
    echo "dotnet $* succeeded. Full log: $log_file"
fi

echo "--- last 60 lines of $log_file ---"
tail -n 60 "$log_file"

exit "$exit_code"
