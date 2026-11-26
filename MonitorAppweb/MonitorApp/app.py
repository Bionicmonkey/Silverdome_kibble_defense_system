"""MonitorApp - Flask application for feeder dashboard."""

import logging
import os
import re
import time
from datetime import datetime, timezone
from pathlib import Path

from flask import Flask, Response, abort, jsonify, render_template, send_file
from werkzeug.utils import safe_join

from MonitorApp import config

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

SNAPSHOT_RETRY_COUNT = 3
SNAPSHOT_RETRY_DELAY_MS = 100


def _read_image_with_retry(path, count, delay_ms):
    """Read an image file with retry-on-lock logic.

    Returns the raw bytes on success, or None if all retries are exhausted.
    """
    for attempt in range(1, count + 1):
        try:
            return path.read_bytes()
        except (PermissionError, OSError) as exc:
            remaining = count - attempt
            logger.warning(
                "Snapshot read attempt %d/%d failed (%s); %s retry(s) left",
                attempt, count, exc, remaining,
            )
            if remaining > 0:
                time.sleep(delay_ms / 1000.0)
    logger.error("Snapshot read failed after %d attempts: %s", count, path)
    return None


def _read_state_with_retry(path):
    """Read the state file with a single retry on lock.

    Returns stripped text content or an empty string on failure.
    """
    try:
        return path.read_text(encoding="utf-8").strip()
    except (PermissionError, OSError):
        logger.warning("State file read failed; retrying after 100ms")
        time.sleep(0.1)
        try:
            return path.read_text(encoding="utf-8").strip()
        except (PermissionError, OSError):
            return ""


# --- Event enumeration regex patterns ---

EVENT_FILENAME_RE = re.compile(r"^(\d{8})_(\d{2})_(\d{2})_(\d{2})\s(.+?)\.jpg$")
ARM_LOG_RE = re.compile(r"^(\d{8})_(\d{2})_(\d{2})_(\d{2})\sArm\s(Closed|Opened)\.txt$")


def enumerate_events():
    """Return list of event entries sorted newest-first.

    Each entry: {"name", "date_str", "timestamp", "datetime", "animal"}
    Returns empty list if EVENTS_DIR doesn't exist (no error).
    """
    base_path = Path(config.EVENTS_DIR)
    if not base_path.exists():
        return []
    entries = []
    for fname in os.listdir(base_path):
        m = EVENT_FILENAME_RE.match(fname)
        if not m:
            continue  # skip Arm Close.jpg, txt logs, etc.
        date_str, h, mi, s, animal = m.groups()
        ts = f"{date_str}_{h}{mi}{s}"
        dt = datetime.strptime(ts, "%Y%m%d_%H%M%S")
        entries.append({
            "name": fname,
            "date_str": date_str,
            "timestamp": ts,
            "datetime": dt,
            "animal": animal,
        })
    entries.sort(key=lambda e: e["datetime"], reverse=True)
    return entries


def parse_arm_logs():
    """Return list of arm action dicts sorted newest-first.

    Each entry: {"type": "arm", "action", "timestamp", "datetime"}
    """
    base_path = Path(config.EVENTS_DIR)
    if not base_path.exists():
        return []
    actions = []
    for fname in os.listdir(base_path):
        m = ARM_LOG_RE.match(fname)
        if not m:
            continue
        date_str, h, mi, s, action_type = m.groups()
        ts = f"{date_str}_{h}{mi}{s}"
        dt = datetime.strptime(ts, "%Y%m%d_%H%M%S")
        action_label = "Arm Closed" if action_type == "Closed" else "Arm Opened"
        actions.append({
            "type": "arm",
            "action": action_label,
            "timestamp": ts,
            "datetime": dt,
        })
    actions.sort(key=lambda a: a["datetime"], reverse=True)
    return actions


def group_events(entries):
    """Group sorted event entries into time clusters using 5-minute gap.

    Different animal type or date always breaks a cluster.
    Returns list of cluster dicts with keys: id, start_ts, end_ts, animal,
    event_count, last_modified.
    """
    if not entries:
        return []

    clusters = []
    current_cluster = [entries[0]]

    for entry in entries[1:]:
        prev = current_cluster[-1]
        gap = (entry["datetime"] - prev["datetime"]).total_seconds()

        same_animal = entry["animal"] == prev["animal"]
        same_date = entry["timestamp"][:8] == prev["timestamp"][:8]
        close_enough = gap <= 300  # 5 minutes

        if same_animal and same_date and close_enough:
            current_cluster.append(entry)
        else:
            clusters.append(_make_cluster(current_cluster))
            current_cluster = [entry]

    clusters.append(_make_cluster(current_cluster))
    return clusters


def _make_cluster(cluster):
    """Build a cluster dict from a list of event entries."""
    timestamps = [e["timestamp"] for e in cluster]
    earliest_ts = min(timestamps)
    latest_ts = max(timestamps)
    last_dt = max(e["datetime"] for e in cluster)
    return {
        "id": cluster[0]["date_str"],
        "start_ts": earliest_ts,
        "end_ts": latest_ts,
        "animal": cluster[0]["animal"],
        "event_count": len(cluster),
        "last_modified": last_dt.isoformat(),
    }


app = Flask(__name__, template_folder="templates")


def get_feeder_state():
    """Read the current feeder arm state from state.txt.

    Returns a dict with arm_state and timestamp keys.
    Never raises - always returns HTTP 200-compatible data.
    """
    if not config.STATE_FILE.exists():
        logger.warning("state.txt not found at %s", config.STATE_FILE)
        return {"arm_state": "unknown", "timestamp": None}

    content = _read_state_with_retry(config.STATE_FILE)
    if not content:
        logger.warning("state.txt returned empty after retry")
        return {"arm_state": "unknown", "timestamp": None}

    return {
        "arm_state": content.strip().lower(),
        "timestamp": datetime.now(timezone.utc).isoformat(),
    }


