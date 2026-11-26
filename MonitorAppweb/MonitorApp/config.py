"""Configuration constants for MonitorApp."""

import logging
import sys
from pathlib import Path

logger = logging.getLogger(__name__)

ANIMAL_DETECTOR_PATH = Path(r"C:\altcode\AnimalDetector")
STATE_FILE = ANIMAL_DETECTOR_PATH / "state.txt"
SNAPSHOT_FILE = ANIMAL_DETECTOR_PATH / "temp.jpg"

HOST = "0.0.0.0"
PORT = 8080

# --- Path validation ---

DETECTOR_PATH_VALID = False
if ANIMAL_DETECTOR_PATH.exists() and ANIMAL_DETECTOR_PATH.is_dir():
    DETECTOR_PATH_VALID = True
else:
    _msg = f"WARNING: AnimalDetector path does not exist or is not a directory: {ANIMAL_DETECTOR_PATH.resolve()}"
    logger.warning(_msg)
    sys.stderr.write(_msg + "\n")

def get_state_file_path() -> str:
    """Return the full string path of STATE_FILE, or empty string if invalid."""
    if not DETECTOR_PATH_VALID:
        logger.warning("Detector path invalid — cannot resolve state file path")
        return ""
    return str(STATE_FILE)


def get_snapshot_file_path() -> str:
    """Return the full string path of SNAPSHOT_FILE, or empty string if invalid."""
    if not DETECTOR_PATH_VALID:
        logger.warning("Detector path invalid — cannot resolve snapshot file path")
        return ""
    return str(SNAPSHOT_FILE)


# --- Events directory config ---

EVENTS_DIR = Path(r"C:\altcode\AnimalDetector\events")
EVENTS_DIR_VALID = False
if EVENTS_DIR.exists() and EVENTS_DIR.is_dir():
    EVENTS_DIR_VALID = True
else:
    _msg = f"WARNING: events path does not exist or is not a directory: {EVENTS_DIR.resolve()}"
    logger.warning(_msg)
    sys.stderr.write(_msg + "\n")
