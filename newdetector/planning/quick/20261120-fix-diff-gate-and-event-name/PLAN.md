---
slug: fix-diff-gate-event-name
date: 2026-11-20
status: complete
---

# Fix Diff Gate + Unknown-Animal Event Names

## Bug 1 — Diff gate not firing after animal detection
`DetectionEngine.cs` line 40 had `&& lastState == "none"` guard that prevented the diff gate from working when any animal was previously detected. Remove guard so diff check always fires first.

## Bug 2 — Generic unknown-species event filename
`Program.cs` line 183 used `"what animal {timestamp}"`. Replace with actual species names from `detectedAnimals` array.

## Files to modify
1. `Modules/Detection/DetectionEngine.cs` — remove lastState guard from diff gate, clean orphaned dead code block
2. `Program.cs` — use detectedAnimals in event filename
