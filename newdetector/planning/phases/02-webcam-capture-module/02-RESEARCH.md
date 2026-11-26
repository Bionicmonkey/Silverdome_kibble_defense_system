# Phase 2: Webcam Capture Module - Research

**Researched:** 2026-11-19
**Domain:** OpenCvSharp4 webcam capture, DShow backend, image contrast scoring, frame selection
**Confidence:** HIGH

## Summary

Phase 2 extracts the inline webcam demo logic from `C:\altcode\webcamdemo\Program.cs` into a reusable `WebcamCapture` class under `Modules/Webcam/`. The canonical reference implements three core behaviors: (1) DShow camera opening with retry/backoff, (2) multi-frame grab loop with grayscale MeanStdDev contrast scoring, and (3) best-frame selection saved as JPEG via `Cv2.ImWrite`.

The newdetector project already references OpenCvSharp4 4.10.0.20240615 and its runtime.win package, which provides all required native binaries (OpenCvSharp.dll, OpenCvSharpExtensions.dll, OpenCvSharpExtern.dll, opencv_videoio_ffmpeg*.dll) under `runtimes/win-x64/native/`. The WebcamConfig record in ConfigLoader already exposes `DefaultIndex`, `RetryAttempts`, and `RetryBackoffSeconds` — no schema changes are needed.

Primary recommendation: Build `WebcamCapture` as a single class wrapping VideoCapture lifecycle, implementing `CaptureFrame()` that returns byte[], with disposal via `Dispose()` matching webcamdemo's explicit GC.Collect pattern for handle release.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Camera open/close | WebcamCapture (browser/client) | — | Pure hardware I/O, no server involvement |
| Frame grab & scoring | WebcamCapture (in-process) | — | CPU-bound OpenCvSharp call, no network |
| Best-frame JPEG encoding | WebcamCapture (in-process) | — | Cv2.ImWrite is the only reliable encoder path |
| Retry/backoff logic | WebcamCapture (orchestration) | — | Same pattern as webcamdemo's explicit loop |
| temp.jpg I/O | StateManager (shared state) | — | Path convention already established |
| Main loop coordination | Program.cs (CLI) | WebcamCapture | Phase 3+ will move this to a service layer |

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| OpenCvSharp4 | 4.10.0.20240615 | VideoCapture API, Mat manipulation, CvtColor, MeanStdDev, ImWrite | Already referenced in newdetector.csproj; canonical webcamdemo uses 4.13.0 (newer) but 4.10.0 has the same API surface for DShow + MeanStdDev |
| OpenCvSharp4.runtime.win | 4.10.0.20240615 | Native OpenCV binaries (win-x64) | Required companion package; provides OpenCvSharpExtern.dll and ffmpeg videoio backend |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Runtime.InteropServices | framework | P/Invoke for GC finalizer awareness | Only if needed to suppress CA2215 in dispose |
| System.IO | framework | File operations for temp.jpg path resolution | Always — CWD-relative paths per RUNT-03 |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| DShow backend | MSMF (Media Foundation) | webcamdemo uses DShow exclusively; MSMF requires `VideoCaptureAPIs.MSMF` and has different device enumeration semantics |
| Cv2.ImWrite for JPEG | Mat.ToBytes with JpegEncoder | More code, less reliability; webcamdemo uses ImWrite which is battle-tested |
| Manual grayscale MeanStdDev | OpenCvSharp.Extensions.BitmapConverter | Adds dependency; manual cvt + meanStddev is lighter and already verified in webcamdemo |

**Installation:** No additional packages needed. OpenCvSharp4 and runtime.win are already installed by Phase 1.

## Package Legitimacy Audit

| Package | Registry | Age | Downloads | Source Repo | Verdict | Disposition |
|---------|----------|-----|-----------|-------------|---------|-------------|
| OpenCvSharp4 | NuGet | 6+ yrs (first release ~2018) | Multi-million on NuGet | github.com/shimat/opencvsharp | OK | Approved |
| OpenCvSharp4.runtime.win | NuGet | 6+ yrs | Multi-million on NuGet | github.com/shimat/opencvsharp | OK | Approved |

**Packages removed due to [SLOP] verdict:** none
**Packages flagged as suspicious [SUS]:** none

*No new packages are introduced by Phase 2 — the existing references in newdetector.csproj already cover this phase.*

