# App Dependencies

Exhaustive list of tools, frameworks, SDKs, hardware, and materials required to build, run, and develop the Silverdome Kibble Defense System. Grouped by dependent project or system component.

---

## 1. Runtime / Platform

| Item | Version | Purpose | Notes |
|------|---------|---------|-------|
| **Windows 10/11** (x64) | — | Host OS for all software and hardware | All projects target `win-x64` runtime identifier |
| **.NET 10.0 SDK** | net10.0 | Compile target for all C# projects | Required to build `newdetector`, `ollama-batcher`, `MoveArm` |
| **.NET 10.0 Runtime** (ASP.NET Core) | net10.0 | Host the MonitorAppweb Flask app? No — Python only. But .NET runtime needed for all C# apps at deploy time | Framework-dependent deploy (`<SelfContained>false</SelfContained>`); the target machine needs the ASP.NET Core runtime bundle installed |
| **Ollama** | latest stable | Local LLM inference server (vision model) | Runs on `http://127.0.0.1:11434`. Both `qwen3.5:4b` and `qwen3.6:35b` models need to be pulled into the local Ollama library |

---

## 2. C# / .NET Dependencies (nuget packages)

### newdetector (`newdetector.csproj`)

| Package | Version | Use |
|---------|---------|-----|
| **OpenCvSharp4** | 4.10.0.20240615 | OpenCV bindings — frame capture, image diff computation, color space conversion, resize, JPEG encode/decode |
| **OpenCvSharp4.runtime.win** | 4.10.0.20240615 | Native OpenCV binaries (win-x64/win-x86) with FFmpeg video backend — ships `opencv_videoio_ffmpeg4100_64.dll` and `OpenCvSharpExtern.dll` |
| **System.Drawing.Common** | 11.0.0 | Bitmap resize, pixel-level RGB delta comparison (diff gate), color manipulation |

### ollama-batcher (`ollama-batcher.csproj`)

| Package | Version | Use |
|---------|---------|-----|
| **OpenCvSharp4** | 4.10.0.20240615 | Image scaling (ImDecode/Resize/ImEncode before sending to Ollama) |
| **OpenCvSharp4.runtime.win** | 4.10.0.20240615 | Native OpenCV runtime |

### MoveArm (`MoveArm.csproj`)

| Package | Version | Use |
|---------|---------|-----|
| _(none)_ | — | Pure CLI — no NuGet packages. Only uses `System.Diagnostics.ProcessStartInfo` to invoke `ticcmd.exe` |

---

## 3. Python Dependencies (MonitorAppweb)

### Runtime (Flask app)

These are not listed in `pyproject.toml[project.dependencies]` but are imported at runtime:

| Package | Version | Use |
|---------|---------|-----|
| **Flask** | (unspecified, latest compatible with Python 3.11) | WSGI web framework — routes `/`, `/api/state`, `/snapshot`, `/events`, `/api/event/images/<date>`, `/api/event/image/<file>` |
| **Werkzeug** | bundled with Flask | `safe_join()` for path traversal prevention, static file serving |

### Development

| Package | Version (in pyproject.toml) | Use |
|---------|----------------------------|-----|
| **pytest** | >=7.0 | Test runner — `tests/test_app.py`, `tests/test_events.py` |

### Python version

| Item | Value |
|------|-------|
| **Python** | 3.11 (observed from `__pycache__/__init__.cpython-311.pyc`) |

---

## 4. External Command-Line Tools (runtime dependencies)

These are external binaries expected to be on the system PATH at runtime:

| Tool | Source | Used By | Purpose |
|------|--------|---------|---------|
| **ticcmd** | Pololu TIC Controller Software | `newdetector` (`ArmController.cs`), `MoveArm` (`Program.cs`) | Controls Pololu TIC500 stepper motor controller over serial/USB — commands: `--energize`, `--resume`, `--position`, `--exit-safe-start`, `--deenergize` |
| **Ollama API** (local) | Ollama server at `http://127.0.0.1:11434` | `newdetector` (`DetectionEngine.cs` → `OllamaClient.PromptAndSend()`), `ollama-batcher` | Vision inference endpoint (`/api/chat`) — receives base64-encoded images + text prompts, returns JSON with model's response text |

