"""Tests for MonitorApp feeder dashboard endpoints."""

import os
import sys
import tempfile
from pathlib import Path

import pytest

# Ensure project root is on the path so `MonitorApp` package is importable
PROJECT_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(PROJECT_ROOT))


@pytest.fixture()
def app():
    """Create Flask application for testing."""
    from MonitorApp.app import app
    return app


@pytest.fixture()
def client(app):
    """Create a test client for the Flask app."""
    return app.test_client()


class TestApiState:
    def test_state_returns_json(self, client):
        """Verify /api/state returns a JSON response with arm_state and timestamp keys."""
        resp = client.get("/api/state")
        assert resp.status_code == 200
        data = resp.get_json()
        assert "arm_state" in data
        assert "timestamp" in data

    def test_state_unknown_when_file_missing(self, client, monkeypatch):
        """When state.txt doesn't exist, returns unknown state with HTTP 200."""
        from MonitorApp import config as cfg

        tmp_missing = Path(tempfile.gettempdir()) / "nonexistent_monitorapp_state.txt"
        monkeypatch.setattr(cfg, "STATE_FILE", tmp_missing)

        resp = client.get("/api/state")
        data = resp.get_json()
        assert data["arm_state"] == "unknown"
        assert data["timestamp"] is None


class TestSnapshot:
    def test_snapshot_returns_image_mime(self, client, monkeypatch):
        """Verify /snapshot returns Content-Type image/jpeg when temp.jpg exists."""
        # Create a temporary JPEG file to mock temp.jpg; keep it closed so we can delete it later
        fd, tmp_path = tempfile.mkstemp(suffix=".jpg")
        os.write(fd, b"\xff\xd8\xff\xe0" + b"\x00" * 100)  # minimal JPEG header
        os.close(fd)

        from MonitorApp import config as cfg

        monkeypatch.setattr(cfg, "SNAPSHOT_FILE", Path(tmp_path))

        resp = client.get("/snapshot")
        assert resp.status_code == 200
        assert "image/jpeg" in resp.content_type

        try:
            os.unlink(tmp_path)
        except PermissionError:
            pass  # Windows may not allow immediate deletion; test already passed


class TestDashboard:
    def test_dashboard_rendered(self, client):
        """Verify / returns 200 with HTML content containing 'Feeder Dashboard'."""
        resp = client.get("/")
        assert resp.status_code == 200
        body = resp.get_data(as_text=True)
        assert "Feeder Dashboard" in body