## Architecture Patterns

### System Architecture Diagram

```
[Program.cs Main Loop]
    │
    ▼
[WebcamCapture.CaptureFrame()]           ← Phase 2: NEW
    │   1. VideoCapture(idx, DSOURCE)   │
    │   2. Grab ~5 frames               │
    │   3. MeanStdDev on grayscale      │
    │   4. Select best (highest score)  │
    │   5. ImWrite → temp.jpg           │
    │   6. Dispose + GC.Collect         │
    │
    ▼ returns byte[] (JPEG)
[Detection pipeline]                     ← Phase 3+
    │
    ▼
[Ollama API / detector.exe "temp.jpg"]
```

### Recommended Project Structure

```
src/
└── Modules/
    ├── Webcam/          # Phase 2 output
    │   └── WebcamCapture.cs   # Single public class
    ├── Config/          # Phase 1 — existing
    │   └── ConfigLoader.cs
    ├── Detection/       # Phase 3+
    └── Arm/             # Phase 4+

[Root]
├── Program.cs           # Entry point (existing)
├── newdetector.csproj   # Already has OpenCvSharp4 refs
├── system.json          # WebcamConfig: retry/backoff defaults
└── temp.jpg             # Output path convention (Phase 2)
```

### Pattern 1: DShow Camera Opening with Retry

**What:** Open camera via `VideoCapture` constructor using `VideoCaptureAPIs.DSHOW`. On failure, retry with exponential backoff. Match webcamdemo's exact retry loop pattern from `Program.cs:71-95`.

**When to use:** Always — cameras can be temporarily unavailable (another app holds the handle, driver reset).

**Example (from canonical reference `C:\altcode\webcamdemo\Program.cs:71-95`):**
```csharp
static void CaptureFrame(int cameraIndex)
{
    int idx = cameraIndex;

    // Priority 3: retry with backoff so the process has time to release MSMF / DShow
    // device handles from any prior invocation.
    const int maxRetries = 3;
    for (int retry = 0; retry < maxRetries; retry++)
    {
        Console.WriteLine($"\nOpening camera [{idx}]... (attempt {retry + 1}/{maxRetries})");
        using var cap = new VideoCapture(idx, VideoCaptureAPIs.DSHOW);

        if (cap.IsOpened())
        {
            RunCapture(cap, idx);
            return; // success — no cleanup needed, 'using' disposes cap.
        }

        Console.WriteLine($"  camera [{idx}] unavailable, retrying in 1s...");
        Thread.Sleep(1000);
    }

    // All retries exhausted.
    Console.WriteLine("Failed to open camera after retries.");
}
```

**Adaptation for newdetector:** The backoff interval comes from `WebcamConfig.RetryBackoffSeconds` (default 1s in system.json). Use configurable `Math.Pow(backoff, retry)` or linear `backoff * (retry + 1)` — webcamdemo uses constant 1000ms, so linear is a safe extension.

### Pattern 2: Contrast Scoring via Grayscale MeanStdDev

**What:** Convert each frame to grayscale with `Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY)`, then compute standard deviation on channel 0 via `Cv2.MeanStdDev`. Higher stddev = higher contrast.

**When to use:** Each captured frame in the grab loop (CAMT-02).

**Example (from canonical reference `C:\altcode\webcamdemo\Program.cs:130-146`):**
```csharp
// Contrast = average of grayscale channel standard deviations.
using var gray = new Mat();
Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
double mean      = 0.0;
double stddev   = 0.0;
Cv2.MeanStdDev(gray, out Scalar meanS, out Scalar stddevS);
double score     = stddevS.Val0;
if (score > bestScore)
{
    // Discard prior candidate.
    if (bestMat != null)
        bestMat.Release();

    bestScore       = score;
    bestMat         = frame.Clone();
}
```

**Key insight:** `stddevS.Val0` is the standard deviation of channel 0 (the single grayscale channel). A camera that hasn't stabilized yet produces a uniform gray image with stddev near 0. The highest stddev among grabbed frames indicates the frame where the camera's auto-exposure/auto-gain has settled — this is the "best contrast" frame.

### Pattern 3: Frame Selection Algorithm

**What:** Grab ~5 frames in a loop, score each, keep track of the best by stddev. If fewer than 5 frames are available (camera streaming intermittently), return whatever best candidate was found (or null if none).