---

## 5. AI / ML Models (to pull via Ollama)

| Model | Size | Type | Purpose | Used By |
|-------|------|------|---------|---------|
| **qwen3.5:4b** | ~4B params | Vision (Qwen series) | Default model for both `newdetector` and `ollama-batcher` (per `system.json` default + batch help text) | All detection |
| **qwen3.6:35b** | ~35B params | Vision (Qwen series, upgraded) | Listed in `ollama-batcher` help as an example model; target for future upgrade from 4B → 35B for better classification accuracy | Future target |

**Pull command:**
```bash
ollama pull qwen3.5:4b
ollama pull qwen2.5vl
```

---

## 6. Hardware — FeederMachine Build (from `FeederMachine/Readme.txt`)

### Pololu TIC Stepper Motor Controller

| Item | Part # / Link | Purpose |
|------|---------------|---------|
| **Pololu TIC500** | [pololu.com/product/3134](https://www.pololu.com/product/3134) | USB-connected programmable stepper motor driver — controls the gate arm position. Requires `ticcmd` CLI tool from Pololu's TIC Controller Software download. |

### Stepper Motor

| Item | Part / Link | Purpose |
|------|-------------|---------|
| **Creality 42-40 stepper motor** | [Amazon](https://www.amazon.com/Creality-Official-Stepper-Extruder-Compatible/dp/B09ZQT9DJY) | NEMA 17-style bipolar stepper — actuates the feeder gate arm. 4-wire, 6-position connector cable required. |

### Mechanical / Hardware

| Item | Qty | Notes |
|------|-----|-------|
| **3D printer filament** | ~2 kg (any type) | ABS recommended for outdoor longevity; PLA/PETG viable depending on climate |
| **USB cable** (to connect Pololu TIC500) | 1 | Standard USB-A to USB-B (printer-style) |
| **6-position stepper motor cable, 4-wire** | 1 | Connects Creality 42-40 motor to Pololu TIC500 board |
| **Glue gun + glue sticks** | 1 each | Enclosure / strain relief assembly |
| **3mm screws** | Various | Frame mounting |
| **4mm screws** | Various | Structural mounting |

### Max Printed Part Size (for slicer setup)

| Dimension | Value |
|-----------|-------|
| Largest part bounding box | 335 × 180 × 140 mm |

### STL Files (all under `FeederMachine/Printed Parts/`)

| File | Description |
|------|-------------|
| `base online -nocup.stl` | Main base housing |
| `base online -nocupskinny.stl` | Slim variant of base housing |
| `bottom bracket.stl` | Lower mounting bracket |
| `Cup Cover.stl` | Food cup cover |
| `electrical protector.stl` | Enclosure for electrical components |
| `Food Cup.stl` | Kibble dispensing cup |
| `gear side spacer.stl` | Spacing shim for gear assembly |
| `gear-lash spacers.stl` | Gear lash compensation spacers |
| `Gear.stl` | Main drive gear |
| `lid.stl` | Top cover / lid |
| `top bracket.stl` | Upper mounting bracket |
| `worm gear-recut.stl` | Recut worm gear (revised profile) |
| `worm gear-split.stl` | Split-worm-gear variant |
| `worm gear.stl` | Original worm gear |

### Design Reference Images (under `FeederMachine/Images/`)

| File | Description |
|------|-------------|
| `Machine Design.jpg` | Full assembly photo |
| `screw mechanism.jpg` | Close-up of screw/gear mechanism |

---

## 7. Development IDE & Editor Tools

### For C# / .NET Projects

| Tool | Purpose |
|------|---------|
| **Visual Studio 2022** (17.x+) | Full IDE for .NET project development, debugging, NuGet management, publishing. `.slnx` solution files are Visual Studio 2025+ format. |
| **VS Code** + C# Dev Kit extension | Lightweight alternative; supports IntelliSense, debugging, `dotnet` CLI workflows |
| **dotnet CLI** (`dotnet build`, `dotnet publish`, `dotnet test`) | All project builds use the .NET SDK CLI. See section 2 for required packages. |

### For Python / MonitorAppweb

| Tool | Purpose |
|------|---------|
| **Python 3.11** (min) | Runtime + development |
| **pip / venv** | Dependency management — `python -m venv .venv` then `pip install flask pytest` |
| **Flask CLI** (`flask run`) | Optional: local dev server launcher |

---

## 8. Configuration Files (distributed with builds)

These files are loaded at runtime by the applications and should be included in deployment:

| File | Format | Used By | Purpose |
|------|--------|---------|---------|
| `system.json` | JSON | newdetector | System-wide config: webcam index, retry settings, detection thresholds, Ollama endpoint/model/timeout, arm positions, alarm file paths |
| `animals.json` | JSON | newdetector | Species lists (`good`, `bad`), prompt template (image classification instructions), species whitelist |
| `config.json` | JSON | newdetector | Legacy / alternate config (exists in root but not actively referenced by current code) |

---

## 9. Test Projects

| Project | Type | Framework | Tests |
|---------|------|-----------|-------|
| `newdetector/tests/NewDetector.Camera.Tests/` | .NET xUnit | xUnit (via `<PackageReference Include="Microsoft.NET.Test.Sdk">`) | `WebcamCaptureTests.cs` |
| `newdetector/tests/NewDetector.Detection.Tests/` | .NET xUnit | xUnit | `DetectionEngineTests.cs` |
| `MonitorAppweb/tests/` | Python | pytest (>=7.0) | `test_app.py`, `test_events.py` |

---

## 10. Deployment / Build Artifacts

### C# Build Output (per project)

| Folder | Content |
|--------|---------|
| `bin/Debug/net10.0/` or `bin/Release/net10.0/` | Compiled DLLs, PDBs, `*.deps.json`, `*.runtimeconfig.json`, published exe |
| `obj/` | Intermediate build files — package resolution, generated source (`.csproj.*`), NuGet cache |
| `.publish/` (newdetector only) | Staging directory for `dotnet publish` before final copy to `C:\altcode\AnimalDetector\` |

### Python Build Output

| Folder | Content |
|--------|---------|
| `__pycache__/` | Compiled `.pyc` bytecode — auto-generated by Python runtime |
| `.pytest_cache/` | pytest cache (node IDs, lastfailed) — should be gitignored |

---

## 11. Monitoring / Diagnostic Tools (recommended)

| Tool | Purpose |
|------|---------|
| **Pololu TIC Device Center** | Windows GUI for configuring the TIC500 controller — target position limits, current settings, safe-start options, firmware updates |
| **Ollama CLI** (`ollama list`, `ollama ps`) | Model management: list installed models, check running status, pull new models |
| **Process Explorer / Task Manager** | Monitor CPU/memory usage of Ollama (35B model can be memory-intensive — ~20+ GB VRAM for full model) |
| **Browser (any modern)** | Access MonitorAppweb dashboard at `http://<feeder-pc-ip>:8080` |

---

## 12. Checklist — Things to Download / Install Before First Run

- [ ] **.NET 10 SDK** — [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)
- [ ] **Ollama for Windows** — [ollama.com](https://ollama.com)
- [ ] **Ollama models**: `qwen3.5:4b` and/or `qwen3.6:35b` (pull via CLI)
- [ ] **Pololu TIC Controller Software** (includes `ticcmd.exe`) — [pololu.com/page/tic](https://www.pololu.com/page/tic)
- [ ] **Python 3.11+** — [python.org](https://www.python.org/downloads/)
- [ ] **Flask** (`pip install flask`)
- [ ] **pytest** (`pip install pytest`)
- [ ] **Visual Studio 2022/2025** or **VS Code + C# Dev Kit** (for development)
- [ ] **3D printer** capable of ≥335 × 180 × 140 mm build volume (for FeederMachine hardware)
- [ ] ~2 kg filament (ABS preferred for outdoor longevity)
