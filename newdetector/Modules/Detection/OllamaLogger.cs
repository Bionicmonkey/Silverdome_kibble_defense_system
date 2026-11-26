using System.Text;

namespace NewDetector.Detection;

/// <summary>
/// Logs raw Ollama HTTP responses to one file per call, with date+time in the filename.
/// Each response is written as pretty-printed JSON (including reasoning) with CRLF endings.
/// </summary>
public static class OllamaLogger
{
    private static readonly string _logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "logs", "ollama");

    /// <summary>
    /// Writes a raw Ollama response body to a timestamped log file.
    /// Silently swallows any I/O errors so logging never breaks detection.
    /// </summary>
    /// <param name="rawBody">The complete HTTP response body from Ollama.</param>
    public static void LogRawResponse(string rawBody)
    {

        return; 

        //-- preserving in case needed, but not needed at the moment

        if (string.IsNullOrWhiteSpace(rawBody))
            return;

        try
        {
            // Ensure log directory exists.
            if (!Directory.Exists(_logDir))
                Directory.CreateDirectory(_logDir);

            // Unique filename: YYYYMMDD-HHmmss-fff.log
            var now = DateTime.Now;
            var ts = now.ToString("yyyyMMdd-HHmmss") + "-" + now.Millisecond.ToString("D3");
            var filePath = Path.Combine(_logDir, $"ollama-{ts}.log");

            // Pretty-print the JSON for readability.
            var pretty = PrettyJson(rawBody);

            // Write with CRLF endings — use explicit newline handling.
            var lines = new[] { pretty };
            var text = string.Join("\r\n", lines) + "\r\n";

            File.AppendAllText(filePath, text, Encoding.UTF8);
        }
        catch
        {
            // Never let logging break detection.
        }
    }

    /// <summary>
    /// Pretty-prints a JSON string by deserializing and re-serializing with indentation.
    /// Returns the original if it fails (never block).
    /// </summary>
    private static string PrettyJson(string raw)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            return System.Text.Json.JsonSerializer.Serialize(doc.RootElement, options);
        }
        catch
        {
            // Not valid JSON — return as-is.
            return raw;
        }
    }
}
