---
plan: 05-01
phase: 05-integration-behavioral-verification
status: pending
wave: 1
depends_on:
  - phase: 04-arm-control-event-handling
    waves: [1, 2]
files_modified:
  - .planning/ROADMAP.md
  - .planning/STATE.md
autonomous: true
requirements: []

estimate:
  tokens: 45000
  raw_tokens: 45000
  tasks: 4
  confidence: high

waves:
  wave_1: [build-verify, deploy-script]          # sequential (deploy needs build)
  wave_2: [behavior-analysis, state-verification, event-naming]  # parallel (independent)
  wave_3: [deploy-test, shutdown-test]            # sequential (test depends on deploy)

must_haves:
  truths:
    - deploy.ps1 exists at project root and copies newdetector.exe, system.json, animals.json, OpenCvSharp DLLs, native libs to C:\altcode\AnimalDetector\
    - dotnet build succeeds with zero errors on win-x64 target
    - All state file formats (state.txt, last_detection_state.txt, last_ollama_call.txt) produce output compatible with reference implementation values
    - Event directory naming matches existing events/ convention: `{yyyyMMdd_HH_mm_ss} {type}.jpg` for photos, `{timestamp} Arm Closed.txt` format for text files
  artifacts:
    - deploy.ps1 (new file at project root) — copies all required files to C:\altcode\AnimalDetector\
    - .planning/ROADMAP.md (updated with Phase 5 status and results)
    - .planning/phases/05-integration-behavioral-verification/05-SUMMARY.md (phase summary when complete)
  key_links:
    - from: newdetector.csproj
      to: C:\altcode\AnimalDetector\ (deploy target)
      via: dotnet publish produces newdetector.exe + OpenCvSharp DLLs + native libs; deploy.ps1 copies these files alongside system.json and animals.json
    - from: Modules/State/StateManager.cs
      to: MonitorAppweb compatibility check
      via: state.txt format ("arm=1"/"arm=0") must be compatible with what MonitorApp.exe reads (reference uses plain "open"/"closed")

---

<objective>
Phase 5 is a verification-only phase. No new features are added. The goals are:
1. Build and deploy to C:\altcode\AnimalDetector\ for live testing
2. Compare behavioral output of the .NET composite app against the existing run.bat loop at C:\altcode\AnimalDetector\
3. Verify all state files match expected format (state.txt, last_detection_state.txt, last_ollama_call.txt)
4. Verify events/ directory naming convention matches existing convention
5. Document MonitorAppweb compatibility status
6. Update project docs with deployment notes

Behavioral comparison baseline from reference detector.exe at C:\altcode\detector\Program.cs:
- Run.bat loops: webcamdemo.exe 2 0 → detector.exe "temp.jpg" → goto loop
- Detection paths: cat (open arm + alarm + photo), opossum (close arm + alarm + dual photos 60s apart), ferret (close arm + dual photos no alarm), dog (photo only), none (open arm + save baseline)
- state.txt format: plain "open" or "closed" text
- Event naming: {yyyyMMdd_HH_mm_ss} {type}.jpg for photos

Key behavioral differences identified during analysis:
1. state.txt: .NET writes "arm=0"/"arm=1"; reference reads plain text. .NET read path handles both formats. Compatibility risk for MonitorAppweb reading this file.
2. Alarm file case sensitivity on Windows: "Possum Alarm.wav" works (case-insensitive), but future Linux/macOS would break. Reference detector uses lowercase "possum Alarm.wav".
3. Text event files: reference writes "Arm Closed.txt"/"Arm Opened.txt"; .NET writes "Detected cat {timestamp}" notation — different naming convention for txt files in events/
</objective>

<execution_context>
@~/.claude/gsd-core/workflows/execute-plan.md
@~/.claude/gsd-core/templates/summary.md
</execution_context>

