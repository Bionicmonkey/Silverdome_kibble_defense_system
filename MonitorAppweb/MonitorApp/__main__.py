"""CLI entry point for MonitorApp — run as `python -m MonitorApp`."""

import socket

from MonitorApp.app import app
from MonitorApp.config import DETECTOR_PATH_VALID, HOST, PORT


def main():
    # Print AnimalDetector path status
    from MonitorApp.config import ANIMAL_DETECTOR_PATH
    print(f"AnimalDetector path: {ANIMAL_DETECTOR_PATH.resolve()}")

    if not DETECTOR_PATH_VALID:
        _yellow = "\x1b[33m"
        _reset = "\x1b[0m"
        msg = (
            "WARNING: AnimalDetector paths are invalid. "
            "The app will show 'unknown' for all state."
        )
        print(f"{_yellow}{msg}{_reset}")

    print(f"Starting MonitorApp on http://{HOST}:{PORT}")

    # Print LAN access info after server starts
    try:
        local_ip = socket.gethostbyname(socket.gethostname())
    except Exception:
        local_ip = "127.0.0.1"

    import threading, time

    def print_lan_info():
        time.sleep(1)  # Wait for Flask to finish starting
        print(f"Reachable at http://{local_ip}:{PORT} (and any device on your home network)")

    threading.Thread(target=print_lan_info, daemon=True).start()

    app.run(host=HOST, port=PORT, debug=False)


if __name__ == "__main__":
    main()
