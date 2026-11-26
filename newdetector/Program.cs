using NewDetector;
using NewDetector.Config;
using NewDetector.Detection;
using NewDetector.State;
using NewDetector.Camera;
using NewDetector.Control;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace NewDetector;

/// <summary>
/// Entry point for the animal detector composite feeder controller.
/// </summary>
public static class Program
{

    [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);


    private static volatile bool s_shutdownRequested;

    /// <summary>
    /// Application entry point — loads configuration, initializes state manager,
    /// and enters the main detection loop until termination signal.
    /// </summary>
    public static void Main(string[] args)
    {
        Console.WriteLine("NewDetector starting...");

        // Register Ctrl+C / SIGTERM graceful shutdown handler
        Console.CancelKeyPress += (sender, e) =>
        {
            s_shutdownRequested = true;
            Console.WriteLine("Shutting down...");
            // Save current arm state before exit (RUNT-02 graceful shutdown)
            var currentState = StateManager.GetArmState();
            Console.WriteLine($"[Shutdown] Arm state: {currentState}");
            e.Cancel = true; // Allow cleanup before exit
        };

#if WIN32
        // Register Win32 console control event for Windows-specific termination
        NativeMethods.SetConsoleCtrlHandler(null, 0);
#endif

        // Load configuration with safe defaults (handles missing/invalid config per CONFIG-03)
        var animalsConfig = ConfigLoader.LoadAnimals()!;
        var systemConfig = ConfigLoader.LoadSystem()!;

        Console.WriteLine($"Loaded {animalsConfig.Good.Length} good animal(s): {string.Join(", ", animalsConfig.Good)}");
        Console.WriteLine($"Loaded {animalsConfig.Bad.Length} bad animal(s): {string.Join(", ", animalsConfig.Bad)}");
        Console.WriteLine($"Webcam index: {systemConfig.Webcam.DefaultIndex}, retry attempts: {systemConfig.Webcam.RetryAttempts}");

        // Instantiate webcam capture module (Phase 2)
        var webcam = new WebcamCapture(systemConfig.Webcam!);

        // Open camera once before entering the loop — avoids per-frame DShow churn.
        if (!webcam.Open())
        {
            Console.WriteLine("[Fatal] Failed to open webcam. Exiting.");
            return;
        }

        // Initialize state manager (creates state files and events directory if needed)
        StateManager.EnsureInitialized();

        // Main Loop (Phase 2+): webcam capture → detection → response   -------------------------------------------------------------------
        while (!s_shutdownRequested)
        {
            Console.Write("Capturing frame... ");             //  ----------------------------------- GET FRAME
            var frameBytes = webcam.CaptureLive();
            if (frameBytes == null)
            {
                Console.WriteLine("FAILED");
                Thread.Sleep(1000);
                continue;
            }
            
            Console.WriteLine($"OK! ({frameBytes.Length} bytes, saved as temp.jpg)");

            // Phase 3: Detection pipeline — analyze frame through Ollama vision model
            var detection = systemConfig.Detection ?? new();
            var ollama = systemConfig.Ollama ?? new();
            var animalsCfg = animalsConfig;

            var result = DetectionEngine.Analyze(              //----------------------------------- DETECT ANIMALS
                frameBytes,
                animalsCfg.SpeciesWhitelist,
                detection,
                ollama,
                animalsCfg.PromptTemplate);

            if (result.WasSkipped)
            {
                Console.WriteLine($"[Detection] Skipped — last known: {string.Join(", ", result.Animals)}");
            }
            else
            {
                var summary = result.Animals.Count > 0
                    ? $"Found: {string.Join(", ", result.Animals)}"
                    : "No animals detected";
                Console.WriteLine($"[Detection] {summary}");

                // Phase 4: Config-driven response routing — good animals override bad animals.
                // Defensive check: treat empty array the same as ["none"] to cover both
                // (a) DetectionEngine skipping Ollama + returning no data, and
                // (b) Ollama returning an empty/blank response that ResponseParser didn't normalize.
                var hasAnyAnimal = result.Animals?.Count > 0;

                // Classify detected animals by config good/bad lists (filter out "none" entries).
                var detectedAnimals = hasAnyAnimal ? result.Animals!.Where(a => a != "none").ToList() : new List<string>();
                var goodMatches = detectedAnimals.Intersect(animalsConfig.Good).ToArray();
                var badMatches = detectedAnimals.Except(animalsConfig.Good).Intersect(animalsConfig.Bad).ToArray();

                // "None" path: open arm, update baseline, log event — covers empty array + ["none"]
                var isSingleNone = result.Animals != null && result.Animals.Count == 1 && result.Animals[0] == "none";
                if (isSingleNone || !hasAnyAnimal)
                {
                    Console.WriteLine("[Response] No animal detected — opening arm, updating baseline...");
                    if (StateManager.GetArmState() != "open") ArmController.OpenAsync();
                    EventManager.UpdateBaseline("temp.jpg");
                 //   EventManager.SaveEventFile($"No Animal {DateTime.Now:yyyyMMdd_HH_mm_ss}");

                    // Save detection state and Ollama call timestamp (EVT-02, EVT-03)
                    EventManager.SaveDetectionState("none");
                    EventManager.SaveLastOllamaCallUtc(DateTime.UtcNow);
                }
                else if (goodMatches.Any())
                {
                    // GOOD animal present: open arm, save event photo for each detected animal
                    Console.WriteLine($"[Response] Good animal(s) detected ({string.Join(", ", goodMatches)}) — opening arm, saving event photos...");
                    if (StateManager.GetArmState() != "open") ArmController.OpenAsync();
                    foreach (var animal in detectedAnimals)
                    {
                        EventManager.SaveEventJpg(frameBytes, animal);
                        // Write a fresh frame to temp.jpg so MonitorAppweb always sees current activity
                        var freshFrame = webcam.CaptureLive();   // rewrites temp.jpg with a fresh frame
                        if (freshFrame != null) frameBytes = freshFrame;
                        
                    }

                    // Save detection state and Ollama call timestamp (EVT-02, EVT-03)
                    EventManager.SaveDetectionState("animal");
                    EventManager.SaveLastOllamaCallUtc(DateTime.UtcNow);

                    // Save arm event log for arm actions (EVT-04)
                   // EventManager.SaveEventFile("Arm Opened");
                }
                else if (badMatches.Any())
                {
                    ArmController.resettimer(); // Reset cooldown timer on each bad animal detection to keep arm closed

                    // Only BAD animals present: close arm, save event photos + second photo set after settle.
                    // Detection loop is not frozen — the 60s cooldown lives in ArmController, not here.
                    Console.WriteLine($"[Response] Bad animal(s) detected ({string.Join(", ", badMatches)}) — closing arm, saving event photos...");
                    var wasAlreadyClosed = StateManager.GetArmState() == "closed";
                    if (!wasAlreadyClosed)
                    {
                        ArmController.CloseAsync();

                //        var freshFrame = webcam.CaptureLive();  // writes temp.jpg
                  //      if (freshFrame != null) frameBytes = freshFrame;

                    }

                    // temporary hack
                    PlaySound(Path.GetFullPath("C:\\altcode\\AnimalDetector\\Possum Alarm.wav"), IntPtr.Zero, 0x20000 | 0x0);

                    // Only save photos if arm actually moved this cycle — avoids redundant saves when
                    // the same animal is re-detected while the arm is already in its target state.
                    
                    foreach (var animal in badMatches)
                    {
                        EventManager.SaveEventJpg(frameBytes, animal);
                        // Write a fresh frame to temp.jpg so MonitorAppweb always sees current activity
                    }

                    
                    // Save detection state and Ollama call timestamp (EVT-02, EVT-03)
                    EventManager.SaveDetectionState("animal");
                    EventManager.SaveLastOllamaCallUtc(DateTime.UtcNow);

                    // Save arm event log for arm actions (EVT-04)
                   // EventManager.SaveEventFile("Arm Closed");
                }
                else if (hasAnyAnimal)
                {
                    // Unknown species detected: at least one animal found but none matched config lists.
                    Console.WriteLine($"[Response] Unknown species detected ({string.Join(", ", detectedAnimals)}) — ignoring");
                    // Save event file with actual animal names from Ollama detection
                    var animalTags = string.Join("_", detectedAnimals);
                    EventManager.SaveEventFile($"{animalTags}_{DateTime.Now:yyyyMMdd_HH_mm_ss}");
                    // Save detection state and Ollama call timestamp (EVT-02, EVT-03)
                    EventManager.SaveDetectionState("animal");
                    EventManager.SaveLastOllamaCallUtc(DateTime.UtcNow);
                }
            }

            // Phase 3+: pass frameBytes or temp.jpg path to detection pipeline
            // Camera capture loop complete — per D-03 layer structure WebcamCapture is its own module

            Thread.Sleep(1000);
        }

        // Final state save before exit (RUNT-02 graceful shutdown)
        Console.WriteLine($"[Shutdown] Final arm state: {StateManager.GetArmState()}");

        webcam.Dispose();
        Console.WriteLine("Shutting down gracefully...");
    }
}

#if WIN32
internal static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetConsoleCtrlHandler(
        HandlerRoutine? handler,
        bool add);

    private delegate bool HandlerRoutine(uint dwCtrlType);
}
#endif
