using NewDetector.Config;
using NewDetector.State;
using OpenCvSharp;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace NewDetector.Detection;

/// <summary>
/// Orchestrates the detection pipeline: state checks, diff gate, time gate,
/// Ollama call, and response parsing. Returns DetectResult summarizing findings.
/// </summary>
public static class DetectionEngine
{
    private const int DiffCompareWidth = 160;
    private const int DiffCompareHeight = 120;

    // Rolling consensus cache — last 10 raw detection labels.
    private static readonly ConcurrentQueue<(DateTime ts, string label)> _consensusBuffer = new();
    private static readonly object _consensusLock = new();

    /// <summary>
    /// Number of recent detections to consider when forming a consensus.
    /// Hard-coded at 5 per the user's spec: majority rule (≥3 of N).
    /// </summary>
    private const int ConsensusWindow = 5;

    /// <summary>
    /// Total capacity of the rolling consensus buffer.
    /// Keeps exactly 10 raw detections so the window can be tuned later (e.g. 4-of-7).
    /// </summary>
    private const int ConsensusBufferCapacity = 5;

    /// <summary>
    /// Resolves a consensus label from the last `ConsensusWindow` entries in the buffer.
    ///
    /// Majority rule: if any single value appears ≥ ⌊window/2⌋ + 1 times, it wins.
    /// Tie (no majority): outcome is "none" — the harmless default prevents false-positive
    /// arm actions on ambiguous frames.
    /// </summary>
    private static string ResolveConsensus()
    {
        // Trim buffer to capacity and snapshot the last ConsensusWindow items — all under one lock.
        string[] recent;
        lock (_consensusLock)
        {
            while (_consensusBuffer.Count > ConsensusBufferCapacity)
                _consensusBuffer.TryDequeue(out _);

            int count = _consensusBuffer.Count;
            if (count == 0)
                return "none";

            int take = Math.Min(ConsensusWindow, count);
            recent = new string[take];

            // Queue is FIFO — snapshot without consuming.
            var peek = _consensusBuffer.Select(e => e.label).ToList();
            int start = Math.Max(0, peek.Count - take);
            for (int i = 0; i < take; i++)
                recent[i] = peek[start + i];
        }

        // Count frequencies (outside lock — pure computation).
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in recent)
            counts[v] = counts.TryGetValue(v, out var c) ? c + 1 : 1;

        int threshold = (recent.Length / 2) + 1; // e.g. 5 → 3, 4 → 3
        foreach (var kvp in counts)
        {
            if (kvp.Value >= threshold)
                return kvp.Key;
        }

