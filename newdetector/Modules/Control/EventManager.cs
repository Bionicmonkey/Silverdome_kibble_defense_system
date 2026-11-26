using System.Globalization;
using NewDetector.State;

namespace NewDetector.Control;

/// <summary>
/// Static class for event I/O operations — timestamped photos, file logs,
/// and state persistence. All paths are CWD-relative per RUNT-03.
/// </summary>
public static class EventManager
{
    /// <summary>
    /// Saves a frame byte array as a JPEG event photo in the events/ directory.
    /// Filename format: {yyyyMMdd_HH_mm_ss} {type}.jpg (EVT-01)
    /// </summary>
    public static void SaveEventJpg(byte[] frameBytes, string eventType)
    {
        try
        {
            StateManager.EnsureEventsDirectoryExists();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HH_mm_ss", CultureInfo.InvariantCulture);
            var eventsDir = Path.Combine(Directory.GetCurrentDirectory(), "events");
            var filename = $"{timestamp} {eventType}.jpg";
            var fullPath = Path.Combine(eventsDir, filename);
            File.WriteAllBytes(fullPath, frameBytes);
            Console.WriteLine($"[EventManager] Saved event photo: {fullPath}");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[EventManager] Error saving event photo: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves a timestamped text event file in the events/ directory.
    /// Filename format: {yyyyMMdd_HH_mm_ss} {notation}.txt (EVT-04)
    /// </summary>
    public static void SaveEventFile(string notation)
    {
        try
        {
            StateManager.EnsureEventsDirectoryExists();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HH_mm_ss", CultureInfo.InvariantCulture);
            var eventsDir = Path.Combine(Directory.GetCurrentDirectory(), "events");
            var filename = $"{timestamp} {notation}.txt";
            var fullPath = Path.Combine(eventsDir, filename);
            File.WriteAllText(fullPath, notation);
            Console.WriteLine($"[EventManager] Saved event file: {fullPath}");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[EventManager] Error saving event file: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves the last detection state (EVT-02).
    /// </summary>
    public static void SaveDetectionState(string state)
    {
        StateManager.SetDetectionState(state);
        Console.WriteLine($"[Response] saved detection state: {state}");
    }

    /// <summary>
    /// Saves the last Ollama call timestamp as UTC ISO 8601 (EVT-03).
    /// </summary>
    public static void SaveLastOllamaCallUtc(DateTime utc)
    {
        StateManager.SetLastOllamaCallUtc(utc);
        Console.WriteLine($"[Response] saved last Ollama call timestamp");
    }

    /// <summary>
    /// Updates the no-animal baseline image (RESP-05).
    /// Copies sourcePath (default temp.jpg) to last_no_animal.jpg.
    /// </summary>
    public static void UpdateBaseline(string sourcePath = "temp.jpg")
    {
        try
        {
            var cwd = Directory.GetCurrentDirectory();
            var dest = Path.Combine(cwd, StateManager.LastNoAnimalJpgPath);
            File.Copy(sourcePath, dest, overwrite: true);
            Console.WriteLine($"[Response] updated baseline from {sourcePath}");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[Response] error updating baseline: {ex.Message}");
        }
    }
}
