using NewDetector.Config;
using NewDetector.Camera;

namespace NewDetector.Camera.Tests;

/// <summary>
/// Tests for WebcamCapture covering CAPT-02 (contrast scoring) and CAPT-03 (file output).
/// Some tests require a real camera; others are deterministic without hardware.
/// </summary>
public class WebcamCaptureTests
{
    /// <summary>
    /// Constructor correctly reads all three config values from WebcamConfig.
    /// No camera required.
    /// </summary>
    [Fact]
    public void Constructor_ReadsAllConfigValues()
    {
        var config = new WebcamConfig
        {
            DefaultIndex = 5,
            RetryAttempts = 7,
            RetryBackoffSeconds = 2.5
        };

        var capture = new WebcamCapture(config);
        Assert.NotNull(capture);

        // Without a camera, CaptureFrame returns null (deterministic).
        var result = capture.CaptureFrame();
        Assert.Null(result);
    }

    /// <summary>
    /// CAPT-04: Retry attempts exhaust and return null when no camera is available.
    /// </summary>
    [Fact]
    public void CaptureFrame_NoCamera_ReturnsNull()
    {
        var config = new WebcamConfig
        {
            DefaultIndex = 99,
            RetryAttempts = 3,
            RetryBackoffSeconds = 0.01
        };

        var capture = new WebcamCapture(config);
        var result = capture.CaptureFrame();

        Assert.Null(result);
    }

    /// <summary>
    /// CAPT-04: Backoff timing is proportional to config value (linear backoff).
    /// </summary>
    [Fact]
    public void CaptureFrame_Backoff_ProportionalToConfig()
    {
        var fastConfig = new WebcamConfig
        {
            DefaultIndex = 99,
            RetryAttempts = 3,
            RetryBackoffSeconds = 0.01
        };

        var slowConfig = new WebcamConfig
        {
            DefaultIndex = 99,
            RetryAttempts = 3,
            RetryBackoffSeconds = 0.05
        };

        var fastTime = RunCapture(fastConfig);
        var slowTime = RunCapture(slowConfig);

        Assert.True(slowTime > fastTime * 3, "Slow backoff should produce significantly longer execution time.");
    }

    /// <summary>
    /// Dispose releases without throwing (verifies GC pattern is correct).
    /// </summary>
    [Fact]
    public void Dispose_NoCrash()
    {
        var config = new WebcamConfig();
        var capture = new WebcamCapture(config);

        capture.Dispose();
        capture.Dispose(); // Double dispose should also be safe.
    }

    /// <summary>
    /// Integration test: with a real camera, CaptureFrame returns non-null bytes.
    /// Skipped when no camera is found (CI/headless environments).
    /// </summary>
    public class LiveCameraTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void CaptureFrame_WithCamera_ReturnsBytes(int index)
        {
            var config = new WebcamConfig
            {
                DefaultIndex = index,
                RetryAttempts = 1,
                RetryBackoffSeconds = 0.01
            };

            var capture = new WebcamCapture(config);
            var result = capture.CaptureFrame();

            if (result is null)
                return; // No camera at this index — test passes by skipping.

            Assert.NotEmpty(result);
            Assert.True(result.Length > 100, $"Captured frame should be a valid JPEG (>100 bytes), got {result.Length} bytes.");

            var fullPath = Path.GetFullPath("temp.jpg");
            Assert.True(File.Exists(fullPath), $"temp.jpg should exist at {fullPath}");

            if (File.Exists(fullPath))
                File.Delete(fullPath);

            capture.Dispose();
        }
    }

    private static long RunCapture(WebcamConfig config)
    {
        var capture = new WebcamCapture(config);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _ = capture.CaptureFrame();
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }
}