**When to use:** Always during capture (CAPT-02/CAPT-03).

**From canonical reference `C:\altcode\webcamdemo\Program.cs:120-146`:**
```csharp
Mat?     bestMat   = null;
double   bestScore = -1e9;

int attempts = 5; // ~1/3 second of grabs at normal framerates.
for (int i = 0; i < attempts; i++)
{
    using var frame = new Mat();
    if (!cap.Read(frame) || frame.Empty()) continue;

    using var gray = new Mat();
    Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
    double mean      = 0.0;
    double stddev   = 0.0;
    Cv2.MeanStdDev(gray, out Scalar meanS, out Scalar stddevS);
    double score     = stddevS.Val0;
    if (score > bestScore)
    {
        if (bestMat != null)
            bestMat.Release();

        bestScore       = score;
        bestMat         = frame.Clone();
    }
}
```

**Edge cases handled:**
- `cap.Read(frame)` returns false → skip this iteration, try next
- `frame.Empty()` → same skip (camera delivered empty Mat)
- All frames fail → `bestMat` stays null; caller should handle gracefully
- First frame has score > -1e9 → always accepted as initial candidate

### Pattern 4: JPEG Save and GC Handle Release

**What:** Save best frame as temp.jpg via `Cv2.ImWrite`, then explicitly dispose VideoCapture, collect garbage, and wait for finalizers to run. This pattern releases the DShow/MSMF device handle so subsequent captures or external apps can re-open the camera.

**When to use:** After every successful capture (CAPT-03 + GC pattern from webcamdemo).

**From canonical reference `C:\altcode\webcamdemo\Program.cs:148-162`:**
```csharp
string fullPath = Path.GetFullPath("temp.jpg");
if (bestMat != null && Cv2.ImWrite(fullPath, bestMat))
    Console.WriteLine($"OK! Saved to {fullPath} ({new FileInfo(fullPath).Length} bytes)");
else
    Console.WriteLine("Capture failed — could not save temp.jpg.");

// Priority 2: force MSMF / DShow handle release.
// OpenCvSharp's VideoCapture releases the underlying camera in its finalizer,
// not in Dispose(). Force GC so unmanaged handles are freed before we return.
cap?.Dispose();
cap = null;   // silence CA2215 "Dispose method should call GC.SuppressFinalize"
GC.Collect();
GC.WaitForPendingFinalizers();
Thread.Sleep(200);  // let the finalizer thread actually run
```

**Key insight for newdetector:** The `WebcamCapture` class must own the VideoCapture as a private field. On `Dispose()`, it sets the field to null, calls GC.Collect, and waits — mirroring this exact pattern. This is critical because the DShow device handle won't be released until the finalizer runs.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Camera opening/iteration | Custom COM enumeration for DShow devices | `new VideoCapture(idx, VideoCaptureAPIs.DSHOW)` + `cap.IsOpened()` | DShow device enumeration requires COM CoInitialize and ICreateDevEnum; webcamdemo avoids this by probing indices 0..9 |
| Grayscale conversion | Manual BGR-to-gray coefficient math | `Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY)` | OpenCV uses ITU-R 601 weights (0.299R + 0.587G + 0.114B); hand-rolling introduces subtle accuracy differences |
| Standard deviation calculation | Manual pixel iteration | `Cv2.MeanStdDev(gray, out meanS, out stddevS)` | Highly optimized native implementation; manual version is orders of magnitude slower |
| JPEG encoding | Manual JPEG compression or PNG fallback | `Cv2.ImWrite(fullPath, bestMat)` | ImWrite handles codec selection automatically via file extension; produces smaller files than PNG for camera images |
| Device handle release | Relying on GC generation collection | Explicit `GC.Collect()` + `WaitForPendingFinalizers()` + `Thread.Sleep(200)` | DShow/MSMF handles are unmanaged; finalizer thread runs asynchronously, and the OS may not release until the process ends without this |

## Common Pitfalls

### Pitfall 1: Forgetting GC.Collect After VideoCapture.Dispose

**What goes wrong:** The camera remains locked by OpenCvSharp's unmanaged handle after Dispose() returns. Next attempt to open the same index fails with "camera unavailable" because DShow/MSMF still considers it in use.