@app.route("/api/state")
def api_state():
    """Return current feeder arm state as JSON."""
    return jsonify(get_feeder_state()), 200


@app.route("/snapshot")
def snapshot():
    """Serve the latest camera snapshot image."""
    image_bytes = _read_image_with_retry(
        config.SNAPSHOT_FILE, SNAPSHOT_RETRY_COUNT, SNAPSHOT_RETRY_DELAY_MS
    )
    if image_bytes is not None:
        resp = Response(image_bytes, mimetype="image/jpeg")
        resp.headers["Cache-Control"] = "no-store, no-cache, must-revalidate"
        resp.headers["Pragma"] = "no-cache"
        return resp
    logger.warning("Snapshot unavailable after retries")
    return "Image unavailable (file locked)", 503


@app.route("/")
def dashboard():
    """Serve the feeder dashboard page."""
    return render_template("dashboard.html"), 200


@app.route("/events")
def events_page():
    """Serve the event history page."""
    return render_template("timeline.html"), 200


@app.route("/api/event/images/<path:date_str>")
def api_event_images(date_str):
    """Return list of image names for a cluster date."""
    base_path = Path(config.EVENTS_DIR)
    if not base_path.exists():
        return jsonify({"date": date_str, "images": []}), 200

    prefix = f"{date_str}_"
    images = []
    for fname in os.listdir(base_path):
        if not fname.startswith(prefix):
            continue
        m = EVENT_FILENAME_RE.match(fname)
        if not m:
            continue  # skip non-event files
        date_str_part, h, mi, s, animal = m.groups()
        images.append({
            "name": fname,
            "timestamp": f"{date_str_part}_{h}{mi}{s}",
        })

    images.sort(key=lambda i: i["timestamp"])
    return jsonify({"date": date_str, "images": images}), 200


@app.route("/api/event/image/<path:filename>")
def api_event_image(filename):
    """Serve raw JPEG bytes via send_file() with safe_join validation."""
    # Validate filename against EVENT_FILENAME_RE first (whitelist)
    if not EVENT_FILENAME_RE.match(filename):
        abort(404)

    filepath = safe_join(str(Path(config.EVENTS_DIR)), filename)
    if not filepath or not os.path.isfile(filepath):
        abort(404)

    # Retry on file-lock from AnimalDetector writes
    for attempt in range(1, SNAPSHOT_RETRY_COUNT + 1):
        try:
            image_bytes = Path(filepath).read_bytes()
            break
        except (PermissionError, OSError) as exc:
            remaining = SNAPSHOT_RETRY_COUNT - attempt
            logger.warning(
                "Event image read attempt %d/%d failed (%s); %s retry(s) left",
                attempt, SNAPSHOT_RETRY_COUNT, exc, remaining,
            )
            if remaining > 0:
                time.sleep(SNAPSHOT_RETRY_DELAY_MS / 1000.0)
    else:
        logger.error("Event image read failed after %d attempts: %s", SNAPSHOT_RETRY_COUNT, filepath)
        abort(503)

    return send_file(filepath, mimetype="image/jpeg", max_age=86400)


# --- Static asset: opossum icon ---

OPOSSUM_ICON_PATH = Path(__file__).parent.parent / "opossum32x32.jpg"
OPOSSUM_PHOTO_PATH = Path(__file__).parent.parent / "opossum.jpg"


@app.route("/opossum-icon.jpg")
def opossum_icon():
    """Serve the 32×32 opossum icon used in event timelines."""
    if not OPOSSUM_ICON_PATH.exists():
        abort(404)
    return send_file(str(OPOSSUM_ICON_PATH), mimetype="image/jpeg", max_age=86400)


@app.route("/opossum-photo.jpg")
def opossum_photo():
    """Serve the full-res opossum photo (used for event detail backdrop)."""
    if not (OPOSSUM_PHOTO_PATH := Path(__file__).parent.parent / "opossum.jpg").exists():
        abort(404)
    return send_file(str(OPOSSUM_PHOTO_PATH), mimetype="image/jpeg", max_age=86400)


@app.route("/api/events")
def api_events():
    """Return JSON array of time-clustered event groups + arm action markers."""
    if not Path(config.EVENTS_DIR).exists():
        return jsonify([]), 200

    entries = enumerate_events()
    clusters = group_events(entries)

    # Parse arm actions and merge into the same list
    arm_actions = parse_arm_logs()

    # Convert cluster dicts to match timeline template expectations
    cluster_entries = []
    for c in clusters:
        cluster_entries.append({
            "id": c["id"],
            "start_ts": c["start_ts"],
            "end_ts": c["end_ts"],
            "animal": c["animal"],
            "event_count": c["event_count"],
            "last_modified": c["last_modified"],
            "_dt": datetime.fromisoformat(c["last_modified"]),
        })

    # Merge clusters + arm actions, sort by datetime descending
    all_entries = cluster_entries + arm_actions
    all_entries.sort(key=lambda e: e.get("_dt", e.get("datetime")), reverse=True)

    # Limit to newest 50 entries
    all_entries = all_entries[:50]

    # Strip internal _dt key before returning JSON
    result = []
    for e in all_entries:
        out = {k: v for k, v in e.items() if not k.startswith("_")}
        result.append(out)

    return jsonify(result), 200


if __name__ == "__main__":
    app.run(host=config.HOST, port=config.PORT, debug=False)
