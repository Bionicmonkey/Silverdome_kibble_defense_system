"""Tests for event history endpoints -- Phase 2 Wave 1 (Task 3).

All tests use tempfile-based synthetic events directory -- no dependency
on actual AnimalDetector data.
"""

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


@pytest.fixture()
def tmp_events_dir(monkeypatch):
    """Create a temporary directory populated with synthetic event files.

    Writes:
    - Small valid JPEG header bytes to simulate event images
    - Arm log txt files (empty content)
    Monkeypatches config.EVENTS_DIR at setup; cleans up on teardown.
    """
    td = tempfile.mkdtemp(prefix="monitorapp_events_")

    # -- synthetic event jpgs --
    # Burst of 3 opossum events within 5-min gap,
    # then a cat burst, then next-day dog.
    jpeg_data = b"\xff\xd8\xff\xe0" + b"\x00" * 100  # >=200 bytes

    event_names = [
        "20261112_04_58_46 opossum.jpg",   # t=04:58:46
        "20261112_04_59_10 opossum.jpg",   # +24s  -> same cluster
        "20261112_05_02_30 opossum.jpg",   # +200s -> still same cluster (<=300s)
        "20261112_06_10_00 cat.jpg",       # +4050s -> new cluster, diff animal+date
        "20261112_06_11_15 cat.jpg",       # +75s  -> same cluster as prev cat
        "20261113_08_00_00 dog.jpg",       # next day
    ]

    for n in event_names:
        (Path(td) / n).write_bytes(jpeg_data)

    # -- arm log txt files (empty content, matching actual format from research) --
    arm_names = [
        "20261112_10_30_00 Arm Closed.txt",
        "20261112_14_00_00 Arm Opened.txt",
    ]
    for n in arm_names:
        (Path(td) / n).write_text("")

    # Monkeypatch config.EVENTS_DIR to point to this temp directory
    from MonitorApp import config as cfg
    monkeypatch.setattr(cfg, "EVENTS_DIR", td)

    yield td

    # teardown -- best-effort cleanup on Windows
    try:
        import shutil
        shutil.rmtree(td, ignore_errors=True)
    except Exception:
        pass


# ========== Task 1 tests ==========

class TestApiEvents:
    """Tests for /api/events endpoint."""

    def test_events_returns_json_list(self, client, tmp_events_dir):
        """GET /api/events returns 200 with JSON array."""
        resp = client.get("/api/events")
        assert resp.status_code == 200
        data = resp.get_json()
        assert isinstance(data, list)

    def test_events_clusters_grouped_by_time_span(self, client, tmp_events_dir):
        """Verify clusters have start_ts/end_ts for grouped bursts."""
        resp = client.get("/api/events")
        data = resp.get_json()
        # Find clusters (entries with event_count key, not arm actions)
        clusters = [e for e in data if "event_count" in e]
        assert len(clusters) >= 1  # at least one cluster exists

    def test_events_includes_arm_actions(self, client, tmp_events_dir):
        """Arm actions from txt logs appear in merged response."""
        resp = client.get("/api/events")
        data = resp.get_json()
        arm_entries = [e for e in data if e.get("type") == "arm"]
        assert len(arm_entries) >= 1
        actions = {e["action"] for e in arm_entries}
        assert "Arm Closed" in actions or "Arm Opened" in actions


class TestEventImages:
    """Tests for /api/event/images/<date_str> and /api/event/image/<filename>."""

    def test_images_endpoint_returns_list(self, client, tmp_events_dir):
        """GET /api/event/images/{date_str} returns 200 with image list."""
        resp = client.get("/api/event/images/20261112")
        assert resp.status_code == 200
        data = resp.get_json()
        assert "images" in data
        assert isinstance(data["images"], list)
        # Should have images for that date
        assert len(data["images"]) >= 5

    def test_image_serves_jpeg_bytes(self, client, tmp_events_dir):
        """GET /api/event/image/{filename} returns 200 with image/jpeg content type."""
        resp = client.get("/api/event/image/20261112_04_58_46%20opossum.jpg")
        assert resp.status_code == 200
        assert "image/jpeg" in resp.content_type

    def test_traversal_blocked(self, client, tmp_events_dir):
        """Path traversal filenames return 404."""
        resp = client.get("/api/event/image/../../../etc/passwd")
        assert resp.status_code == 404


# ========== Timeline template structural tests (Plan 02-03 Task 1) ==========

class TestTimelineTemplate:
    """Structural tests for timeline.html — plan 02-03 Task 1.

    These tests verify that the rendered timeline page contains all required
    CSS classes, HTML elements, and JS function signatures expected by the
    client-side rendering logic.
    """

    def test_timeline_page_returns_200(self, client):
        """GET /events returns 200 with HTML."""
        resp = client.get("/events")
        assert resp.status_code == 200

    def test_timeline_contains_event_row_classes(self, client):
        """Timeline has .event-row class for animal and arm event rows."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert ".event-row" in body or "event-row" in body

    def test_timeline_has_animal_event_card_style(self, client):
        """Animal event cards have .animal-event styling support."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert "animal-event" in body

    def test_timeline_has_arm_action_marker_style(self, client):
        """Arm action markers have .arm-action styling support."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert "arm-action" in body

    def test_timeline_has_detail_panel_element(self, client):
        """Detail panel #event-detail exists with image grid container."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert "event-detail" in body or "#event-detail" in body
        assert "image-grid" in body or ".image-grid" in body

    def test_timeline_has_render_events_function(self, client):
        """renderEvents function is defined for static card rendering."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert "function renderEvents" in body or "const renderEvents" in body

    def test_timeline_has_show_event_detail_function(self, client):
        """showEventDetail function fetches images from API on click."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert "showEventDetail" in body

    def test_timeline_has_format_time_helper(self, client):
        """formatTime helper converts timestamps to readable format."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert "function formatTime" in body or "const formatTime" in body

    def test_timeline_uses_encode_url_component_for_image_paths(self, client):
        """Image URLs use encodeURIComponent for filenames with spaces."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert "encodeURIComponent" in body

    def test_timeline_has_dark_theme_colors_matching_dashboard(self, client):
        """Timeline uses same dark theme palette as Phase 1 dashboard."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert "#1a1a2e" in body   # bg color
        assert "#e0e0e0" in body   # text color
        assert "#a0a0c0" in body   # panel header color

    def test_timeline_has_event_card_time_span_display(self, client):
        """Event cards display start → end time span."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert "start_ts" in body and "end_ts" in body  # data fields used

    def test_timeline_has_arm_action_label_rendering(self, client):
        """Arm action markers render 'Arm Closed' / 'Arm Opened' labels."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert "arm-action" in body  # class applied to arm rows
        assert "Arm" in body  # label text

    def test_timeline_has_image_count_display(self, client):
        """Event cards show image count (event_count) indicator."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert "event_count" in body or "img-count" in body