<context>
.planning/ROADMAP.md (Phase 5 requirements)
.planning/REQUIREMENTS.md (all requirements v1)
.planning/PROJECT.md (context, constraints, traceability)
C:\altcode\detector\Program.cs (reference detector behavior — exact detection/response paths)
Modules\State\StateManager.cs (state file I/O — format verification against reference)
Modules\Control\EventManager.cs (event directory naming verification)
Modules\Control\ArmController.cs (arm control state.txt interaction)
Modules\Control\AlarmPlayer.cs (alarm playback path verification)
Program.cs (main loop response routing)
system.json (config values for deploy)
animals.json (animal lists used in detection)
C:\altcode\AnimalDetector\state.txt, C:\altcode\AnimalDetector\last_detection_state.txt, C:\altcode\AnimalDetector\last_ollama_call.txt (reference state file formats on disk)
</context>

<tasks>

<task type="auto">
  <name>Build verification — dotnet build Release on win-x64</name>
  <files></files>
  <read_first>
newdetector.csproj (target framework, runtime identifier, publish settings)
  </read_first>
  <acceptance_criteria>
    - `dotnet build --configuration Release --verbosity quiet` returns exit code 0
    - Zero error messages in output (non-null warnings acceptable per project convention)
    - Output binary at C:\code\newdetectorc#\bin\Release\net10.0\win-x64\newdetector.exe exists
    - OpenCvSharp DLLs present: bin\Debug\net10.0\win-x64\OpenCvSharp.dll, OpenCvSharpExtern.dll
    - Native runtime DLLs present: opencv_videoio_ffmpeg4100_64.dll
  </acceptance_criteria>
  <action>
Run `dotnet build --configuration Release --verbosity quiet` in the project directory.
Verify exit code is 0 and check that output binary exists at bin\Release\net10.0\win-x64\newdetector.exe.

Also verify deployable artifacts:
- OpenCvSharp.dll, OpenCvSharpExtern.dll (managed)
- opencv_videoio_ffmpeg4100_64.dll (native)
- newdetector.deps.json (dependency manifest)
- newdetector.runtimeconfig.json (runtime config)
  </action>
  <verify>
    <automated>dotnet build --configuration Release --verbosity quiet</automated>
    <fails_when>non-zero exit code</fails_when>
  </verify>
  <done>
    - dotnet build succeeds with zero errors
    - newdetector.exe produced at bin\Release\net10.0\win-x64\
    - All required DLLs (OpenCvSharp, OpenCvSharpExtern, opencv_videoio_ffmpeg) present
  </done>
</task>

<task type="auto">
  <name>Create deploy.ps1 — copy all files to C:\altcode\AnimalDetector\</name>
  <files>deploy.ps1 (new)</files>
  <read_first>
C:\code\newdetectorc#\bin\Debug\net10.0\win-x64\*.* (verify which files are produced by build)
C:\altcode\AnimalDetector\Deploy.bat (existing deploy reference — copy pattern to follow)
C:\altcode\AnimalDetector\runtimes\ (existing native runtime layout for reference)
  </read_first>
  <acceptance_criteria>
    - deploy.ps1 exists at project root C:\code\newdetectorc#\deploy.ps1
    - deploy.ps1 copies newdetector.exe to C:\altcode\AnimalDetector\
    - deploy.ps1 copies system.json and animals.json to C:\altcode\AnimalDetector\
    - deploy.ps1 copies OpenCvSharp.dll, OpenCvSharpExtern.dll to C:\altcode\AnimalDetector\
    - deploy.ps1 copies native runtime DLLs (opencv_videoio_ffmpeg4100_64.dll) to appropriate runtimes/win-x64\native\ directory
    - deploy.ps1 copies newdetector.deps.json and runtimeconfig.json
    - deploy.ps1 verifies C:\altcode\AnimalDetector\ exists and creates it if needed
    - Running deploy.ps1 produces all required files in C:\altcode\AnimalDetector\
  </acceptance_criteria>
  <action>
Create deploy.ps1 at project root:

```powershell
# Deploy newdetector to C:\altcode\AnimalDetector\ (reference working directory)
$target = "C:\altcode\AnimalDetector"
if (-not (Test-Path $target)) {
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Write-Host "Created target directory: $target"
}

# Build first if not already built
Write-Host "Building..."
dotnet build --configuration Release --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed. Aborting deploy." -ForegroundColor Red
    exit 1
}

# Copy main binary
Copy-Item "bin\Release\net10.0\win-x64\newdetector.exe" $target -Force
Write-Host "Copied newdetector.exe"

# Copy config files
Copy-Item "system.json" $target -Force
Copy-Item "animals.json" $target $target -Force
Write-Host "Copied system.json, animals.json"

# Copy OpenCvSharp managed DLLs
Copy-Item "bin\Release\net10.0\win-x64\OpenCvSharp.dll" $target -Force
Copy-Item "bin\Release\net10.0\win-x64\OpenCvSharpExtern.dll" $target -Force

# Copy native runtime DLLs
$runtimes = "bin\Release\net10.0\win-x64\runtimes\win-x64\native\"
if (Test-Path "$runtimes") {
    Copy-Item "$runtimes\opencv_videoio_ffmpeg*.dll" $target -Force
}

# Copy deps/runtime configs
Copy-Item "bin\Release\net10.0\win-x64\newdetector.deps.json" $target -Force
Copy-Item "bin\Release\net10.0\win-x64\newdetector.runtimeconfig.json" $target -Force

Write-Host "Deploy complete to: $target" -ForegroundColor Green
```
  </action>
  <verify>
    <automated>powershell -ExecutionPolicy Bypass -File deploy.ps1</automated>
    <fails_when>non-zero exit code or missing critical files in target directory</fails_when>
  </verify>
  <done>
    - deploy.ps1 exists at project root
    - Running deploy.ps1 copies all required files to C:\altcode\AnimalDetector\
    - All critical files verified present after deploy (newdetector.exe, system.json, animals.json, OpenCvSharp DLLs, native libs)
  </done>
</task>

<task type="auto">
  <name>Behavioral comparison analysis — per-path verification against reference detector.exe</name>
  <files>none (read-only analysis)</files>
  <read_first>
C:\altcode\detector\Program.cs (reference detector source — ALL lines, especially: ParseDetectedAnimals, AskOllamaAsync, RunArmCommand, PlayHardcodedAlarm, SaveEventImageCopy)
Modules\Detection\DetectionEngine.cs (diff gate logic: ANAL-01/ANAL-02 implementation)
Modules\Detection\OllamaClient.cs (time-gate logic: ANAL-03)
Modules\Control\EventManager.cs (event file naming convention verification)
Program.cs (response routing switch for all detection paths)
C:\altcode\AnimalDetector\events\* (verify existing event file naming from reference implementation)
C:\altcode\AnimalDetector\state.txt, last_detection_state.txt, last_ollama_call.txt (reference state file formats on disk)
  </read_first>
  <acceptance_criteria>
    - Written comparison document covering ALL detection paths: none (diff-gated), none (fresh), cat, opossum, ferret, dog, unknown
    - For each path: documented expected behavior from reference, actual .NET behavior, equivalence verdict
    - state.txt format verified: .NET writes "arm=1"/"arm=0", reference reads plain "open"/"closed" — compatibility assessed for MonitorAppweb
    - Event naming verified: photos match `{timestamp} {type}.jpg` convention (confirmed from existing events/ in C:\altcode\AnimalDetector\)
    - Text file naming noted: .NET uses "Detected cat {ts}" vs reference "Arm Closed.txt" — format difference documented
    - Alarm file case sensitivity noted on Windows vs future cross-platform
    - Diff gate (ANAL-01/02) implementation status verified by reading DetectionEngine.cs source
    - Time-gate (ANAL-03) implementation status verified by reading OllamaClient/DetectionEngine source
  </acceptance_criteria>
  <action>
Perform a comprehensive line-by-line behavioral comparison between:

Reference detector.exe flow (C:\altcode\detector\Program.cs):
1. Read state.txt (plain text "open"/"closed") via File.ReadAllText
2. Check previous detection state from last_detection_state.txt
3. Compute diff% using 160x120 resize, RGB delta >120 per pixel → threshold is 2%
4. If diff < 2% AND prev run was "none" → skip Ollama, return 0 immediately
5. If >60s since last Ollama call → force fresh check regardless of diff
6. Scale image to max 800px before base64 encoding
7. POST to http://127.0.0.1:11434/api/chat with qwen3.5:4b model
8. Parse response: split on comma/space/newline, filter to known species
9. Cat → RunArmCommand("open") + PlayHardcodedAlarm("Cat Alarm.wav") + webcamdemo capture
10. Opossum → RunArmCommand("close") + SaveEventImageCopy("Arm Close") + Play("possum Alarm.wav") + sleep 60s + webcamdemo
11. Ferret → RunArmCommand("close") + SaveEventImageCopy("Arm Close") + sleep 60s + webcamdemo
12. Dog/none → similar photo save or baseline update

.NET Program.cs flow:
1. ArmController.OpenAsync() issues ticcmd sequence (8 commands) vs reference RunArmCommand("open") — different mechanism, same end state
2. AlarmPlayer.Play uses winmm.dll PlaySound (sync) vs reference using synchronous PlaySound — both synchronous, same result
3. EventManager.SaveEventJpg saves byte[] directly vs reference File.Copy(imagePath, targetPath) — both produce identical JPEG files in events/
4. Response routing: same switch on firstAnimal (cat/opossum/ferret/dog/none/default)
5. SaveDetectionState writes exactly "animal" or "none" — identical format
6. SaveLastOllamaCallUtc writes DateTime.UtcNow.ToString("o") — ISO 8601, compatible with reference read

For each path, document: expected behavior, actual behavior, equivalence verdict, and any gaps.
Write findings to a markdown comparison table.
  </action>
  <verify>
    <automated>none (manual verification of written analysis)</automated>
  </verify>
  <done>
    - All detection paths analyzed with clear equivalence verdict
    - State file format compatibility assessed for MonitorAppweb
    - Event naming convention verified against existing events/ in reference directory
    - Diff gate and time-gate implementation status documented
    - Alarm file case sensitivity noted as Windows vs cross-platform concern
  </done>
</task>

<task type="auto">
  <name>Graceful shutdown state preservation test + dry-run</name>
  <files></files>
  <read_first>
Program.cs (shutdown handler: Console.CancelKeyPress, arm state save on exit)
Modules\State\StateManager.cs (SetArmState, SetDetectionState — verify persistence at shutdown time)
C:\altcode\AnimalDetector\state.txt (reference state file format for comparison)
  </read_first>
  <acceptance_criteria>
    - newdetector runs for ~5 seconds with no camera (graceful failure: "Capturing frame... FAILED", sleeps, retries)
    - Sending Ctrl+C produces: "[Shutdown] Arm state: ..." logging
    - After shutdown, state.txt exists in working directory with valid content
    - last_detection_state.txt persists correctly after shutdown
    - No unhandled exceptions during shutdown sequence
    - webcam.Dispose() called (no resource leak)
  </acceptance_criteria>
  <action>
Test deployment and graceful shutdown:

Step 1 — Deploy: Run `deploy.ps1` from project root. Verify C:\altcode\AnimalDetector\ contains newdetector.exe.

Step 2 — Dry run: In C:\altcode\AnimalDetector\, run:
  `.\newdetector.exe`
Observe output for graceful failure when no camera is available (should print "Capturing frame... FAILED" repeatedly).

Step 3 — Shutdown test: After ~5-10 seconds, send Ctrl+C. Verify:
  - "[Shutdown] Arm state: ..." appears in output
  - "[Shutdown] Final arm state: ..." appears before process exits
  - Process exits cleanly (exit code 0)

Step 4 — State file verification: Read state.txt and last_detection_state.txt from C:\altcode\AnimalDetector\:
  - state.txt should contain "arm=0" (closed, the default initial state)
  - last_detection_state.txt should contain "none" (default initialization)
  - last_ollama_call.txt should contain ISO 8601 UTC timestamp

