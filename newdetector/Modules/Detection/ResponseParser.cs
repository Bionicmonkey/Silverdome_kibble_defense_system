using System.Text.RegularExpressions;

namespace NewDetector.Detection;

/// <summary>
/// Parses the raw text response from Ollama into a list of detected animal species,
/// filtering against the provided whitelist.
/// </summary>
public static class ResponseParser
{
    /// <summary>
    /// Parses model output into species names that appear in the whitelist.
    /// Handles formats like "cat, dog", one-per-line, or a single word.
    /// Returns ["none"] if nothing matches.
    /// </summary>
    public static IReadOnlyList<string> Parse(string responseText, string[] whitelist)
    {
        var trimmed = responseText?.Trim() ?? "";
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
            return new[] { "none" };

        // Split by common delimiters: commas, newlines, semicolons.
        var tokens = Regex.Split(trimmed, @"[,\n\r;]+")
            .Select(t => t.Trim())
            .Where(t => t.Length > 0);

        var results = new List<string>();
        foreach (var token in tokens)
        {
            // Skip non-species words
            if (token.Equals("none", StringComparison.OrdinalIgnoreCase))
                continue;

            // Check whitelist containment — case-insensitive partial match.
            foreach (var species in whitelist)
            {
                var normalized = species.Trim().ToLowerInvariant();
                var tokenLower = token.ToLowerInvariant();
                if (tokenLower.Contains(normalized))
                {
                    // Avoid duplicates
                    if (!results.Contains(species, StringComparer.OrdinalIgnoreCase))
                        results.Add(species);
                }
            }
        }

        return results.Count > 0 ? results : new[] { "none" };
    }
}