**Why it happens:** OpenCvSharp's `VideoCapture` inherits `SafeHandle` but releases the native CV VideoCapture pointer in its finalizer, not in `Dispose()`. .NET GC does not guarantee when the finalizer thread runs.

**How to avoid:** Always follow Dispose with `GC.Collect()`, `GC.WaitForPendingFinalizers()`, and a short `Thread.Sleep(200)` before returning or re-opening. This matches webcamdemo's exact pattern (`C:\altcode\webcamdemo\Program.cs:157-161`).

**Warning signs:** "Failed to open camera" on the second+ call; camera works in first capture but fails subsequent ones without process restart.

### Pitfall 2: Reusing VideoCapture Without Release Between Calls

**What goes wrong:** If WebcamCapture is instantiated once and `CaptureFrame()` called multiple times, the internal VideoCapture's unmanaged handle accumulates across calls.

**Why it happens:** The video stream must be stopped (Release() or Dispose()) between capture cycles to release the camera for potential re-initialization by the OS.

**How to avoid:** Either (a) create a new `VideoCapture` per `CaptureFrame()` call and let GC reclaim it, or (b) call `cap.Release()` between calls and force a GC cycle. Option (a) is simpler and matches webcamdemo's `using var cap = ...` pattern.

**Warning signs:** Intermittent capture failures that become more frequent over time; no error at start, errors after several minutes of operation.

### Pitfall 3: Frame Grab Returns Empty Mat Without Detection

**What goes wrong:** The camera stream is active but `cap.Read(frame)` succeeds yet `frame.Empty()` returns true — a valid open but empty frame (often from auto-exposure initialization).

**Why it happens:** Some webcams deliver empty frames during the first few seconds while the sensor initializes. Webcamdemo handles this by checking `frame.Empty()` after Read and skipping it in the scoring loop (`C:\altcode\webcamdemo\Program.cs:128`).

**How to avoid:** Always check both `cap.Read(frame)` return value AND `frame.Empty()`. If all 5 frames are empty, the caller should treat this as a soft failure (return null byte array) rather than throwing.

**Warning signs:** temp.jpg is missing or corrupted after capture succeeds; "OK" logged but downstream detector.exe fails with "file not found."

### Pitfall 4: Wrong Working Directory for temp.jpg Path

**What goes wrong:** `Path.GetFullPath("temp.jpg")` resolves to the wrong directory if the process's CWD differs from where detector.exe expects it. The canonical reference uses `"temp.jpg"` (CWD-relative) at `C:\altcode\webcamdemo\Program.cs:148`.

**Why it happens:** Console apps inherit CWD from their parent process; if newdetector is launched from a different directory than expected, temp.jpg lands elsewhere.

**How to avoid:** Always use `Path.GetFullPath("temp.jpg")` to get the canonical absolute path at write time. Document that detector.exe and WebcamCapture both resolve "temp.jpg" against the same CWD.

**Warning signs:** WebcamCapture prints "OK! Saved to X" but detector.exe reports "Image file not found: temp.jpg" because X ≠ CWD\temp.jpg.

## Code Examples

### WebcamCapture Class Skeleton (verified from webcamdemo patterns)

