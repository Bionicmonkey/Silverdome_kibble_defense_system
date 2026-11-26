using NewDetector.Config;
using OpenCvSharp;

namespace NewDetector.Camera;

/// <summary>
/// Webcam capture module that opens a DShow camera, grabs frames, selects the best contrast frame,
/// and returns it as JPEG bytes. Handles retry with configurable backoff on open failure.
/// </summary>
public sealed class WebcamCapture : IDisposable
{
    private VideoCapture? _capture = null;
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
    /// Returns true if the camera is currently open and ready for CaptureLive().
    /// </summary>
    public bool IsOpen => _capture != null && _capture.IsOpened();

    /// <summary>
    /// Open the DShow camera and keep it alive across repeated captures.
    /// Uses configured retry/backoff on failure. Returns false if all retries exhaust.
    /// </summary>
    public bool Open()
    {
        for (int attempt = 0; attempt < _retryAttempts; attempt++)
        {
            try
            {
                // Open via DShow backend — mirrors canonical webcamdemo pattern.
                _capture = new VideoCapture(_cameraIndex, VideoCaptureAPIs.DSHOW);

                if (_capture.IsOpened())
                {
                    Console.WriteLine($"[Webcam] Camera opened (index {_cameraIndex}, attempt {attempt + 1})");
                    return true;
                }

                // Failed to open — clean up and retry.
                _capture?.Dispose();
                _capture = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OpenCvSharp exception (attempt {attempt + 1}): {ex.Message}");
            }

            // Backoff before next attempt — no sleep after last attempt.
            if (attempt < _retryAttempts - 1)
            {
                var backoffMs = (int)(_retryBackoffSeconds * 1000 * (attempt + 1));
                Thread.Sleep(backoffMs);
            }
        }

        Console.WriteLine("[Webcam] Failed to open camera after all retries");
        return false; // All retries exhausted.
    }

    /// <summary>
    /// Capture a frame from the already-open camera without closing it.
    /// Returns null if capture fails (camera not open or all frames empty).
    /// </summary>
    public byte[]? CaptureLive()
    {
        if (_capture == null || !_capture.IsOpened())
            return null;

        return RunCapture();
    }

    /// <summary>
    /// Capture a frame: open camera via DShow, grab ~5 frames, select best contrast frame,
    /// save as temp.jpg in CWD, return JPEG bytes.
    /// Returns null if capture fails (camera unavailable or all frames empty).
    /// </summary>
    [Obsolete("Use Open() + CaptureLive() for lifecycle-based usage. CaptureFrame() kept for backward compat.")]
    public byte[]? CaptureFrame()
    {
        for (int attempt = 0; attempt < _retryAttempts; attempt++)
        {
            try
            {
                // Open via DShow backend — mirrors canonical webcamdemo pattern.
                _capture = new VideoCapture(_cameraIndex, VideoCaptureAPIs.DSHOW);

                if (_capture.IsOpened())
                {
                    return RunCapture();
                }

                // Failed to open — clean up and retry.
                _capture?.Dispose();
                _capture = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OpenCvSharp exception (attempt {attempt + 1}): {ex.Message}");
            }

            // Backoff before next attempt — no sleep after last attempt.
            if (attempt < _retryAttempts - 1)
            {
                var backoffMs = (int)(_retryBackoffSeconds * 1000 * (attempt + 1));
                Thread.Sleep(backoffMs);
            }
        }

        return null; // All retries exhausted.
    }

    /// <summary>
    /// Run the capture loop on an already-opened camera: grab ~5 frames, score contrast,
    /// save best frame as temp.jpg. Does NOT dispose _capture — caller manages lifecycle.
    /// </summary>
    private byte[]? RunCapture()
    {
        if (_capture == null || !_capture.IsOpened())
            return null;

        // Set MJPG codec for the stream.
        _capture.Set(VideoCaptureProperties.FourCC, FourCC.MJPG);

        // Probe frame to verify stream is active (handles Pitfall 3: empty initial frames).
        using var probe = new Mat();
        if (!_capture.Read(probe) || probe.Empty())
            return null;

        var bestMat = (Mat?)null;
        double bestScore = -1e9;

        const int attempts = 5;
        for (int i = 0; i < attempts; i++)
        {
            using var frame = new Mat();
            if (!_capture.Read(frame) || frame.Empty())
                continue;

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

        if (bestMat == null)
            return null;
         
        // Save temp.jpg in CWD — resolves path at write time.
        var fullPath = Path.GetFullPath("temp.jpg");   //-- updates temp.jpg 
        if (!Cv2.ImWrite(fullPath, bestMat))
            return null;

        // Read back as byte array for the API contract.
        return File.ReadAllBytes(fullPath);
    }

    /// <summary>
    /// Release unmanaged DShow device handle via explicit GC pattern.
    /// </summary>
    public void Dispose()
    {
        _capture?.Dispose();
        _capture = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Thread.Sleep(200);
    }
}
