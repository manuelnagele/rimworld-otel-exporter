#!/usr/bin/env bash
# Watch the Progress Renderer output folder and upload the newest full-map PNG to a public URL
# as a stable "latest.png", so the Grafana "Colony Map" panel can display it from anywhere.
#
# Configure either inline or via a config file, then run (directly or under launchd/systemd):
#
#   # inline:
#   SRC_DIR="$HOME/Library/Application Support/RimWorld/RenderProgress" \
#   UPLOAD_CMD='rclone copyto "$SRC_FILE" r2:my-bucket/rimworld/latest.png' \
#   scripts/upload-map-screenshot.sh
#
#   # or via a config file (what the launchers use), default location:
#   ~/.config/rimworld-map/uploader.env    (override with MAP_UPLOADER_ENV=/path)
#   — copy scripts/uploader.env.example there and edit it.
#
# UPLOAD_CMD runs with $SRC_FILE set to the newest PNG. Any command works — examples:
#   rclone:  UPLOAD_CMD='rclone copyto "$SRC_FILE" r2:my-bucket/rimworld/latest.png'
#   aws s3:  UPLOAD_CMD='aws s3 cp "$SRC_FILE" s3://my-bucket/rimworld/latest.png --acl public-read'
#   scp:     UPLOAD_CMD='scp "$SRC_FILE" user@host:/var/www/html/rimworld/latest.png'
# See docs/map-screenshot.md for the full walkthrough.
set -euo pipefail

# Load config file if present (lets the launchers run with no inline env).
CONFIG="${MAP_UPLOADER_ENV:-$HOME/.config/rimworld-map/uploader.env}"
if [ -f "$CONFIG" ]; then
  # shellcheck disable=SC1090
  . "$CONFIG"
fi

SRC_DIR="${SRC_DIR:?set SRC_DIR to the Progress Renderer output folder}"
UPLOAD_CMD="${UPLOAD_CMD:?set UPLOAD_CMD (it runs with \$SRC_FILE set to the newest PNG)}"
INTERVAL="${INTERVAL:-60}"

echo "[map-uploader] watching '$SRC_DIR' every ${INTERVAL}s; uploading via: $UPLOAD_CMD"
last=""
while true; do
  # newest .png by mtime (nullglob-safe)
  newest="$(ls -t "$SRC_DIR"/*.png 2>/dev/null | head -n 1 || true)"
  if [ -n "$newest" ] && [ "$newest" != "$last" ]; then
    echo "[map-uploader] new map: $newest"
    if SRC_FILE="$newest" bash -c "$UPLOAD_CMD"; then
      last="$newest"
      echo "[map-uploader] uploaded ok"
    else
      echo "[map-uploader] upload FAILED — will retry next tick" >&2
    fi
  fi
  sleep "$INTERVAL"
done