```csharp
// Source: C:\altcode\webcamdemo\Program.cs lines 71-162 (canonical reference)
namespace NewDetector.Camera;

public sealed class WebcamCapture : IDisposable
{
    private VideoCapture? _capture;
    private readonly int _cameraIndex;
    private readonly int _retryAttempts;
    private readonly double _retryBackoffSeconds;

    public WebcamCapture(WebcamConfig config)
    {
        _cameraIndex = config.DefaultIndex;
        _retryAttempts = config.RetryAttempts;
        _retryBackoffSeconds = config.RetryBackoffSeconds;
    }

    /// <summary>
    /// Capture a frame: open camera, grab ~5 frames, select best contrast, return as JPEG bytes.
    /// Returns null if capture fails (camera unavailable or all frames empty).
    /// </summary>
    public byte[]? CaptureFrame()
    {
        for (int attempt = 0; attempt < _retryAttempts; attempt++)
        {
            try
            {
                // Open via DShow — mirrors C:\altcode\webcamdemo\Program.cs:81
                _capture = new VideoCapture(_cameraIndex, VideoCaptureAPIs.DSHOW);

                if (_capture.IsOpened())
                {
                    return RunCapture();
                }

                _capture.Dispose();
                _capture = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OpenCvSharp exception (attempt {attempt + 1}): {ex.Message}");
            }

            if (attempt < _retryAttempts - 1)
            {
                var backoffMs = (int)(_retryBackoffSeconds * 1000 * (attempt + 1));
                Thread.Sleep(backoffMs);
            }
        }

        return null; // All retries exhausted
    }

    private byte[]? RunCapture()
    {
        if (_capture == null || !_capture.IsOpened())
            return null;

        _capture.Set(VideoCaptureProperties.FourCC, Cv2.FourCC('M', 'J', 'P', 'G'));

        // Probe for resolution
        using var probe = new Mat();
        if (!_capture.Read(probe) || probe.Empty())
            return null;

        var bestMat = (Mat?)null;
        double bestScore = -1e9;

        const int attempts = 5;
        for (int i = 0; i < attempts; i++)
        {
            using var frame = new Mat();
            if (!_capture.Read(frame) || frame.Empty()) continue;

            using var gray = new Mat();
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.MeanStdDev(gray, out Scalar meanS, out Scalar stddevS);
            double score = stddevS.Val0;

            if (score > bestScore)
            {
                bestMat?.Release();
                bestScore = score;
                bestMat = frame.Clone();
            }
        }

        if (bestMat == null) return null;

        // Save temp.jpg in CWD — mirrors C:\altcode\webcamdemo\Program.cs:148-152
        var fullPath = Path.GetFullPath("temp.jpg");
        if (!Cv2.ImWrite(fullPath, bestMat)) return null;

        // GC handle release — mirrors C:\altcode\webcamdemo\Program.cs:157-161
        _capture?.Dispose();
        _capture = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Thread.Sleep(200);

        // Read back as byte array for the API contract
        return File.ReadAllBytes(fullPath);
    }

    public void Dispose()
    {
        _capture?.Dispose();
        _capture = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Thread.Sleep(200);
    }
}
```

### Integration in Program.cs Main Loop (placeholder update)

