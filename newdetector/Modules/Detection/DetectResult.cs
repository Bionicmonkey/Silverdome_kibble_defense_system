namespace NewDetector.Detection;

/// <summary>
/// Result of a detection analysis pass. Contains the list of detected animals (or ["none"])
/// and whether the pass was skipped due to gating logic.
/// </summary>
public record DetectResult(
    IReadOnlyList<string> Animals,
    bool WasSkipped = false);
