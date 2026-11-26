using System.Net.Http.Headers;
using System.Text.Json;
using NewDetector.Config;
using OpenCvSharp;

namespace NewDetector.Detection;

/// <summary>
/// Thin wrapper around the Ollama REST API that sends a single frame with a prompt
/// and returns the model's text response. Returns null on any failure.
/// </summary>
public static class OllamaClient
{
    private static readonly HttpClient _httpClient = new();

    /// <summary>
    /// Sends a frame to the local Ollama instance with a system prompt template,
    /// scales the image for transport, and returns the parsed text response.
    /// Returns null when the request fails or the model is unavailable.
    /// </summary>
    /// <param name="config">Ollama configuration from the app config file.</param>
    /// <param name="frameBytes">JPEG-encoded frame bytes from the webcam capture.</param>
    /// <param name="promptTemplate">System prompt template, e.g. "Identify all insects in this image. Return species names only or 'none'."</param>
    public static string? PromptAndSend(
        OllamaConfig config,
        byte[] frameBytes,
        string promptTemplate)
    {
        try
        {
            // Scale image for transport (Ollama does not need full-res).
            var scaled = ScaleImage(frameBytes, maxPx: 800);

            // Build the request payload — match the working detector.exe format exactly.
            var imageBase64 = Convert.ToBase64String(scaled);

            var payload = new
            {
                model = config.Model,
                messages = (object[])new object[]
                {
                    new
                    {
                        role = "user",
                        content = promptTemplate,
                        images = new[] { imageBase64 },
                    },
                },
                stream = false,
            };

            var jsonBody = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonBody, null, "application/json");

            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(config.TimeoutSeconds));
            var response = _httpClient.Send(
                new HttpRequestMessage(HttpMethod.Post, config.Endpoint + config.ApiPath)
                {
                    Content = content,
                },
                HttpCompletionOption.ResponseContentRead,
                cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                // HTTP failure — caller expects null.
                return null;
            }

            var body = response.Content.ReadAsStringAsync().Result;

            // Log raw Ollama response (including reasoning) to timestamped file.
            OllamaLogger.LogRawResponse(body);

            return ParseResponse(body);
        }
        catch (OperationCanceledException)
        {
            // Timed out.
            return null;
        }
        catch (HttpRequestException)
        {
            // Network unreachable, connection refused, etc.
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not HttpRequestException)
        {
            // Catch-all: model-not-loaded exceptions, corrupted image data,
            // serialization errors, etc.
            return null;
        }
    }

    /// <summary>
    /// Parses the Ollama /api/chat response to extract the content string.
    /// Returns null when the expected structure is absent or empty.
    /// Handles both string content and inline array content (for vision model responses).
    /// </summary>
    private static string? ParseResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        // Try top-level "message" format first (Ollama /api/chat non-streaming
        // often returns {model, message: {...}, done} rather than wrapping in choices).
        if (root.TryGetProperty("message", out var directMessage))
        {
            if (directMessage.TryGetProperty("content", out var contentEl)
                && contentEl.ValueKind == JsonValueKind.String)
            {
                var text = contentEl.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            // Handle inline array content from vision models.
            if (directMessage.TryGetProperty("content", out var contentArray)
                && contentArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in contentArray.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textProp)
                        && textProp.ValueKind == JsonValueKind.String)
                    {
                        var text = textProp.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                            return text;
                    }
                }
            }
        }

        // Fallback: try "choices" format (some Ollama versions or endpoint variants).
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            return null;

        var choicesList = new List<JsonElement>();
        foreach (var item in choices.EnumerateArray())
        {
            choicesList.Add(item.Clone());
        }

        if (choicesList.Count == 0)
            return null;

        // JsonElement doesn't implement IDisposable — no using here.
        var choice = choicesList[0];
        if (!choice.TryGetProperty("message", out var message))
            return null;

        // Handle content as a plain string (text-only response).
        if (message.TryGetProperty("content", out var contentElement)
            && contentElement.ValueKind == JsonValueKind.String)
        {
            var text = contentElement.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        // Handle content as an array of inline objects (vision model response).
        if (message.TryGetProperty("content", out var contentArray2)
            && contentArray2.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in contentArray2.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textProp2)
                    && textProp2.ValueKind == JsonValueKind.String)
                {
                    var text = textProp2.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }

                if (part.TryGetProperty("content", out var nestedText)
                    && nestedText.ValueKind == JsonValueKind.String)
                {
                    var text = nestedText.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Scales a JPEG byte array so its width does not exceed maxPx pixels,
    /// preserving the original aspect ratio. Returns the resized JPEG bytes.
    /// </summary>
    private static byte[] ScaleImage(byte[] jpeg, int maxPx)
    {
        // Decode — ImDecode returns a new Mat in this OpenCvSharp version.
        using var mat = Cv2.ImDecode(jpeg, ImreadModes.Color);

        // Already fits — return original bytes (no round-trip cost).
        if (mat.Width <= maxPx)
            return jpeg;

        int targetHeight = (int)Math.Ceiling(
            (double)mat.Height * maxPx / mat.Width);

        using var resized = new Mat();
        Cv2.Resize(mat, resized, new OpenCvSharp.Size(maxPx, Math.Max(targetHeight, 1)));

        Cv2.ImEncode(".jpg", resized, out var result, new[] { 1, 85 });
        return result.ToArray();
    }
}