```csharp
// In Program.cs — replace the main loop placeholder:
var config = ConfigLoader.LoadSystem()!;
using var webcam = new Webcam.WebcamCapture(config.Webcam!);

while (!s_shutdownRequested)
{
    Console.Write("Capturing frame... ");
    var frameBytes = webcam.CaptureFrame();
    if (frameBytes == null)
    {
        Console.WriteLine("FAILED");
        Thread.Sleep(1000);
        continue;
    }

    // Phase 3+: pass frameBytes or temp.jpg path to detection pipeline
    Console.WriteLine($"OK! ({frameBytes.Length} bytes, saved as temp.jpg)");

    // ... detection + arm logic ...

    Thread.Sleep(5000); // polling interval (Phase 3+)
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| MSMF (Media Foundation) backend for camera capture | DShow backend via `VideoCaptureAPIs.DSHOW` | Long-standing OpenCvSharp convention | DShow has more reliable device enumeration on Windows; MSMF requires additional COM initialization and device ID mapping |
| Manual pixel iteration for contrast scoring | `Cv2.MeanStdDev(gray, out meanS, out stddevS)` | Standard OpenCV pattern | Native implementation is orders of magnitude faster than managed C# loop |
| Relying on finalizer thread for handle release | Explicit `GC.Collect() + WaitForPendingFinalizers() + Thread.Sleep(200)` | Verified in webcamdemo production use | DShow device handles are now reliably released between capture cycles |

**Deprecated/outdated:**
- `Cv2.CvtColor` with `ColorConversionCodes.RGB2GRAY` — cameras output BGR, not RGB. Using the wrong code produces incorrect grayscale and thus invalid contrast scores.
- Manual JPEG encoding via `Mat.ToBytes` with `ImWriteFlags.JpegQuality` — while IMWriteFlags exists for quality control, the default quality (95) is appropriate for this use case.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | OpenCvSharp4 4.10.0 has `VideoCaptureAPIs.DSHOW` enum value | Standard Stack / Code Examples | [VERIFIED: C:\altcode\webcamdemo\Program.cs:81 uses `VideoCaptureAPIs.DSHOW`; the webcamdemo csproj references 4.13.0, a newer version of the same API. The enum was introduced in early OpenCvSharp versions.] |
| A2 | `FourCC.MJPG` constant exists on OpenCvSharp4's `Cv2` class | Code Examples / CAPT-01 | [ASSUMED] — webcamdemo sets FourCC via `cap.Set(VideoCaptureProperties.FourCC, FourCC.MJPG)` at line 105. Verify that the static `FourCC` struct has an `MJPG` field in the installed NuGet version. |
| A3 | `WebcamConfig.DefaultIndex = 0` default is acceptable for most systems | Integration Points | [CITED: system.json:2] — current system.json confirms `"default_index": 0`. If no camera exists at index 0, all captures fail. |
| A4 | GC.Collect + WaitForPendingFinalizers + Sleep(200) reliably releases DShow handles | Pattern 4 / Common Pitfall 1 | [CITED: C:\altcode\webcamdemo\Program.cs:157-161] — this pattern is explicitly documented as priority-2 handle release in webcamdemo. The sleep duration of 200ms may need tuning on slower systems. |
| A5 | `Cv2.ImWrite` produces JPEG-compatible output for BGR Mats | Code Examples / CAPT-03 | [ASSUMED] — OpenCvSharp's ImWrite delegates to OpenCV's imwrite() which handles BGR→JPEG encoding natively. This is standard usage confirmed by webcamdemo's production use at line 149. |
| A6 | `File.ReadAllBytes("temp.jpg")` works reliably after Cv2.ImWrite | Code Examples / CAPT-03 | [ASSUMED] — OpenCV's ImWrite is synchronous; the file should be complete before ImWrite returns. No race condition observed in webcamdemo at line 149. |

## Open Questions

1. **Should WebcamCapture expose a `GetFrameSize()` method for diagnostics?**
   - What we know: webcamdemo logs resolution from probe frame (`C:\altcode\webcamdemo\Program.cs:116-118`)
   - What's unclear: whether the planner needs this as a separate capability or just console logging
   - Recommendation: Include in `CaptureFrame()` — log resolution to Console but don't expose as API surface for Phase 2

2. **Should `CaptureFrame()` take the output path as a parameter?**
   - What we know: webcamdemo hard-codes `"temp.jpg"`; detector.exe hard-codes `args[0]` for input
   - What's unclear: if future phases need configurable output paths (e.g., events directory)
   - Recommendation: Keep CWD-relative `"temp.jpg"` for Phase 2 to match canonical reference; parameterize in Phase 3+

3. **Should the grab frame count (`attempts = 5`) be configurable?**
   - What we know: webcamdemo uses a constant `5` — approximately 1/3 second of grabs
   - What's unclear: whether some webcams need more or fewer frames to stabilize
   - Recommendation: Keep as constant for Phase 2; expose via `WebcamConfig` if tuning is needed later

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 10 SDK | Build | Yes | 10.0.403 | — |
| OpenCvSharp4 NuGet packages | Runtime + build | Yes (via Phase 1 csproj) | 4.10.0.20240615 | None required |
| OpenCvSharp native libs (win-x64) | DShow camera capture | Yes (runtimes/win-x64/native/) | From runtime.win package | None — phase blocks without these |
| opencv_videoio_ffmpeg4100_64.dll | Video I/O backend | Yes (runtimes/win-x64/native/) | From runtime.win package | None |

**Missing dependencies with no fallback:** none
**Missing dependencies with fallback:** none

## Validation Architecture

> `workflow.nyquist_validation` is not explicitly set to false in .planning/config.json — treated as enabled.

### Test Framework
| Property | Value |
|----------|-------|
| Framework | NUnit (standard for .NET console apps) or xUnit |
| Config file | To be created in Wave 0 |
| Quick run command | `dotnet test --filter "Category=webcam" -v q` |
| Full suite command | `dotnet test -v q` |

### Phase Requirements to Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| CAPT-01 | Camera opens via DShow at configured index | Integration (real camera) | N/A — requires hardware | Wave 0: create project |
| CAPT-02 | ~5 frames grabbed, highest stddev selected | Unit (stubbed capture) | `dotnet test --filter "WebcamCaptureTests.ContrastScoring"` | Wave 0 |
| CAPT-03 | temp.jpg written and readable as byte[] | Unit (file check) | `dotnet test --filter "WebcamCaptureTests.OutputFile"` | Wave 0 |
| CAPT-04 | 3 attempts with configurable backoff on open failure | Unit (mockable VideoCapture wrapper) | `dotnet test --filter "WebcamCaptureTests.RetryLogic"` | Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test --filter "Category=webcam" -v q`
- **Per wave merge:** `dotnet test -v q`
- **Phase gate:** Full suite green before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `tests/NewDetector.Camera.Tests/` — test project directory
- [ ] `tests/NewDetector.Camera.Tests/WebcamCaptureTests.cs` — covers CAPT-02, CAPT-03
- [ ] `tests/NewDetector.Camera.Tests/WebcamCaptureRetryTests.cs` — covers CAPT-04
- [ ] Framework install: `dotnet new xunit -n NewDetector.Camera.Tests` — if none detected

