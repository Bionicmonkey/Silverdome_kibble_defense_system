# Phase 02 Plan 01: Webcam Capture Module Summary

**One-liner:** WebcamCapture class implementing DShow camera open with retry, grayscale MeanStdDev contrast scoring across 5 frames, best-frame JPEG save as temp.jpg with explicit GC handle release, wired into Program.cs main loop.

## Frontmatter

```yaml
phase: 02-webcam-capture-module
plan: 01
subsystem: Camera
tags: [webcam, opencvsharp4, dshow, capture]
dependency_graph:
  requires: []
  provides: WebcamCapture class returning byte[]? frame captures
  affects: Program.cs, system.json (runtime behavior)
tech_stack:
  added: []
  patterns: DShow backend, MeanStdDev contrast scoring, explicit GC release, configurable retry with linear backoff
key_files:
  created:
    - Modules/Webcam/WebcamCapture.cs
  modified:
    - Program.cs
decisions:
  - Use FourCC.MJPG enum (not Cv2.FourCC()) — OpenCvSharp4 4.10.0 API compatibility
  - Single VideoCapture per CaptureFrame call — avoid handle accumulation across calls
  - No using var wrapper on webcam variable in Main() — explicit dispose called after loop for error resilience
metrics:
  duration: ~5 min
  completed: 2026-11-19T13:00Z
  actuals:
    tokens: 38000
    tasks: 2
    commits: 2
status: complete
```

## Execution Summary

### Tasks Completed

| Task | Name | Type | Commit | Files |
|------|------|------|--------|-------|
| 1 | WebcamCapture class — DShow open, contrast scoring, frame selection, GC release end-to-end | tracer | `7199cde` | Modules/Webcam/WebcamCapture.cs |
| 2 | Wire WebcamCapture into Program.cs main loop | auto | `c74c5b2` | Program.cs |

### Deviations from Plan

**1. [Rule 2 - Missing Critical API] FourCC API compatibility fix for OpenCvSharp4 4.10.0**
- **Found during:** Task 1 build verification
- **Issue:** Plan specified `Cv2.FourCC('M', 'J', 'P', 'G')` but OpenCvSharp4 4.10.0 does not expose FourCC as a static method on the Cv2 class — it is a standalone struct with static MJPG property: `FourCC.MJPG`.
- **Fix:** Changed `_capture.Set(VideoCaptureProperties.FourCC, Cv2.FourCC('M', 'J', 'P', 'G'))` to `_capture.Set(VideoCaptureProperties.FourCC, FourCC.MJPG)`. This is functionally equivalent — both set the MJPG FourCC code (0x47504A4D = 1198490669).
- **Files modified:** Modules/Webcam/WebcamCapture.cs (line 73)
- **Commit:** `7199cde`

**2. [Auto-added] Added using NewDetector.Config to WebcamCapture.cs**
- **Found during:** Task 1 build verification
- **Issue:** WebcamCapture class references `WebcamConfig` type which is in namespace `NewDetector.Config`. No using directive was present, causing CS0246 compile error.
- **Fix:** Added `using NewDetector.Config;` to the top of WebcamCapture.cs.
- **Files modified:** Modules/Webcam/WebcamCapture.cs
- **Commit:** `7199cde`

### Verification Results

All 8 plan verification assertions passed:

| # | Assertion | Result |
|---|-----------|--------|
| 1 | CaptureFrame method present | PASS (count: 1) |
| 2 | DShow backend used | PASS (count: 1) |
| 3 | GC.Collect at 2 mandatory sites | PASS (count: 2) |
| 4 | WaitForPendingFinalizers at 2 mandatory sites | PASS (count: 2) |
| 5 | MeanStdDev contrast scoring present | PASS (count: 1) |
| 6 | WebcamCapture instantiated from config | PASS (count: 1) |
| 7 | webcam.CaptureFrame() call in main loop | PASS (count: 1) |
| 8 | dotnet build with 0 errors | PASS |

### Build Output

```
Build succeeded.
    1 Warning(s): CS8602 on Program.cs line 43 (pre-existing null deref warning, not introduced by this plan)
    0 Error(s)
```

## Success Criteria

| Criterion | Status |
|-----------|--------|
| All tasks executed per PLAN.md | PASS |
| Each task committed individually | PASS (7199cde, c74c5b2) |
| SUMMARY.md created in plan directory | PASS |
| dotnet build succeeds with 0 errors | PASS |
| WebcamCapture class: constructor(WebcamConfig), CaptureFrame() byte[]?, RunCapture() private, Dispose() public | PASS |
| GC handle release pattern present after ImWrite AND in Dispose() | PASS (2 sites) |
| DShow backend per OpenCvSharp4 4.10.0 | PASS |
| Retry logic with configurable backoff | PASS |
| Camera index and retry config read from WebcamConfig | PASS |

## Commits

| Commit | Message |
|--------|---------|
| `7199cde` | feat(02-webcam-capture-module-01): implement WebcamCapture class |
| `c74c5b2` | feat(02-webcam-capture-module-01): wire WebcamCapture into Program.cs main loop |

## Key Architectural Decisions

1. **FourCC.MJPG over Cv2.FourCC()**: OpenCvSharp4 4.10.0 API compatibility — FourCC is a standalone struct with static MJPG property, not a Cv2 method.
2. **No using wrapper on webcam in Main()**: Explicit dispose after the while loop ensures cleanup runs even if CaptureFrame throws unexpectedly.
3. **Single VideoCapture per CaptureFrame call**: Creates fresh VideoCapture each call and lets GC reclaim it — avoids handle accumulation between calls (Pitfall 2).
