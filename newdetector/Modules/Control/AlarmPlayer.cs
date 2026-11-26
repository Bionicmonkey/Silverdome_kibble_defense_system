using System.Runtime.InteropServices;

namespace NewDetector.Control;

/// <summary>
/// Static class for WAV alarm playback via Windows winmm.dll PlaySound P/Invoke.
/// Alarm playback is best-effort — missing files or failures log warnings but never crash.
/// </summary>
public static class AlarmPlayer
{
    private const int SND_FILENAME = 0x20000;
    private const int SND_ASYNC = 0x1;

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern bool PlaySound(string pszSound, IntPtr hmod, int flags);

    /// <summary>
    /// Plays a WAV alarm file asynchronously.
    /// Resolves path from CWD if relative; warns on missing files (best-effort).
    /// </summary>
    public static void Play(string wavFile)
    {
        if (string.IsNullOrWhiteSpace(wavFile))
        {
            Console.WriteLine("[AlarmPlayer] No alarm file specified");
            return;
        }

        string fullPath = Path.IsPathFullyQualified(wavFile)
            ? wavFile
            : Path.Combine(Directory.GetCurrentDirectory(), wavFile);

        if (!File.Exists(fullPath))
        {
            Console.WriteLine($"[AlarmPlayer] Alarm file not found: {fullPath}");
            return; // best-effort — don't crash on missing alarm
        }

        try
        {
            Console.WriteLine($"[AlarmPlayer] Playing: {wavFile}");
            PlaySound(fullPath, IntPtr.Zero, SND_FILENAME | SND_ASYNC);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AlarmPlayer] Error playing alarm '{wavFile}': {ex.Message}");
        }
    }
}
