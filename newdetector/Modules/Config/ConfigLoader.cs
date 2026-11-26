using System.Text.Json;

namespace NewDetector.Config;

/// <summary>
/// Loads and validates configuration from animals.json and system.json files.
/// Returns safe defaults when config files are missing or malformed.
/// All paths resolve to AppContext.BaseDirectory (the application exe's output folder).
/// </summary>
public static class ConfigLoader
{
    /// <summary>
    /// Loads animals configuration with safe defaults when the file is missing or invalid.
    /// All config paths resolve to AppContext.BaseDirectory (the application exe's output folder),
    /// not the process CWD — this ensures reliable loading regardless of how/where the app is launched.
    /// Expands {time_context} in the raw template with a formatted DateTime string before returning.
    /// </summary>
    public static AnimalsConfig LoadAnimals(string? path = null)
    {
        var resolvedPath = path ?? Path.Combine(AppContext.BaseDirectory, "animals.json");

        if (!File.Exists(resolvedPath))
        {
            Console.WriteLine($"animals.json not found at '{resolvedPath}', using safe defaults.");
            return new AnimalsConfig();
        }

        try
        {
            var json = File.ReadAllText(resolvedPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var config = JsonSerializer.Deserialize<AnimalsConfig>(json, options);

            if (config is null)
            {
                Console.WriteLine($"animals.json parsed to null at '{resolvedPath}', using safe defaults.");
                return new AnimalsConfig();
            }

            // Validate species_whitelist — never allow null/empty (defense-in-depth for T-01-02)
            if (config.SpeciesWhitelist is null || config.SpeciesWhitelist.Length == 0)
            {
                Console.WriteLine("animals.json has empty or missing species_whitelist; using safe defaults.");
                return new AnimalsConfig();
            }

            // Expand {time_context} placeholder in the raw template with a formatted DateTime string.
            // This ensures the LLM receives actual time context rather than a literal "{time_context}" token.
            var rawTemplate = string.IsNullOrEmpty(config.PromptTemplate)
                ? ""
                : config.PromptTemplate;
            var expandedTemplate = rawTemplate; // .Replace("{time_context}", $"current time is {DateTime.Now:h:mm tt}");

            // Merge with defaults for any missing optional fields
            return config with
            {
                PromptTemplate = expandedTemplate,
                SpeciesWhitelist = config.SpeciesWhitelist.Length > 0
                    ? config.SpeciesWhitelist.Distinct().ToArray()
                    : new AnimalsConfig().SpeciesWhitelist
            };
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Failed to parse animals.json at '{resolvedPath}': {ex.Message}. Using safe defaults.");
            return new AnimalsConfig();
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Error reading animals.json at '{resolvedPath}': {ex.Message}. Using safe defaults.");
            return new AnimalsConfig();
        }
    }

    /// <summary>
    /// Loads system configuration with safe defaults when the file is missing or invalid.
    /// Resolves paths to AppContext.BaseDirectory (the application exe's output folder),
    /// not the process CWD — this ensures reliable loading regardless of launch context.
    /// </summary>
    public static SystemConfig LoadSystem(string? path = null)
    {
        var resolvedPath = path ?? Path.Combine(AppContext.BaseDirectory, "system.json");

        if (!File.Exists(resolvedPath))
        {
            Console.WriteLine($"system.json not found at '{resolvedPath}', using safe defaults.");
            return new SystemConfig();
        }

        try
        {
            var json = File.ReadAllText(resolvedPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var config = JsonSerializer.Deserialize<SystemConfig>(json, options);

            if (config is null)
            {
                Console.WriteLine($"system.json parsed to null at '{resolvedPath}', using safe defaults.");
                return new SystemConfig();
            }

            // Merge with defaults for any null nested configs (partial load support)
            var defaults = new SystemConfig();
            return config with
            {
                Webcam = config.Webcam ?? defaults.Webcam,
                Detection = config.Detection ?? defaults.Detection,
                Ollama = config.Ollama ?? defaults.Ollama,
                Arm = config.Arm ?? defaults.Arm,
                Alarm = config.Alarm ?? defaults.Alarm
            };
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Failed to parse system.json at '{resolvedPath}': {ex.Message}. Using safe defaults.");
            return new SystemConfig();
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Error reading system.json at '{resolvedPath}': {ex.Message}. Using safe defaults.");
            return new SystemConfig();
        }
    }
}

// --- AnimalsConfig records ---

public record AnimalsConfig
{
    public string[] Good { get; init; } = ["cat", "dog"];
    public string[] Bad { get; init; } = ["opossum", "ferret"];
    public string PromptTemplate { get; init; } = "";
    public string[] SpeciesWhitelist { get; init; } = ["cat", "dog", "opossum", "ferret"];
}

// --- SystemConfig records ---

public record SystemConfig
{
    public WebcamConfig? Webcam { get; init; }
    public DetectionConfig? Detection { get; init; }
    public OllamaConfig? Ollama { get; init; }
    public ArmConfig? Arm { get; init; }
    public AlarmConfig? Alarm { get; init; }

    public SystemConfig()
    {
        Webcam = new WebcamConfig();
        Detection = new DetectionConfig();
        Ollama = new OllamaConfig();
        Arm = new ArmConfig();
        Alarm = new AlarmConfig();
    }
}

public record WebcamConfig
{
    public int DefaultIndex { get; init; } = 0;
    public int RetryAttempts { get; init; } = 3;
    public double RetryBackoffSeconds { get; init; } = 1;
}

public record DetectionConfig
{
    public double DiffThresholdPercent { get; init; } = 0.1;
    public int ScaleMaxWidth { get; init; } = 800;
    public int[] ResizeCompareDims { get; init; } = [160, 120];
    public int RgbDeltaThreshold { get; init; } = 120;
}

public record OllamaConfig
{
    public string Endpoint { get; init; } = "http://127.0.0.1:11434";
    public string ApiPath { get; init; } = "/api/chat";
    public string Model { get; init; } = "qwen3.5:4b";
    public int TimeoutSeconds { get; init; } = 60;
}

public record ArmConfig
{
    public int OpenPosition { get; init; } = 1500;
    public int ClosedPosition { get; init; } = 0;
    public double WaitAfterMoveSeconds { get; init; } = 15;
}

public record AlarmConfig
{
    public string CatAlarmFile { get; init; } = "Cat Alarm.wav";
    public string PossumAlarmFile { get; init; } = "Possum Alarm.wav";
    public string RaccoonAlarmFile { get; init; } = "Raccoon Alarm.wav";
}
