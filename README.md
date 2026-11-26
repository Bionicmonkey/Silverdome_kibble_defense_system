# Silverdome — Kibble Defense System

## Overview

first let me take a moment to explain the intention of the project and why it exists.  As many people have tried over the years I attempted to feed stray cats that have come to my porch for reasons that are complex and specific to my circumstances i am not in a place to take the cats in.  But wanting to provide them with some food seemed appropriate. However after not too long a period of time I discovered that one of my cats was SUS in fact on later examination it's an Opossum. The problem there isn't that I don't mind feeding the opossum but the opossum eats everything and i mean Literally everything meaning the cats get no food so in my circumstances not being allowed to dispatch the animal I instead endeavored to create a cat feeder which would use AI to determine what kind of animal is facing the feeder so that the feeder can intelligently allow access by a certain type of animal or even potentially a certain color or description of animal and not to other animals that way special collar or special procedure or special action on the part of the cat would be needed It would simply walk up to the feeder and gain access to food but the possum would be unable to to do that same thing.

This project is the successful completion of that project statement in a form which is recreatable by others, although I do leave the process of model fine tuning to you as that is a whole separate project worthy of its own document.  This application paired with off the shelf models such as QWEN 3.5:4b or qwen2.5vl will give you a good starting point, and then you can build a library of your own images with which to do a fine tune to acheive the same fast detection with high accuracy.

In terms of complexity this is not super complex, but involves multi-disciplinary capabilities including software engineering, AI (including model fine tuning), 3D Printing, and some light robotics.  this is certainly not a "ready for use" project where you can follow the guide blindly and expect results.  Additionally while the code is all C# console apps and python - designed so to be ready for conversion to a Raspi- type host machine, at the moment the code expects to be run from a host machine that is accessible to the user, while being USB connected to the robotic feeder.

If you are interested in contributing, please do reach out to connect. 

IMPORTANT LICENSING NOTE - see the PolyForm NonCommercial License document in this repo which explains the NON-COMMERCIAL license granted to use this software and design.

## The Three Pillars

The system is built from three interconnected parts — hardware, vision, and computation:

### 1. 3D-Printed Feeder Machine

A self-contained motorized gate mechanism printed in parts and assembled on a benchtop extrusion printer. A NEMA 17 stepper motor drives a worm gear that rotates a gate arm between open and closed positions, controlled over USB by a Pololu TIC500 stepper driver. The housing encloses the electronics, protects against weather, and routes kibble from a food cup through the gate opening.

![Full assembly](FeederMachine/Images/"Machine Design.jpg")

### 2. Webcam — the Eye

A standard DirectShow webcam (MJPG codec) captures frames at approximately 1-second intervals. The capture loop grabs 5 consecutive frames and selects the one with the highest contrast — filtering out motion blur from quick-moving animals. Each frame feeds through an image-diff gate: if the scene hasn't changed more than ~10%, Ollama is skipped entirely, saving inference time.

### 3. Host Computer — the Brain

A always-on PC connected to both the webcam and the TIC500 controller via USB/serial. It runs `newdetector` (the core detection daemon), `MonitorAppweb` (a Flask dashboard for monitoring), and communicates with Ollama for vision-based animal classification via a local LLM. Designed to be migratable to a Raspberry Pi 5 with on-device inference down the line.

![Screw / gear mechanism close-up](FeederMachine/Images/"screw mechanism.jpg")

## Projects in this Repository

Located at `C:\AnimalDetectorRoot\` — six distinct sub-projects:

| # | Folder | Language / Stack | Purpose |
|---|--------|------------------|---------|
| 1 | **newdetector** | C# (.NET 10) | Core detection engine — webcam capture → image-diff gating → Ollama vision inference → consensus voting → arm control via Pololu TIC500. The primary daemon that runs continuously on the feeder host PC. |
| 2 | **ollama-batcher** (inside `quickvalidator/`) | C# (.NET 10) | Batch CLI tool for offline image validation. Loads JPGs from a folder, sends them to Ollama with a custom prompt, saves structured results. Used in training / model evaluation workflows (`quickvalidator.bat` variants). |
| 3 | **MoveArm** | C# (.NET 10) | Standalone CLI for direct stepper-motor arm control via `ticcmd`. Deprecated — functionality is now embedded inside `newdetector`'s `ArmController` module. Kept as a reference / fallback. |
| 4 | **MonitorAppweb** | Python 3.11 + Flask | Web dashboard serving the feeder's current arm state, live camera snapshot stream, and browsable event-history timeline. Accessible from any browser on the home LAN (port 8080). |
| 5 | **FeederMachine** | Hardware / CAD | 3D-printed mechanical assembly — STL files for all printed parts, design photos, and a BOM with hardware source links (Pololu TIC500, Creality stepper motor, etc.). Build guide in `Readme.txt`. |
| 6 | **RunningFolder** | — | Placeholder / staging folder. Status unknown. |

## System Architecture (Logical Flow)

```
Webcam (DShow) ──→ newdetector (capture → diff-gate → Ollama CLI) ──→ ticcmd (Pololu TIC500)
                                                                    ↓
                                                          state.txt + events/  ← shared disk
                                                                    ↓
                                              MonitorAppweb (Flask reads files → JSON + images over HTTP)
```

### Key Shared Artifacts

- **`state.txt`** — plain-text arm state (`open` / `closed`), written by `newdetector`, read by `MonitorAppweb`.
- **`temp.jpg`** — latest captured frame, shared between `newdetector` (writer) and `MonitorAppweb` (reader for `/snapshot`).
- **`events/`** — timestamped event photos and arm-action logs, consumed by the MonitorApp timeline.

## AI Pipeline

1. **Image-diff gate** (`System.Drawing` + OpenCvSharp fallback): if scene hasn't changed beyond a threshold (~10%), skip Ollama to save inference time.
2. **Time gate**: force a fresh Ollama call every 30 seconds regardless of diff score, preventing stale state on long static periods.
3. **Ollama `qwen3.5:4b`** (local): vision model receives JPEG → text prompt asking it to classify animals on the porch.
4. **Response parser**: normalizes Ollama output against a species whitelist (`cat`, `dog`, `opossum`, `ferret`).
5. **Rolling consensus**: last 5 raw labels, majority rule (≥3 of 5) to prevent flickering on ambiguous frames.
6. **Daytime heuristic**: opossum detections between 7 am–5 pm are downgraded to "cat" to reduce false-positive arm closures (opossums are nocturnal; daytime misidentifications are common).

## Good Animal → Bad Animal Routing

- **Good match** (e.g. cat, dog): opens the gate arm, saves event photo per detected species.
- **Bad match** (e.g. opossum): closes the arm with a 60-second cooldown on reopen to prevent immediate reopening while the animal is still present. Plays a WAV alarm file from disk.
- **Unknown / no match**: logs event, takes no action.

## Build & Deploy

- **C# projects**: `dotnet build` / `dotnet publish --runtime win-x64`. Output deployed to `C:\altcode\AnimalDetector\`.
- **Python app**: `python -m MonitorApp` from the `MonitorAppweb` directory (Flask dev server on port 8080).

## Future Directions

- Migrate MoveArm out entirely once all hardware runs through newdetector's ArmController.
- migrate the whole stack into Raspi-5 type hardware with on-device inference so the device requires only power plug and wifi.

