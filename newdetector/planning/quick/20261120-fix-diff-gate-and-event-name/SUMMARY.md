---
slug: fix-diff-gate-event-name
date: 2026-11-20
status: complete
summary: Fixed diff gate that only worked when lastState==none, plus added animal names to unknown-species event filenames from Ollama detection.
---

# Summary: Fix Diff Gate + Unknown-Animal Event Names

## Bug 1 — Diff Gate Always Failing After Animal Detection

**Symptom:** All images going to Ollama, even with diff < 2%.  
**Root cause:** `DetectionEngine.cs` line 40 had `&& lastState == "none"` guard. After detecting "cat", lastState="cat", so the condition was always false → diff gate never fired for any animal state.

**Fix:** Removed `lastState == "none"` guard from the diff check. Now the diff gate fires first regardless of previous state:
```csharp
// Before: only active when nothing was detected before
if (diff < threshold && lastState == "none")

// After: always active — skip if scene hasn't changed enough to warrant Ollama  
if (diff < threshold) → return last known state as skipped
```

This works because: after detecting a good animal, the baseline is updated to that scene. If the same scene persists (cat still there), diff will be low (<2%) and we skip with the previously-known state. No unnecessary Ollama calls.

**Verified:** 8 detection tests + 6 camera tests pass.

## Bug 2 — Generic Unknown-Species Event Filename

**Symptom:** All unknown-species events saved as `"what animal_YYYYMMDD_HH_mm_ss"`.  
**Fix:** Use actual detected species names from `result.Animals` / `detectedAnimals`:
```csharp
// Before: generic name
EventManager.SaveEventFile($"what animal {DateTime.Now:yyyyMMdd_HH_mm_ss}");

// After: includes actual animal names
var animalTags = string.Join("_", detectedAnimals);
EventManager.SaveEventFile($"unknown_animal_{animalTags}_{DateTime.Now:yyyyMMdd_HH_mm_ss}");
```

So if Ollama detects "zebra", filename becomes `unknown_animal_zebra_20261120_123456`.

## Verification
- Build: ✅ 0 errors
- Detection tests: ✅ 8/8 pass
- Camera tests: ✅ 6/6 pass (total 14)
- Behavioral: ⏳ needs live testing with unknown-species scenario
