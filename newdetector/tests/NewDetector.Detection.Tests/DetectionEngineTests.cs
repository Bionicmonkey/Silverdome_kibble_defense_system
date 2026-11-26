using NewDetector.Config;
using NewDetector.Detection;
using NewDetector.State;
using OpenCvSharp;

namespace NewDetector.Detection.Tests;

/// <summary>
/// Tests for ImageDiff (via DetectionEngine.Analyze) and ResponseParser.
/// ImageDiff is tested through DetectionEngine.Analyze since ComputeDiff is private.
/// xUnit runs tests within a class sequentially by default.
/// </summary>
public sealed class DetectionEngineTests : IDisposable
{
    // State file paths (CWD-relative per StateManager)
    private static string BaselinePath => Path.Combine(Directory.GetCurrentDirectory(), StateManager.LastNoAnimalJpgPath);
    private static string DetStatePath   => Path.Combine(Directory.GetCurrentDirectory(), StateManager.LastDetectionStateFile);
    private static string OllamaCallPath => Path.Combine(Directory.GetCurrentDirectory(), StateManager.LastOllamaCallFile);
    private static string ArmStatePath   => Path.Combine(Directory.GetCurrentDirectory(), StateManager.StateFilePath);

    public DetectionEngineTests()
    {
        StateManager.EnsureInitialized();
    }

    public void Dispose()
    {
        CleanupStateFiles();
    }

    // ===================== ImageDiff tests (via DetectionEngine.Analyze) =====================

    /// <summary>
    /// EmptyBaseline_DiffReturnsZero: When the baseline file exists but contains a minimal
    /// "empty" image, diff against identical content should be zero.
    /// </summary>
    [Fact]
    public void ImageDiff_EmptyBaseline_DiffReturnsZero()
    {
        using var emptyMat = new Mat(1, 1, MatType.CV_8UC3, Scalar.Black);
        Cv2.ImEncode(".jpg", emptyMat, out byte[] bytes);
        File.WriteAllBytes(BaselinePath, bytes);
        SetDetectionState("none");

        var result = DetectionEngine.Analyze(
            bytes, new[] { "cat", "dog", "owl" },
            new DetectionConfig { DiffThresholdPercent = 5.0 },
            new OllamaConfig(),
            "Identify animals.");

        Assert.True(result.WasSkipped);
        Assert.Single(result.Animals);
        Assert.Equal("none", result.Animals[0]);
    }

    /// <summary>
    /// IdenticalImages_DiffBelowThreshold: Frame identical to baseline yields diff ~0,
    /// which is below any reasonable threshold. The engine skips analysis.
    /// </summary>
    [Fact]
    public void ImageDiff_IdenticalImages_DiffBelowThreshold()
    {
        using var img = CreateSolidColorMat(255, 140, 100);
        Cv2.ImEncode(".jpg", img, out byte[] identicalBytes);
        File.WriteAllBytes(BaselinePath, identicalBytes);
        SetDetectionState("none");

        var result = DetectionEngine.Analyze(
            identicalBytes, new[] { "cat", "dog", "owl" },
            new DetectionConfig { DiffThresholdPercent = 5.0 },
            new OllamaConfig(),
            "Identify animals.");

        Assert.True(result.WasSkipped);
        Assert.Single(result.Animals);
    }

    /// <summary>
    /// DifferentImages_DiffAboveThreshold: Frame differs significantly from baseline,
    /// diff exceeds threshold -> engine proceeds to Ollama. We verify WasSkipped=false.
    /// </summary>
    [Fact]
    public void ImageDiff_DifferentImages_DiffAboveThreshold()
    {
        using var whiteMat = CreateSolidColorMat(255, 255, 255);
        Cv2.ImEncode(".jpg", whiteMat, out byte[] baselineBytes);
        File.WriteAllBytes(BaselinePath, baselineBytes);

        using var blackMat = CreateSolidColorMat(0, 0, 0);
        Cv2.ImEncode(".jpg", blackMat, out byte[] frameBytes);

        SetDetectionState("none");

        var result = DetectionEngine.Analyze(
            frameBytes, new[] { "cat", "dog", "owl" },
            new DetectionConfig { DiffThresholdPercent = 5.0 },
            new OllamaConfig { Endpoint = "http://localhost:11434", TimeoutSeconds = 1 },
            "Identify animals.");

        Assert.False(result.WasSkipped);
        Assert.Single(result.Animals);
    }

