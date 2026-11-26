---
slug: separate-perception-from-control
date: 2026-11-20
status: complete
---

# Separate Perception From Control — 60s Block Fix

## Problem
A single 60-second time gate lived in DetectionEngine, freezing the entire detection loop after detecting any animal. This created blind spots where animals could arrive or depart without being detected.

## Solution
- Move cooldown to ArmController.CloseAsync() only (control problem, not perception)
- Remove time gate from DetectionEngine entirely (detection is always needed)
- Delete hardcoded Thread.Sleep(60000) in Program.cs Path C

## Changes
1. ArmController.cs — add s_lastCloseUtc + CloseCooldownSeconds; CloseAsync checks and returns early if within window
2. DetectionEngine.cs — remove entire time gate block (lines 48-55)
3. Program.cs — replace Thread.Sleep(60000) with brief settle wait (~2s); detection loop resumes naturally

## Verification
- Build succeeds (0 errors)
- Tests: all pass (detection + camera)
