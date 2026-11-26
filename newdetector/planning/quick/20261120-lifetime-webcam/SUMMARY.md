---
slug: lifetime-webcam
date: 2026-11-20
status: complete
summary: WebcamCapture now uses Open()/CaptureLive()/Dispose() lifecycle instead of per-frame open/close. Eliminates ~9 DShow device enumeration cycles per loop iteration, reducing stale-handle errors.
---

# Summary: Lifetime Webcam

## What Changed

### `Modules/Webcam/WebcamCapture.cs`
- **Added** `Open()` — opens DShow camera with retry/backoff, keeps `_capture` alive after return
- **Added** `CaptureLive()` — reads frames from already-open `_capture`, no open/close
- **Added** `IsOpen` property for defensive checks
- **Deprecated** `CaptureFrame()` — marked `[Obsolete]`, kept for backward compat (tests still use it)
- **Modified** `RunCapture()` — removed internal dispose/GC pattern; caller now manages lifecycle

### `Program.cs`
- **Added** `webcam.Open()` call before the main loop with fatal-exit on failure
- **Replaced** all 6 `webcam.CaptureFrame()` calls with `webcam.CaptureLive()` (1 already done via the first replacement)
- Kept `webcam.Dispose()` after the loop — unchanged

## Why
`CaptureFrame()` opened DShow → grabbed frames → disposed + GC collect on every call. During a bad-animal response cycle, this happened 5-9 times in rapid succession. Stale DShow handles accumulated, causing intermittent "camera unreachable" errors (~once/day with old detector.exe).

## Impact
- **Before:** ~1 device open/close per frame = 6-9 cycles every 100ms loop iteration
- **After:** 1 device open at startup = held across all captures until `Dispose()` on exit
- **Net reduction:** ~99% fewer DShow device enumeration events

## Verification
- Build: ✅ succeeds (0 errors)
- Camera tests: ✅ 6/6 pass (backward compat preserved)
- Detection tests: ✅ 8/8 pass
- Behavioral: ⏳ needs live camera hardware testing

## Risk
Keeping the unmanaged DShow handle open longer is the intended goal. The old close+GC pattern was a band-aid that didn't prevent accumulation. Explicit lifecycle gives deterministic control.
