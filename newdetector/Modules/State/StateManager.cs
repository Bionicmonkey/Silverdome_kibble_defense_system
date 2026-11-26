namespace NewDetector.State;

/// <summary>
/// Centralized handler for all shared state file I/O.
/// All paths are CWD-relative per RUNT-03 — operates on the current working directory.
/// State files are shared with MonitorAppweb (Python companion app).
/// </summary>
public static class StateManager
{
    // --- State file path constants (CWD-relative) ---

    public const string StateFilePath = "state.txt";
    public const string LastNoAnimalJpgPath = "last_no_animal.jpg";
    public const string LastDetectionStateFile = "last_detection_state.txt";
    public const string LastOllamaCallFile = "last_ollama_call.txt";

    // --- Events directory ---

    public const string EventsDirName = "events";

    /// <summary>
    /// Ensures critical state infrastructure is initialized.
    /// Creates state.txt with default arm position if missing.
    /// Creates events/ directory under CWD.
    /// Does NOT create image files or detection files eagerly.
    /// </summary>
    public static void EnsureInitialized()
    {
        // Create events directory (idempotent — Directory.CreateDirectory handles existing)
        var eventsDir = Path.Combine(Directory.GetCurrentDirectory(), EventsDirName);
        try
        {
            if (!Directory.Exists(eventsDir))
            {
                Directory.CreateDirectory(eventsDir);
                Console.WriteLine($"Created events directory: {eventsDir}");
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Could not create events directory at '{eventsDir}': {ex.Message}");
        }

        // Create state.txt with default if missing
        var statePath = Path.Combine(Directory.GetCurrentDirectory(), StateFilePath);
        try
        {
            if (!File.Exists(statePath))
            {
                File.WriteAllText(statePath, "closed");
                Console.WriteLine($"Created {StateFilePath} with default 'closed' (closed position)");
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Could not write {StateFilePath} at '{statePath}': {ex.Message}");
        }

        // Create detection state file if missing
        var detPath = Path.Combine(Directory.GetCurrentDirectory(), LastDetectionStateFile);
        try
        {
            if (!File.Exists(detPath))
            {
                File.WriteAllText(detPath, "none");
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Could not write {LastDetectionStateFile}: {ex.Message}");
        }

        // Create last_ollama_call.txt if missing
        var ollamaPath = Path.Combine(Directory.GetCurrentDirectory(), LastOllamaCallFile);
        try
        {
            if (!File.Exists(ollamaPath))
            {
                File.WriteAllText(ollamaPath, DateTime.MinValue.ToString("o"));
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Could not write {LastOllamaCallFile}: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the current arm state from state.txt.
    /// Returns "closed" if file is missing.
    /// </summary>
    public static string GetArmState()
    {
        var statePath = Path.Combine(Directory.GetCurrentDirectory(), StateFilePath);

        if (!File.Exists(statePath))
            return "open"; // default arm position

        try
        {
            var content = File.ReadAllText(statePath).Trim();
            // Plain text format (new — written by SetArmState, MonitorAppweb)
            if (content.Equals("open", StringComparison.OrdinalIgnoreCase))
                return "open";
            if (content.Equals("closed", StringComparison.OrdinalIgnoreCase))
                return "closed";
            // Legacy arm=X format for backward compatibility with existing state.txt files
            if (content.Equals("arm=1", StringComparison.OrdinalIgnoreCase) ||
                content.Equals("arm=true", StringComparison.OrdinalIgnoreCase))
                return "open";
            if (content.Equals("arm=0", StringComparison.OrdinalIgnoreCase) ||
                content.Equals("arm=false", StringComparison.OrdinalIgnoreCase))
                return "closed";
            // Numeric/boolean fallback for other legacy writers (MonitorAppweb, etc.)
            if (content.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                content.Equals("true", StringComparison.OrdinalIgnoreCase))
                return "open";
            // Default: anything else is closed
            return "closed";
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Error reading arm state: {ex.Message}. Returning default 'closed'.");
            return "closed";
        }
    }

    /// <summary>
    /// Sets the arm state by writing to state.txt.
    /// Creates file with default if it does not exist.
    /// </summary>
    public static void SetArmState(string state)
    {
        // Write plain text compatible with MonitorAppweb (reads raw string, compares against "open"/"closed")
        var value = state?.Equals("open", StringComparison.OrdinalIgnoreCase) == true ? "open" : "closed";
        var statePath = Path.Combine(Directory.GetCurrentDirectory(), StateFilePath);

        try
        {
            // Single-line write is atomic on Windows NTFS for small files
            File.WriteAllText(statePath, value);
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Error writing arm state to {statePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the last detection result from last_detection_state.txt.
    /// Returns "none" if file is missing.
    /// </summary>
    public static string GetDetectionState()
    {
        var detPath = Path.Combine(Directory.GetCurrentDirectory(), LastDetectionStateFile);

        if (!File.Exists(detPath))
            return "none"; // default

        try
        {
            return File.ReadAllText(detPath).Trim();
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Error reading detection state: {ex.Message}. Returning default 'none'.");
            return "none";
        }
    }

    /// <summary>
    /// Sets the last detection result in last_detection_state.txt.
    /// Creates file with default if it does not exist.
    /// </summary>
    public static void SetDetectionState(string state)
    {
        var detPath = Path.Combine(Directory.GetCurrentDirectory(), LastDetectionStateFile);

        try
        {
            File.WriteAllText(detPath, state);
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Error writing detection state to {detPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the last Ollama call timestamp from last_ollama_call.txt.
    /// Returns DateTime.MinValue if file is missing (signals no call has been made).
    /// </summary>
    public static DateTime GetLastOllamaCallUtc()
    {
        var ollamaPath = Path.Combine(Directory.GetCurrentDirectory(), LastOllamaCallFile);

        if (!File.Exists(ollamaPath))
            return DateTime.MinValue;

        try
        {
            var content = File.ReadAllText(ollamaPath).Trim();
            return DateTime.Parse(content, null, System.Globalization.DateTimeStyles.RoundtripKind);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing Ollama call timestamp: {ex.Message}. Returning DateTime.MinValue.");
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// Sets the last Ollama call timestamp in last_ollama_call.txt as ISO 8601 UTC.
    /// Creates file if it does not exist.
    /// </summary>
    public static void SetLastOllamaCallUtc(DateTime utcNow)
    {
        var ollamaPath = Path.Combine(Directory.GetCurrentDirectory(), LastOllamaCallFile);

        try
        {
            File.WriteAllText(ollamaPath, utcNow.ToString("o"));
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Error writing Ollama call timestamp to {ollamaPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures the events directory exists under CWD.
    /// Returns true if it exists or was created; false on permission error.
    /// </summary>
    public static bool EnsureEventsDirectoryExists()
    {
        var eventsDir = Path.Combine(Directory.GetCurrentDirectory(), EventsDirName);

        try
        {
            Directory.CreateDirectory(eventsDir); // Always succeeds — returns existing path if already exists
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not create events directory: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Generates a timestamped event file path under the events directory.
    /// Format: "{timestampStr} &lt;type&gt;.{extension}" (matching EVT-01 convention).
    /// </summary>
    public static string GetEventFilePath(string timestampStr, string extension)
    {
        var eventType = "detection";

        // Extract type from timestamp if it contains "event:" prefix
        var eventIndex = timestampStr.IndexOf("event:", StringComparison.OrdinalIgnoreCase);
        if (eventIndex >= 0 && eventIndex + 6 < timestampStr.Length)
        {
            var afterPrefix = timestampStr.Substring(eventIndex + 6).Trim();
            if (!string.IsNullOrEmpty(afterPrefix))
                eventType = afterPrefix;
        }

        var eventsDir = Path.Combine(Directory.GetCurrentDirectory(), EventsDirName);
        var filename = $"{timestampStr} {eventType}.{extension}";
        return Path.Combine(eventsDir, filename);
    }
}