        return "none"; // No majority → safe default.
    }

    /// <summary>
    /// Normalizes a DetectResult animal list to a single canonical label for the consensus buffer.
    /// - Single animal  → that animal name
    /// - ["none"]       → "none"
    /// - Multiple animals → last one (or "none" if present) to keep history conservative
    /// </summary>
    private static string LabelFromAnimals(IReadOnlyList<string> animals)
    {
        if (animals == null || animals.Count == 0)
            return "none";
        if (animals.Count == 1)
            return animals[0];
        // Multiple detections: if "none" is among them, record "none".
        // Otherwise take the last item (e.g. "opossum" in "cat,opossum") as a conservative marker.
        return animals.Contains("none") ? "none" : animals[animals.Count - 1];
    }

    /// <summary>
    /// Applies daytime rules to a raw detection label before it enters the consensus buffer.
    /// Opossums are nocturnal; detections between 7am and 5pm (local time) are treated as cats
    /// to prevent false-positive arm closures from day-time opossum misidentifications.
    /// </summary>
    private static string ApplyDaytimeRule(string label)
    {
        var hour = DateTime.Now.Hour; // local time — rule is based on wall clock
        if (hour >= 7 && hour < 17)  // 07:00 – 16:59 → daytime
        {
            if (label.Equals("opossum", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[Daytime] Detected opossum during daytime (7am-5pm) — treating as cat");
                return "cat";
            }
        }
        return label; // night hours — no override, let detection stand
    }

    /// <summary>
    /// Analyzes a single frame through the detection pipeline.
    /// </summary>
    /// <param name="frameBytes">JPEG-encoded frame from webcam capture.</param>
    /// <param name="speciesWhitelist">Allowed species names for filtering.</param>
    /// <param name="detectionConfig">Detection settings (thresholds, etc.).</param>
    /// <param name="ollamaConfig">Ollama connection and model configuration.</param>
    /// <param name="promptTemplate">System prompt sent to the model.</param>
    /// <returns>DetectResult with animals list and skip status.</returns>
    public static DetectResult Analyze(
        byte[] frameBytes,
        string[] speciesWhitelist,
        DetectionConfig detectionConfig,
        OllamaConfig ollamaConfig,
        string promptTemplate)
    {
        // Step 1: Read last detection state
        var lastState = StateManager.GetDetectionState();

        // Step 2: TIME GATE — force Ollama every 30s regardless of diff.
        var now = DateTime.UtcNow;
        var lastOllamaCall = StateManager.GetLastOllamaCallUtc();
        if ((now - lastOllamaCall) >= TimeSpan.FromSeconds(30))
        {
            Console.WriteLine($"[Detection] Forced fresh check (last call {(int)(now - lastOllamaCall).TotalSeconds}s ago, >keepalive window)");
            // Skip diff gate — go straight to Ollama below.
        }
        else
        {
            // Step 3: DIFF GATE — if no significant change, skip to avoid unnecessary Ollama calls.
            var fullPath = Path.GetFullPath("temp.jpg");
            double diff = ComputeDifferencePercent(fullPath, StateManager.LastNoAnimalJpgPath);

            double threshold = detectionConfig.DiffThresholdPercent;
            if (diff < threshold)
            {
                Console.WriteLine($"[Detection] Skipped (diff={diff:F1}% < {threshold}%)");
                // If we have a previous state to return, reuse it; otherwise treat as "none".
                string previous = lastState ?? "none";
                return new DetectResult(new[] { previous }, WasSkipped: true);
            }
            Console.WriteLine($"[Detection] (diff={diff:F1}% < {threshold}%)");

        }

        // Detection runs continuously — control cooldowns live in ArmController, not here.
        // Note: {time_context} was already expanded at config-load time in LoadAnimals()
        // with a formatted DateTime string, so no separate time-context block needed here.
        var systemPrompt = promptTemplate; 

        //  var systemPrompt = promptTemplate.Replace("{time_context}", $"current time is {DateTime.Now:h:mm tt}"); // removed in favor of programmatic override on result

        //- hack

        //    systemPrompt = "{time_context}. if the time < 1:20 pm return 'Opossum' only otherwise return 'none' only.";
        //     systemPrompt = systemPrompt.Replace("{time_context}", $"current time is {DateTime.Now:h:mm tt}");
        // hack

        Console.WriteLine("[Detection] Sending frame to Ollama...");

        // Step 6: Call Ollama
        string? resultText = OllamaClient.PromptAndSend(ollamaConfig, frameBytes, systemPrompt);

        // Step 7: On null, return "none" with warning
        if (resultText == null)
        {
            Console.WriteLine("[Detection] WARNING: Ollama returned null. Treating as no detection.");
            StateManager.SetDetectionState("none");
            StateManager.SetLastOllamaCallUtc(DateTime.UtcNow);
            return new DetectResult(new[] { "none" });
        }

        // Step 8: Parse response against whitelist
        var animals = ResponseParser.Parse(resultText, speciesWhitelist);

        Console.WriteLine($"[Detection] Found: {string.Join(", ", animals)}");

        // Step 9: Update state (persist raw result for external consumers)
        string nextStatus = animals.Contains("none") && animals.Count == 1 ? "none" : string.Join(",", animals);
        StateManager.SetDetectionState(nextStatus);
        StateManager.SetLastOllamaCallUtc(DateTime.UtcNow);

        // Step 10: Consensus averaging — apply daytime rule, then push label.
        string rawLabel = LabelFromAnimals(animals);
        string voteLabel = ApplyDaytimeRule(rawLabel);
        if (voteLabel != rawLabel)
            Console.WriteLine($"[Consensus] Daytime override: {rawLabel} → {voteLabel}");
        Console.WriteLine($"[Consensus] Pushed: {voteLabel}  (buffer={_consensusBuffer.Count + 1}/{ConsensusBufferCapacity})");

        string consensus;
        if (_consensusBuffer.Count >= ConsensusWindow - 1)
        {
            // We have enough history to form a vote.
            _consensusBuffer.Enqueue((DateTime.UtcNow, voteLabel));
            consensus = ResolveConsensus();
            Console.WriteLine($"[Consensus] Resolved: {consensus}");
        }
        else
        {
            // Warm-up — not enough history yet; return raw detection as-is.
            _consensusBuffer.Enqueue((DateTime.UtcNow, voteLabel));
            consensus = voteLabel;
            Console.WriteLine($"[Consensus] Warm-up ({_consensusBuffer.Count}/{ConsensusWindow - 1}), returning raw: {consensus}");
        }

        return new DetectResult(new List<string> { consensus });
    }

    static Bitmap ResizeBitmap(Bitmap source, int targetWidth, int targetHeight)
    {
        var resized = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, targetWidth, targetHeight);
        }

        return resized;
    }
    static double ComputeDifferencePercent(string previousImagePath, string currentImagePath)
    {
        try
        {
            using var previousBitmap = new Bitmap(previousImagePath);
            using var currentBitmap = new Bitmap(currentImagePath);

            int width = Math.Min(previousBitmap.Width, currentBitmap.Width);
            int height = Math.Min(previousBitmap.Height, currentBitmap.Height);

            if (width <= 0 || height <= 0)
            {
                return 100.0;
            }

            // Resize to a common, low-cost working size to avoid expensive per-pixel comparisons.
            // This is intentionally approximate; you can tune the target size.
            int targetWidth = 160;
            int targetHeight = 120;

            using var prevScaled = ResizeBitmap(previousBitmap, targetWidth, targetHeight);
            using var currScaled = ResizeBitmap(currentBitmap, targetWidth, targetHeight);

            long differentPixels = 0;
            long totalPixels = targetWidth * targetHeight;

            for (int y = 0; y < targetHeight; y++)
            {
                for (int x = 0; x < targetWidth; x++)
                {
                    Color a = prevScaled.GetPixel(x, y);
                    Color b = currScaled.GetPixel(x, y);

                    int deltaR = Math.Abs(a.R - b.R);
                    int deltaG = Math.Abs(a.G - b.G);
                    int deltaB = Math.Abs(a.B - b.B);

                    // threshold: only count as different if the color change is large enough
                    // to matter for a scene being "essentially the same"
                    int totalDelta = deltaR + deltaG + deltaB;

                    if (totalDelta > 120)
                    {
                        differentPixels++;
                    }
                }
            }

            return (differentPixels / (double)totalPixels) * 100.0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Image comparison failed: {ex.Message}");
            return 100.0; // safest default: do not skip Ollama
        }
    }

    /// <summary>
    /// Computes the average RGB difference percentage between a frame and the baseline image.
    /// Returns a value in [0, 100] where higher means more change.
    /// </summary>
    private static double ComputeDiff(byte[] frameBytes)
    {
        var baselinePath = StateManager.LastNoAnimalJpgPath;

        // If no baseline exists yet, treat diff as 100% (always trigger on first frame)
        if (!File.Exists(baselinePath))
            return 100.0;

        byte[] baselineBytes;
        try
        {
            baselineBytes = File.ReadAllBytes(baselinePath);
        }
        catch (IOException)
        {
            // Can't read baseline — always trigger detection
            return 100.0;
        }

        // Resize both to small fixed dims for fast comparison
        using var origNew = Cv2.ImDecode(frameBytes, ImreadModes.Color);
        using var newSmall = ResizeTo(origNew, DiffCompareWidth, DiffCompareHeight);

        using var origBase = Cv2.ImDecode(baselineBytes, ImreadModes.Color);
        using var baseSmall = ResizeTo(origBase, DiffCompareWidth, DiffCompareHeight);

        // Compute per-pixel absolute difference using At<Vec3b> (safe API).
        long totalDiff = 0;
        int pixelCount = newSmall.Width * newSmall.Height;

        for (int y = 0; y < newSmall.Height; y++)
        {
            for (int x = 0; x < newSmall.Width; x++)
            {
                var newPixel = newSmall.At<Vec3b>(y, x);
                var basePixel = baseSmall.At<Vec3b>(y, x);
                // Vec3b: Item0=B, Item1=G, Item2=R
                totalDiff += Math.Abs(newPixel.Item0 - basePixel.Item0);
                totalDiff += Math.Abs(newPixel.Item1 - basePixel.Item1);
                totalDiff += Math.Abs(newPixel.Item2 - basePixel.Item2);
            }
        }

        double avgDiff = (double)totalDiff / (pixelCount * 3) * 100.0;
        return Math.Min(avgDiff, 100.0);
    }

    /// <summary>
    /// Resizes a Mat to fixed dimensions, returning ownership via new Mat.
    /// Caller must dispose the returned Mat. Returns input unchanged if already correct size.
    /// </summary>
    private static Mat ResizeTo(Mat src, int width, int height)
    {
        if (src.Width == width && src.Height == height)
            return src;

        var dst = new Mat();
        Cv2.Resize(src, dst, new OpenCvSharp.Size(width, height));
        return dst;
    }
}