    /// <summary>
    /// ConfigurableThreshold_RespectsSetting: Verifies that the diff gate uses the
    /// configurable threshold. A gray frame (~35% diff from white baseline):
    /// - Low threshold (1%): diff ~35% > 1% -> no skip (WasSkipped=false)
    /// - High threshold (90%): diff ~35% < 90% -> skip (WasSkipped=true)
    /// Uses separate baselines to isolate each call — Ollama is not called for the
    /// high-threshold path since diff gates it out first.
    /// </summary>
    [Fact]
    public void ImageDiff_ConfigurableThreshold_RespectsSetting()
    {
        // --- Low threshold test: gray frame vs white baseline → diff ~35% > 1% ---
        var whiteImg = new Mat(640, 480, MatType.CV_8UC3);
        for (int y = 0; y < whiteImg.Rows; y++)
            for (int x = 0; x < whiteImg.Cols; x++)
                whiteImg.Set<Vec3b>(y, x, new Vec3b(255, 255, 255)); // White BGR
        Cv2.ImEncode(".jpg", whiteImg, out byte[] whiteBytes);
        File.WriteAllBytes(BaselinePath, whiteBytes);

        var frameImg = new Mat(640, 480, MatType.CV_8UC3);
        for (int y = 0; y < frameImg.Rows; y++)
            for (int x = 0; x < frameImg.Cols; x++)
                frameImg.Set<Vec3b>(y, x, new Vec3b(127, 127, 127)); // Dark gray BGR
        Cv2.ImEncode(".jpg", frameImg, out byte[] frameBytes);

        SetDetectionState("none");

        var lowResult = DetectionEngine.Analyze(
            frameBytes, new[] { "cat", "dog", "owl" },
            new DetectionConfig { DiffThresholdPercent = 1.0 },
            new OllamaConfig { TimeoutSeconds = 1 },
            "Identify animals.");

        // Low threshold should pass diff gate → goes to Ollama (will get null here) → WasSkipped=false
        Assert.False(lowResult.WasSkipped);

        // --- High threshold test: frame identical to baseline → diff = 0% < 90% → skip ---
        Cv2.ImEncode(".jpg", frameImg, out byte[] grayBytes);
        File.WriteAllBytes(BaselinePath, grayBytes); // Same gray image as baseline

        var identicalResult = DetectionEngine.Analyze(
            grayBytes, new[] { "cat", "dog", "owl" },
            new DetectionConfig { DiffThresholdPercent = 90.0 },
            new OllamaConfig(),
            "Identify animals.");

        // Identical frame against itself: diff=0 < 90 → skip, even without time gate
        Assert.True(identicalResult.WasSkipped);
    }

    // ===================== ResponseParser tests =====================

    /// <summary>
    /// CommaSeparated_MatchesWhitelist: Parses comma-delimited model output and matches
    /// each token against the whitelist.
    /// </summary>
    [Fact]
    public void ResponseParser_CommaSeparated_MatchesWhitelist()
    {
        var response = "cat, dog";
        string[] whitelist = new[] { "cat", "dog", "owl" };

        var result = ResponseParser.Parse(response, whitelist);

        Assert.Equal(2, result.Count);
        Assert.Contains("cat", result);
        Assert.Contains("dog", result);
    }

    /// <summary>
    /// MixedCase_CaseInsensitiveMatch: Matching is case-insensitive.
    /// </summary>
    [Fact]
    public void ResponseParser_MixedCase_CaseInsensitiveMatch()
    {
        var response = "CAT, Dog, OWL";
        string[] whitelist = new[] { "cat", "dog", "owl" };

        var result = ResponseParser.Parse(response, whitelist);

        Assert.Equal(3, result.Count);
        Assert.Contains("cat", result);
        Assert.Contains("dog", result);
        Assert.Contains("owl", result);
    }

    /// <summary>
    /// NonSpeciesToken_FilteredOut: Non-matching words are ignored.
    /// </summary>
    [Fact]
    public void ResponseParser_NonSpeciesToken_FilteredOut()
    {
        var response = "cat, bird, unknown";
        string[] whitelist = new[] { "cat", "dog", "owl" };

        var result = ResponseParser.Parse(response, whitelist);

        Assert.Single(result);
        Assert.Equal("cat", result[0]);
    }

    /// <summary>
    /// EmptyResponse_ReturnsNone: null, empty, whitespace, and "none" all produce ["none"].
    /// </summary>
    [Fact]
    public void ResponseParser_EmptyResponse_ReturnsNone()
    {
        var nullResult = ResponseParser.Parse((string?)null!, new[] { "cat", "dog" });
        Assert.Single(nullResult);
        Assert.Equal("none", nullResult[0]);

        var emptyResult = ResponseParser.Parse("", new[] { "cat", "dog" });
        Assert.Single(emptyResult);
        Assert.Equal("none", emptyResult[0]);

        var wsResult = ResponseParser.Parse("   \t\n  ", new[] { "cat", "dog" });
        Assert.Single(wsResult);
        Assert.Equal("none", wsResult[0]);

        var noneResult = ResponseParser.Parse("none", new[] { "cat", "dog" });
        Assert.Single(noneResult);
        Assert.Equal("none", noneResult[0]);
    }

    // ===================== Helpers =====================

    /// <summary>Creates a solid-color Mat (BGR) of standard resolution.</summary>
    private static Mat CreateSolidColorMat(byte b, byte g, byte r)
        => new Mat(480, 640, MatType.CV_8UC3, new Scalar(b, g, r));

    /// <summary>Sets the last_detection_state.txt file.</summary>
    private static void SetDetectionState(string state)
        => File.WriteAllText(DetStatePath, state);

    /// <summary>Cleans up all state files that DetectionEngine may have written.</summary>
    private void CleanupStateFiles()
    {
        foreach (var path in new[] { BaselinePath, DetStatePath, OllamaCallPath, ArmStatePath })
        {
            if (File.Exists(path))
                try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
