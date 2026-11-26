using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using OpenCvSharp;

namespace ollama_batcher;

/// <summary>
/// CLI tool to batch-process JPG images through a local Ollama vision model.
/// </summary>
class Program
{
    private const int ScaleMaxPx = 800;
    private static readonly HttpClient HttpClient = new();

    record FileInfo(
        string FilePath,
        string BaseName,
        string ResultText,
        string RawOutput,
        double Seconds);

    static async Task<int> Main(string[] args)
    {

        if (args.Length == 0 ||
        args.Any(arg => arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("-h", StringComparison.OrdinalIgnoreCase)))
        {
            PrintHelp();
            return 0;
        }

        var parsed = ParseArgs(args);
        if (parsed is null) return 1;

        var tuple = parsed.Value;
        string folderPath = tuple.folder;
        string prompt = tuple.prompt;
        string model = tuple.model;
        var jpgFiles = FindJpgFiles(folderPath).ToArray();
        if (jpgFiles.Length == 0)
        {
            Console.WriteLine($"No JPG files found in: {folderPath}");
            return 1;
        }

        var runNumber = DetermineRunNumber();
        var resultsDir = Path.Combine("C:", "code", "quickvalidator", "results", runNumber.ToString());
        Directory.CreateDirectory(resultsDir);

        Console.WriteLine($"=== Ollama Image Batcher ===");
        Console.WriteLine($"  Folder:   {folderPath}");
        Console.WriteLine($"  Model:    {model}");
        Console.WriteLine($"  Images:   {jpgFiles.Length}");
        Console.WriteLine($"  Results:  {resultsDir}");
        Console.WriteLine();

        var totalStart = Stopwatch.StartNew();
        var processed = new List<FileInfo>();

        for (int i = 0; i < jpgFiles.Length; i++)
        {
            var fullPath = jpgFiles[i];
            var sw = Stopwatch.StartNew();

            string? rawOutput = null;
            try
            {
                rawOutput = await SendImageToOllama(fullPath, prompt, model, resultsDir);
            }
            catch (Exception ex)
            {
                rawOutput = $"ERROR: {ex.Message}";
                Console.WriteLine($"  [{i + 1}/{jpgFiles.Length}] FAILED {Path.GetFileName(fullPath)}: {ex.Message}");
            }

            sw.Stop();
            var seconds = sw.Elapsed.TotalSeconds;

            // Extract text content from the raw response for display and filename.
            var resultText = ExtractContentText(rawOutput);

            // Save full raw output as a text file.
            var safeBasename = SanitizeFileName(BaseNameWithoutExtension(fullPath));
            var safeResult = SanitizeFileName(resultText);
            var safeSeconds = ReplaceDotInDecimal(seconds);
            var outFilename = $"{safeBasename} {safeResult} {safeSeconds}.txt";
            var outPath = Path.Combine(resultsDir, outFilename);

            // Use UTF-8 with CRLF line endings.
            var pretty = string.IsNullOrWhiteSpace(rawOutput)
                ? rawOutput
                : PrettyJson(rawOutput!);
            File.WriteAllText(outPath, pretty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            processed.Add(new FileInfo(fullPath, BaseNameWithoutExtension(fullPath), resultText, rawOutput!, seconds));

            // Progress output.
            var displayResult = resultText.Length > 40
                ? resultText[..40] + "..."
                : resultText;
            Console.WriteLine($"  [{i + 1}/{jpgFiles.Length}] {Path.GetFileName(fullPath)} -> \"{displayResult}\" ({seconds:F1}s)");
        }

        totalStart.Stop();

        // Write consolidated results.txt.
        var resultsTxtLines = new List<string>();
        foreach (var p in processed)
        {
            var display = p.ResultText.Length > 40 ? p.ResultText[..40] + "..." : p.ResultText;
            resultsTxtLines.Add($"{p.BaseName}, \"{display}\", {p.Seconds:F1}");
        }

        double totalSeconds = totalStart.Elapsed.TotalSeconds;
        int imageCount = processed.Count;
        resultsTxtLines.Add("");
        resultsTxtLines.Add($"Total images processed: {imageCount}");
        resultsTxtLines.Add($"Total time: {totalSeconds:F1}s");

        var resultsTxtPath = Path.Combine(resultsDir, "results.txt");
        // Write with CRLF endings.
        File.WriteAllText(resultsTxtPath, string.Join("\r\n", resultsTxtLines), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Console.WriteLine();
        Console.WriteLine($"=== Complete ({imageCount} images in {totalSeconds:F1}s) ===");
        Console.WriteLine($"  Raw outputs: {resultsDir}");
        Console.WriteLine($"  Summary:     {resultsTxtPath}");

        return 0;
    }
    private static void PrintHelp()
    {
        Console.WriteLine("Usage: ollama-batcher --folder <path> --prompt \"text\" --model <name>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --folder   Path to folder containing JPG images (required)");
        Console.WriteLine("  --prompt   Text prompt to send with each image (required)");
        Console.WriteLine("  --model    Ollama model name (default: qwen3.5:4b)");
        Console.WriteLine("  --help     Display this help information");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  dotnet run -- --folder C:\\code\\training\\opossum " +
                          "--prompt \"What animal is in this image?\" --model qwen3.6:35b");
    }

    // ── Args parsing ────────────────────────────────────────────────────

    private static (string folder, string prompt, string model)? ParseArgs(string[] args)
    {
        string? folder = null;
        string? prompt = null;
        string? model = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--folder" when i + 1 < args.Length:
                    folder = args[++i];
                    break;
                case "--prompt" when i + 1 < args.Length:
                    prompt = args[++i];
                    break;
                case "--model" when i + 1 < args.Length:
                    model = args[++i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            Console.WriteLine("Usage: ollama-batcher --folder <path> --prompt \"text\" --model <name>");
            return null;
        }

        if (!Directory.Exists(folder!))
        {
            Console.WriteLine($"Folder not found: {folder}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            Console.WriteLine("--prompt is required.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            model = "qwen3.5:4b";
        }

        return (folder!, prompt!, model!);
    }

    // ── File discovery ─────────────────────────────────────────────────

    private static IEnumerable<string> FindJpgFiles(string folder)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg"
    };

        return Directory
            .EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    // ── Run number auto-increment ───────────────────────────────────────

    private static string DetermineRunNumber()
    {
        var resultsRoot = Path.Combine("C:", "code", "quickvalidator", "results");
        if (!Directory.Exists(resultsRoot)) return "1";

        int maxNum = 0;
        foreach (var d in Directory.EnumerateDirectories(resultsRoot))
        {
            var name = Path.GetFileName(d);
            if (int.TryParse(name, out var n) && n > maxNum)
                maxNum = n;
        }
        return (maxNum + 1).ToString();
    }

    // ── Ollama call (mirrors newdetectorc# OllamaClient exactly) ───────

    /// <summary>Scales a JPEG byte array down to fit within maxPx on the long edge.</summary>
    private static byte[] ScaleImageIfNeeded(byte[] jpeg, int maxPx)
    {
        using var mat = Cv2.ImDecode(jpeg, ImreadModes.Color);
        if (mat.Width <= maxPx && mat.Height <= maxPx)
            return jpeg;
        int targetHeight = (int)Math.Ceiling((double)mat.Height * maxPx / mat.Width);
        using var resized = new Mat();
        Cv2.Resize(mat, resized, new OpenCvSharp.Size(maxPx, Math.Max(targetHeight, 1)));
        Cv2.ImEncode(".jpg", resized, out var result, new int[] { 2, 85 });
        return result.ToArray();
    }

    private static async Task<string?> SendImageToOllama(string imagePath, string prompt, string model, string resultsDir)
    {
        // Single query image scaled to 800px max edge.
        var jpegBytes = File.ReadAllBytes(imagePath);
        var scaled = ScaleImageIfNeeded(jpegBytes, ScaleMaxPx);
        var imageBase64 = Convert.ToBase64String(scaled);

        // System prompt sets the model's role as a porch-animal classifier.
        const string SystemPrompt = "You are an animal classifier that inspects porch camera photos and identifies opossums, cats, or reports none if no animals are present.";

        var payload = new
        {
            model,
            messages = (object)new object[]
            {
                (object)new { role = "system", content = SystemPrompt },
                (object)new
                {
                    role = "user",
                    content = prompt,
                    images = new[] { imageBase64 },
                },
            },
            stream = false,
        };

        var jsonBody = JsonSerializer.Serialize(payload);
        var content = new StringContent(jsonBody, null, "application/json");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(300));
        var uri = "http://127.0.0.1:11434/api/chat";
        var response = await HttpClient.PostAsync(uri, content, cts.Token);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Ollama HTTP error: {response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync(cts.Token);

        // Log raw response to run directory — commented out because the primary need was to capture thinking, which is no longer relevant with a non-thinking model.
        // try
        // {
        //     var rawLogPath = Path.Combine(resultsDir, "raw-response.json");
        //     File.WriteAllText(rawLogPath, PrettyJson(body ?? ""), Encoding.UTF8);
        // }
        // catch { /* never let logging break */ }

        return body;
    }

    // ── Image scaling (mirrors OllamaClient.ScaleImage) ────────────────

    private static byte[] ScaleImage(byte[] jpeg, int maxPx)
    {
        using var mat = Cv2.ImDecode(jpeg, ImreadModes.Color);
        if (mat.Width <= maxPx)
            return jpeg;

        int targetHeight = (int)Math.Ceiling((double)mat.Height * maxPx / mat.Width);
        using var resized = new Mat();
        Cv2.Resize(mat, resized, new OpenCvSharp.Size(maxPx, Math.Max(targetHeight, 1)));
        Cv2.ImEncode(".jpg", resized, out var result, new int[] { 2, 85 });
        return result.ToArray();
    }

    // ── Response parsing (mirrors OllamaClient.ParseResponse) ───────────

    private static string ExtractContentText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // Try top-level "message" format first.
            if (root.TryGetProperty("message", out var directMessage))
            {
                if (directMessage.TryGetProperty("content", out var contentEl)
                    && contentEl.ValueKind == JsonValueKind.String)
                {
                    var text = contentEl.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }

                if (directMessage.TryGetProperty("content", out var contentArray)
                    && contentArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in contentArray.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var textProp)
                            && textProp.ValueKind == JsonValueKind.String)
                        {
                            var t = textProp.GetString();
                            if (!string.IsNullOrWhiteSpace(t)) return t;
                        }
                    }
                }
            }

            // Fallback: "choices" format.
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in choices.EnumerateArray())
                {
                    if (item.TryGetProperty("message", out var msg))
                    {
                        if (msg.TryGetProperty("content", out var ce2)
                            && ce2.ValueKind == JsonValueKind.String)
                        {
                            var t = ce2.GetString();
                            if (!string.IsNullOrWhiteSpace(t)) return t;
                        }

                        if (msg.TryGetProperty("content", out var ca2)
                            && ca2.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var part in ca2.EnumerateArray())
                            {
                                if (part.TryGetProperty("text", out var tp2)
                                    && tp2.ValueKind == JsonValueKind.String)
                                {
                                    var t = tp2.GetString();
                                    if (!string.IsNullOrWhiteSpace(t)) return t;
                                }

                                if (part.TryGetProperty("content", out var nt)
                                    && nt.ValueKind == JsonValueKind.String)
                                {
                                    var t = nt.GetString();
                                    if (!string.IsNullOrWhiteSpace(t)) return t;
                                }
                            }
                        }
                    }
                }
            }

            return "(no content extracted)";
        }
        catch
        {
            return "(parse error)";
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static string BaseNameWithoutExtension(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name)) return "unnamed";
        return name;
    }

    private static string SanitizeFileName(string s)
    {
        // Strip characters that are invalid in filenames, collapse spaces.
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new StringBuilder();
        foreach (var c in s)
        {
            if (!invalid.Contains(c))
                cleaned.Append(char.IsWhiteSpace(c) ? ' ' : c);
        }
        // Collapse multiple spaces to single space and trim.
        return cleaned.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .Aggregate((a, b) => $"{a} {b}").Replace(" ", "-");
    }

    private static string ReplaceDotInDecimal(double value)
    {
        // Convert 3.2 -> "3-2", 10.5 -> "10-5" etc.
        var parts = value.ToString("F1").Split('.');
        return parts.Length == 2 ? $"{parts[0]}-{parts[1]}" : parts[0];
    }

    private static string PrettyJson(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            return JsonSerializer.Serialize(doc.RootElement, options);
        }
        catch
        {
            return raw;
        }
    }
}