Step 5 — Compatibility check: Compare state.txt content with reference convention:
  - Reference expects plain "open"/"closed"
  - .NET writes "arm=0"/"arm=1"
  - If MonitorAppweb reads this file and compares to "open", it will NOT match "arm=1"
  - Document whether this is a blocking issue or acceptable
  </action>
  <verify>
    <automated>none (manual observation test)</automated>
  </verify>
  <done>
    - newdetector.exe runs without crash when no camera available
    - Graceful shutdown produces expected log output
    - state.txt and last_detection_state.txt written correctly after shutdown
    - State file format compatibility with MonitorAppweb documented (PASS / PARTIAL / BLOCKING)
  </done>
</task>

</tasks>

<threat_model>
## Phase 5 Trust Boundaries

| Boundary | Description |
|----------|-------------|
| Deploy target C:\altcode\AnimalDetector\ | Trusted local directory; deploy script runs with user privileges |
| MonitorAppweb state file read | MonitorApp.exe reads state.txt — if format mismatched, monitoring displays stale/wrong data |
| ticcmd subprocess path | Deployed app inherits PATH from environment; ticcmd must be installed and in PATH on target machine |

## STRIDE Threat Register for Phase 5

| Threat ID | Category | Component | Severity | Disposition | Mitigation Plan |
|-----------|----------|-----------|----------|-------------|-----------------|
| T-05-01 | Spoofing | Deployed newdetector.exe replaced by attacker on C:\altcode\AnimalDetector\ | low | accept | Local deployment; physical access already implies trust. Future: code-signing. |
| T-05-02 | Tampering | state.txt format incompatibility causes MonitorAppweb to show stale arm state | high | mitigate | Verify state.txt format is compatible with MonitorApp.exe reads. If not, add a migration or write-both-formats option. |
| T-05-03 | Information Disclosure | deploy.ps1 logs file paths to console — harmless for local use but sensitive if script shared | low | accept | deploy.ps1 is project-local only; not intended for distribution. |

</threat_model>

<verification>
1. `dotnet build --configuration Release --verbosity quiet` returns exit code 0 (zero errors)
2. deploy.ps1 executes without error and all critical files present in C:\altcode\AnimalDetector\:
   - newdetector.exe
   - system.json, animals.json
   - OpenCvSharp.dll, OpenCvSharpExtern.dll
   - opencv_videoio_ffmpeg*.dll (native)
3. Graceful shutdown test: state.txt written with "arm=0" or "arm=1", last_detection_state.txt written with "none" or "animal"
4. Event naming convention verified: existing events/ in C:\altcode\AnimalDetector\ shows pattern `{timestamp} {type}.jpg` — .NET SaveEventJpg produces identical format
5. Behavioral comparison matrix complete for all 6 detection paths (cat, opossum, ferret, dog, none, unknown)
</verification>

<success_criteria>
- dotnet build succeeds with zero errors on net10.0 win-x64
- deploy.ps1 exists and successfully copies all required files to C:\altcode\AnimalDetector\
- All state file formats verified compatible with reference implementation:
  - state.txt: "arm=0"/"arm=1" — format difference from plain text documented (compatibility depends on MonitorAppweb)
  - last_detection_state.txt: "none"/"animal" — IDENTICAL to reference ✓
  - last_ollama_call.txt: ISO 8601 UTC via ToString("o") — IDENTICAL to reference ✓
- Event directory naming convention verified: {yyyyMMdd_HH_mm_ss} {type}.jpg matches existing events/ in reference directory ✓
- Behavioral equivalence documented for all detection paths
- Graceful shutdown test: state files persisted correctly, no crash on missing hardware
- .planning/ROADMAP.md updated with Phase 5 status and results
</success_criteria>

<output>
Create `.planning/phases/05-integration-behavioral-verification/05-SUMMARY.md` when done
</output>