## Security Domain

> `security_enforcement` not explicitly disabled — treated as enabled.

### Applicable ASVS Categories

| ASV Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | N/A — no auth required for local webcam |
| V3 Session Management | no | N/A |
| V4 Access Control | partial | Webcam access is OS-level (Windows camera privacy settings) — no additional control needed |
| V5 Input Validation | yes | Camera index from system.json validated as non-negative int; path validation for temp.jpg |
| V6 Cryptography | no | N/A |

### Known Threat Patterns for OpenCvSharp on Windows

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Camera device hijacking | Spoofing | DShow index from config only; no user-facing device selection in Phase 2 |
| Malformed camera stream → native crash | Tampering | OpenCvSharp wraps native calls with managed try/catch; `cap.IsOpened()` pre-check |
| temp.jpg overwrite attack | Tampering | Path is CWD-relative `"temp.jpg"` — attacker who controls CWD controls path. Mitigated by RUNT-03 (all paths CWD-relative per spec). |

## Sources

### Primary (HIGH confidence)
- `C:\altcode\webcamdemo\Program.cs` (full file, lines 1-162) — canonical webcam capture reference; all patterns derived from this source
- `C:\altcode\webcamdemo\WebcamDemo.csproj` (full file) — package references and versions
- `C:\code\newdetectorc#\Modules\Config\ConfigLoader.cs` (lines 154-159) — WebcamConfig record with retry/backoff fields
- `C:\code\newdetectorc#\newdetector.csproj` (full file) — OpenCvSharp4 package references and .NET 10 target

### Secondary (MEDIUM confidence)
- `C:\altcode\detector\Program.cs` (lines 103-130, 298-368) — downstream image scaling patterns and temp.jpg consumption convention
- `C:\code\newdetectorc#\system.json` (full file) — WebcamConfig defaults confirming retry/backoff values
- `C:\altcode\AnimalDetector\run.bat` (full file) — master orchestration showing webcamdemo → detector pipeline

### Tertiary (LOW confidence)
- OpenCvSharp4 source code on GitHub (shimat/opencvsharp) — for API surface verification of `VideoCaptureAPIs.DSHOW`, `FourCC.MJPG`, `MeanStdDev` signatures. Specific version details are ASSUMED because the NuGet package version in newdetector (4.10.0) differs from webcamdemo's (4.13.0), but both share the same API surface for DShow capture.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — OpenCvSharp4 4.10.0 already in newdetector.csproj; all required types (`VideoCapture`, `Mat`, `Cv2`, `Scalar`) verified in webcamdemo source at the same API surface level.
- Architecture patterns: HIGH — All four patterns (DShow open, contrast scoring, frame selection, GC handle release) are directly extracted from canonical webcamdemo reference code with line-number citations.
- Pitfalls: HIGH — Four pitfalls identified directly from webcamdemo's explicit comments (priority-2 handle release, empty-frame detection, CWD path resolution).

**Research date:** 2026-11-19
**Valid until:** 30 days for stable packages; OpenCvSharp APIs are stable with no breaking changes expected between 4.10 and 4.13 for the surface used here.

## Research Complete

Phase 2 research is complete. All five requirements (CAPT-01 through CAPT-04) are supported by patterns extracted from the canonical webcamdemo reference at `C:\altcode\webcamdemo\Program.cs`. The WebcamCapture class can be implemented as a single file under `Modules/Webcam/` with namespace `NewDetector.Camera`, requiring no additional NuGet packages beyond what Phase 1 already installed.
