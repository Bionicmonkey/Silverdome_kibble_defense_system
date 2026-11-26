using System.Diagnostics;
using NewDetector.Config;
using NewDetector.State;

namespace NewDetector.Control;

/// <summary>
/// Static class for controlling the motorized arm via Pololu ticcmd CLI.
/// Issues proper command sequences (energize → resume → position → exit-safe-start → wait → deenergize)
/// and persists arm state to state.txt for shared access with MonitorAppweb.
/// </summary>
public static class ArmController
{
    // --- Default configuration (matches system.json defaults) ---

    private const int DefaultOpenPosition = 1700;
    private const int DefaultClosedPosition = 0;
    private const double DefaultWaitAfterMoveSeconds = 10.0;

    // --- Config (loaded lazily on first use) ---

    private static readonly Lazy<ArmControllerConfig> s_config = new(() => new ArmControllerConfig(1700,0,10000));

    private record ArmControllerConfig(int OpenPosition, int ClosedPosition, int WaitMs);

    private static ArmControllerConfig GetConfig()
    {
        var loaded = s_config.Value;
        if (loaded != null)
            return loaded;

        // Try loading from system.json config
        try
        {
            var systemConfig = ConfigLoader.LoadSystem();
            var arm = systemConfig?.Arm;
            if (arm != null)
            {
                int openPos = arm.OpenPosition > 0 ? arm.OpenPosition : DefaultOpenPosition;
                int closedPos = arm.ClosedPosition >= 0 ? arm.ClosedPosition : DefaultClosedPosition;
                double waitSec = arm.WaitAfterMoveSeconds > 0 ? arm.WaitAfterMoveSeconds : DefaultWaitAfterMoveSeconds;
                return new(openPos, closedPos, (int)(waitSec * 1000));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ArmController] Warning: failed to load ArmConfig from system.json: {ex.Message} — using defaults");
        }

        // Fall back to embedded defaults
        return new(DefaultOpenPosition, DefaultClosedPosition, (int)(DefaultWaitAfterMoveSeconds * 1000));
    }

    // --- Cooldown gate on OpenAsync(), enforced by CloseAsync() tracking ---
    // The cooldown blocks OpenAsync() for 60s after a bad-animal close.
    // This is about preventing re-opening — the arm can only go to position 0
    // and stays there until opened; it cannot "re-close". Every opossum
    // detection (CloseAsync() call) refreshes the full window regardless of
    // whether the move sequence succeeded or failed.

    private static DateTime s_lastCloseUtc = DateTime.MinValue;
    private const int CloseCooldownSeconds = 60;   // 1 minute cooldown on reopening after a bad-animal close

    /// <summary>
    /// Opens the arm by moving to the configured open position.
    /// No cooldown applies — we always want to respond immediately on good animal detection.
    /// </summary>
    public static void OpenAsync()
    {
        var currentState = StateManager.GetArmState();
        if (currentState.Equals("open", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[ArmController] Arm already open — skipping movement");
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - s_lastCloseUtc) < TimeSpan.FromSeconds(CloseCooldownSeconds))
        {
            Console.WriteLine($"[ArmController] Open blocked by cooldown (last close {(int)(now - s_lastCloseUtc).TotalSeconds}s ago, {CloseCooldownSeconds}s window active)");
            return;
        }

        var config = GetConfig();
        Console.WriteLine($"[ArmController] Opening arm (moving to position {config.OpenPosition})...");

        if (!IssueMoveSequence(config.OpenPosition))
        {
            Console.WriteLine("[ArmController] Arm open sequence failed — state not updated");
            return;
        }

        StateManager.SetArmState("open");
        Console.WriteLine("[ArmController] Arm opened successfully");
    }

    /// <summary>
    /// Closes the arm by moving to the configured closed position.
    /// Skips movement if the arm is already closed.
    /// Every call resets s_lastCloseUtc so OpenAsync() is blocked for
    /// CloseCooldownSeconds — prevents the arm from reopening while a bad
    /// animal (opossum) is still present. The full window refreshes on each call,
    /// keeping the arm shut as long as detections continue.
    /// </summary>
    /// 
    public static void resettimer()
    {
        s_lastCloseUtc = DateTime.UtcNow;
    }
    public static void CloseAsync()
    {
        // Every call refreshes the full cooldown window — arm stays shut while threat is active.
        s_lastCloseUtc = DateTime.UtcNow;

        var currentState = StateManager.GetArmState();
        if (currentState.Equals("closed", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[ArmController] Arm already closed — skipping movement");
            return;
        }

        var config = GetConfig();
        Console.WriteLine($"[ArmController] Closing arm (moving to position {config.ClosedPosition})...");

        if (!IssueMoveSequence(config.ClosedPosition))
        {
            Console.WriteLine("[ArmController] Arm close sequence failed — state not updated");
            return;
        }

        StateManager.SetArmState("closed");
        Console.WriteLine("[ArmController] Arm closed successfully");
    }

    /// <summary>
    /// Issues the ticcmd move sequence:
    /// --energize → --resume → --position {pos} → --exit-safe-start → wait → --deenergize
    /// </summary>
    /// <param name="targetPosition">Target position (e.g., 1500 for open, 0 for closed)</param>
    /// <returns>true if all commands succeeded; false if any command failed</returns>
    private static bool IssueMoveSequence(int targetPosition)
    {
        // Validate position to prevent out-of-range values (T-04-01 mitigation)
        if (targetPosition < 0)
        {
            Console.WriteLine($"[ArmController] Invalid target position: {targetPosition} — must be >= 0");
            return false;
        }

 
        if (!RunTicCmd("--energize"))
            return false;
        if (!RunTicCmd("--resume"))
            return false;

        if (!RunTicCmd($"--position {targetPosition}"))
            return false;

        if (!RunTicCmd("--exit-safe-start"))
            return false;

        Console.WriteLine($"[ArmController] Waiting 15s for arm movement to complete...");
        Thread.Sleep(15000);

        return RunTicCmd("--deenergize");
    }

    /// <summary>
    /// Runs a single ticcmd command as an external subprocess.
    /// Matches the movearm reference pattern exactly (C:/altcode/movearm/MoveArm/MoveArm/Program.cs).
    /// </summary>
    private static bool RunTicCmd(string arguments)
    {
        Console.WriteLine($"[ArmController] Running ticcmd: {arguments}");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ticcmd",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Console.WriteLine($"[ArmController] Failed to start ticcmd with args: {arguments}");
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (output.Length > 0)
            {
                Console.WriteLine($"[ArmController] ticcmd output: {output.Trim()}");
            }

            if (process.ExitCode != 0)
            {
                Console.WriteLine($"[ArmController] Error running ticcmd {arguments}: exit code {process.ExitCode} — {error.Trim()}");
                return false;
            }

            Console.WriteLine($"[ArmController] ticcmd {arguments} succeeded");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ArmController] Exception running ticcmd {arguments}: {ex.Message}");
            return false;
        }
    }
}
