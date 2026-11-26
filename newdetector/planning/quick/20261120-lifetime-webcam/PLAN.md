---
slug: lifetime-webcam
date: 2026-11-20
status: complete
goal: Reduce DShow device handle churn by keeping webcam open across loop iterations
---

# Lifetime Webcam — Quick Task

## Problem
`WebcamCapture.CaptureFrame()` opens a DShow camera, grabs frames, disposes, and GC collects **every time**. During a single bad-animal response cycle, this happens 5-9 times in rapid succession. Each open/close is a full DShow device enumeration — stale handles accumulate and cause intermittent "camera unreachable" errors (observed ~once/day with the old detector.exe).

## Solution
Add lifecycle methods to `WebcamCapture` so the camera opens once at startup, stays open across all captures in the loop, and closes reliably on exit.

### Changes

#### 1. `WebcamCapture.cs` — add `Open()` + keep `_capture` alive between frames

- Add `public bool Open()` that initializes `_capture` via DShow and verifies it's opened. Reuses existing retry/backoff logic.
- Add `public byte[]? CaptureLive()` that reads from the already-open `_capture`, selects best contrast frame, returns JPEG bytes **without** disposing.
- Modify `CaptureFrame()` to remain as-is for backward compat (or deprecate — it won't be called).
- Keep `Dispose()` as-is; it already handles cleanup + GC pattern.
- Add a `bool IsOpen` property for defensive checks in Program.cs.

Key behavior:
```
Open()   → VideoCapture(dshow), verify IsOpened(), return bool
CaptureLive() → read frames from _capture, no open/close, no GC collect
Dispose() → dispose _capture, GC collect (existing pattern)
```

#### 2. `Program.cs` — wire lifecycle around the loop

Before the loop:
```csharp
var webcam = new WebcamCapture(systemConfig.Webcam!);
if (!webcam.Open()) {
    Console.WriteLine("[Fatal] Failed to open webcam. Exiting.");
    return;
}
```

Inside the loop: replace all `webcam.CaptureFrame()` → `webcam.CaptureLive()`.

After the loop: keep `webcam.Dispose()` as-is.

#### 3. Test impact
- `WebcamCaptureTests.cs` — existing tests use `CaptureFrame()` which still works. No breakage expected.

## Files to modify
1. `Modules/Webcam/WebcamCapture.cs` — add `Open()`, `CaptureLive()`, keep `_capture` alive
2. `Program.cs` — wire lifecycle, replace calls

## Risk & Mitigation
- **Risk**: Keeping `_capture` alive means unmanaged DShow handle stays open longer.
  **Mitigation**: This is the goal. The old close+GC pattern was meant to release but didn't prevent accumulation. Explicit lifecycle gives us deterministic control.
- **Risk**: `CaptureLive()` on a closed/disposed capture returns null.
  **Mitigation**: Guard with `IsOpen` check at top of `CaptureLive()`.

## Verification
1. Build succeeds.
2. Run app — verify webcam opens, captures frames, detection works.
3. Monitor for ~1 hour (vs old "once/day" failure). No DShow handle errors.
