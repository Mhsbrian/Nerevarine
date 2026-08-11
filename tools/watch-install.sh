#!/usr/bin/env bash
# Live installer status, refreshed every 10s. Usage: tools/watch-install.sh [install-dir]
DIR="${1:-$HOME/MorrowindRemake}"

while true; do
    clear
    echo "=== Morrowind Remake install status — $(date +%H:%M:%S) ==="
    echo

    LOG=$(ls -t "$DIR"/logs/installer-*.log 2>/dev/null | head -1)

    if pgrep -f "Mri.Curation" > /dev/null; then
        echo "installer:   RUNNING"
    else
        echo "installer:   not running"
    fi

    DL=$(du -sh "$DIR/downloads" 2>/dev/null | cut -f1)
    COUNT=$(find "$DIR/downloads/archive-cache" -name "*.archive" 2>/dev/null | wc -l)
    MODS=$(du -sh "$DIR/mods" 2>/dev/null | cut -f1)
    echo "downloaded:  ${DL:-0} (${COUNT:-0} archives, target ≈586)"
    echo "extracted:   ${MODS:-0} in mods/"
    echo

    if [ -n "$LOG" ]; then
        echo "--- pipeline steps ---"
        grep -E "\[(INFO|ERROR)\s*\] \[engine\]|already satisfied|completed and verified|step failed" "$LOG" \
            | sed -E 's/^[0-9:.]+ \[[A-Z ]+\] //' | tail -8
        echo
        echo "--- latest activity ---"
        grep "\[proc:umo" "$LOG" | tail -4 | sed -E 's/^[0-9:.]+ \[[A-Z ]+\] \[proc:umo[^]]*\] //' | cut -c1-110
    fi
    sleep 10
done
