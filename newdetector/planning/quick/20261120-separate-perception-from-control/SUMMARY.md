---
slug: separate-perception-from-control
date: 2026-11-20
status: complete
summary: Moved close cooldown to ArmController.CloseAsync(), removed time gate from DetectionEngine, eliminated hardcoded 60s Thread.Sleep in Program.cs. Detection now runs continuously; control rate-limited independently.
---

# Summary: Separate Perception From Control

## Problem
DetectionEngine had a single 60-second time gate that blocked ALL re-detection after any animal was detected. Combined with Program.cs Path C's `Thread.Sleep(60000)`, the entire loop could freeze — detection couldn't see if animals departed or new ones arrived during that window.

## Root Cause
The 60s block conflated two separate concerns:
- **Perception** (detect what's in the frame) — should always run
- **Control** (move the arm physically) — should be rate-limited to prevent opossum rapid-cycling

## Solution Applied

### 1. ArmController.cs — Close cooldown (control)
Added `s_lastCloseUtc` and `CloseCooldownSeconds = 60`. `CloseAsync()` checks if within window and returns early with a log message. Key behaviors:
- Cooldown starts from the first close, not reset by subsequent calls
- No cooldown on `OpenAsync()` — always respond immediately to good animals
- After 60s, next close is allowed and starts a new window

### 2. DetectionEngine.cs — Removed time gate (perception)
Deleted the entire time gate block (previously lines 48-55). Now only the diff gate remains:
```csharp
// BEFORE: both gates in sequence — second was a hard wall
if (diff < threshold) → skip
if ((now - lastCall) < 60s && lastState != "none") → skip ← REMOVED

// AFTER: only diff gate — continuous detection
if (diff < threshold) → skip with last known state
```

### 3. Program.cs Path C — Removed hardcoded sleep
Replaced `Thread.Sleep(60000)` with `Thread.Sleep(2000)` for arm settle time. The loop resumes naturally after CloseAsync completes (~15s handled inside IssueMoveSequence), and detection continues on the next 100ms tick.

### 4. Test fix — DetectionEngineTests.cs
The `ImageDiff_ConfigurableThreshold_RespectsSetting` test relied on the time gate (SetDetectionState("cat")) for isolation between two Analyze calls. Rewrote to use separate baselines: first call tests low threshold with gray frame vs white baseline; second call tests high threshold with identical frame vs itself (diff=0).

## Behavioral Flow After Fix
| Scenario | Detection | Arm Response |
|----------|-----------|-------------|
| Bad animal → departs within 60s | ✓ Detected on next tick | Cooldown blocks open — food stays closed for up to 60s after close |
| Opossum returns during cooldown | ✓ Detected | CloseAsync blocked (already closed) — no-op with log |
| Good animal arrives | ✓ Detected immediately | Opens arm — no cooldown on opens |
| New bad animal after cooldown expires | ✓ Detected | Closes arm — new 60s window starts |

## Verification
- Build: ✅ 0 errors
- Detection tests: ✅ 8/8 pass
- Camera tests: ✅ 6/6 pass (total 14)