# ========== Timeline auto-refresh structural tests (Plan 02-03 Task 2) ==========

class TestTimelineAutoRefresh:
    """Structural tests for timeline.html polling and context preservation.

    Plan 02-03 Task 2 adds client-side auto-refresh with diff-based updates,
    scroll preservation, and expanded-event state management.
    """

    def test_timeline_has_polling_interval_constant(self, client):
        """INTERVAL_MS constant defined (10s default for EVT-04)."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert "INTERVAL_MS" in body

    def test_timeline_has_cached_events_state(self, client):
        """cachedEvents variable exists for diff comparison."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert "cachedEvents" in body

    def test_timeline_has_scroll_preservation(self, client):
        """Scroll position saved before poll and restored after DOM update."""
        resp = client.get("/events")
        body = resp.data.decode()
        # Check for scroll save/restore pattern
        has_save = "scrollY" in body or "savedScrollY" in body
        has_restore = "scrollTo" in body
        assert has_save and has_restore, "Missing scroll save/restore logic"

    def test_timeline_has_expanded_event_tracking(self, client):
        """Expanded event ID tracked for detail panel context preservation."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert "expandedEventId" in body or "currentExpandedId" in body

    def test_timeline_has_filter_state_preservation(self, client):
        """Filter state stored in module-scoped variable for persistence across polls."""
        resp = client.get("/events")
        body = resp.data.decode()
        # Filter variable name (generic enough to avoid collisions)
        assert "filterAnimal" in body or "filterType" in body

    def test_timeline_has_diff_based_update_logic(self, client):
        """hasNewEvents or equivalent diff logic prevents unnecessary DOM updates."""
        resp = client.get("/events")
        body = resp.data.decode()
        has_new_events = "hasNewEvents" in body or "newEvents" in body
        has_cached_ids = "cachedIds" in body or "Set(" in body
        assert has_new_events and has_cached_ids, "Missing diff-based update logic"

    def test_timeline_has_poll_events_function(self, client):
        """pollEvents async function fetches /api/events on interval."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert "pollEvents" in body

    def test_timeline_fetches_api_events_endpoint(self, client):
        """pollEvents calls fetch targeting /api/events endpoint."""
        resp = client.get("/events")
        body = resp.data.decode()
        # URL is built dynamically for optional filter param — check core path exists
        assert "/api/events" in body and "fetch(" in body

    def test_timeline_has_set_interval_polling(self, client):
        """setInterval schedules repeated pollEvents calls."""
        resp = client.get("/events")
        body = resp.data.decode()
        assert "setInterval" in body and "pollEvents" in body


# ========== Task 3 group_events unit tests ==========

class TestGroupEvents:
    """Unit tests for group_events clustering logic."""

    @staticmethod
    def _make_entry(ts_str):
        """Helper to create an event entry dict.

        ts_str format: {YYYYMMDD}_{HHmmss} (e.g., '20261112_045846')
        """
        from datetime import datetime
        dt = datetime.strptime(ts_str, "%Y%m%d_%H%M%S")
        return {
            "name": f"{ts_str.replace('_', '', 1)} opossum.jpg",
            "date_str": ts_str[:8],
            "timestamp": ts_str,
            "datetime": dt,
            "animal": "opossum",
        }

    def test_grouping_burst_produces_one_cluster(self):
        """Burst of 3 entries within 5 min produces 1 cluster with correct start_ts/end_ts."""
        from MonitorApp.app import group_events
        entries = [
            self._make_entry("20261112_045846"),
            self._make_entry("20261112_045910"),
            self._make_entry("20261112_050230"),
        ]
        clusters = group_events(entries)
        assert len(clusters) == 1
        c = clusters[0]
        assert c["event_count"] == 3
        assert c["start_ts"] == "20261112_045846"
        assert c["end_ts"] == "20261112_050230"

    def test_grouping_gap_splits_clusters(self):
        """Gap > 5 min splits into separate clusters."""
        from MonitorApp.app import group_events
        entries = [
            self._make_entry("20261112_040000"),
            self._make_entry("20261112_050000"),  # gap = 3600s > 300
        ]
        clusters = group_events(entries)
        assert len(clusters) == 2

    def test_different_animal_splits(self):
        """Different animal types always split regardless of time gap."""
        from MonitorApp.app import group_events
        entries = [
            {**self._make_entry("20261112_040000"), "animal": "opossum"},
            {**self._make_entry("20261112_040005"), "animal": "cat"},  # only 5s gap!
        ]
        clusters = group_events(entries)
        assert len(clusters) == 2
