namespace ClickBaitThumbnailGenerator;

public sealed class AppOptions
{
    public OpenAiOptions OpenAI { get; init; } = new();
    public ProcessingOptions Processing { get; init; } = new();
    public GenerationOptions Generation { get; init; } = new();
    public StorageOptions Storage { get; init; } = new();

    public void Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(OpenAI.ImageModel)) errors.Add("OpenAI:ImageModel is required.");
        if (string.IsNullOrWhiteSpace(OpenAI.ScenarioModel)) errors.Add("OpenAI:ScenarioModel is required.");
        if (string.IsNullOrWhiteSpace(OpenAI.VisionModel)) errors.Add("OpenAI:VisionModel is required.");
        if (OpenAI.Concurrency is < 1 or > 20) errors.Add("OpenAI:Concurrency must be between 1 and 20.");
        if (OpenAI.MaximumRetries is < 0 or > 20) errors.Add("OpenAI:MaximumRetries must be between 0 and 20.");
        if (OpenAI.RequestTimeoutSeconds is < 10 or > 900) errors.Add("OpenAI:RequestTimeoutSeconds must be between 10 and 900.");
        if (OpenAI.EstimatedCostPerImageUsd < 0) errors.Add("OpenAI:EstimatedCostPerImageUsd cannot be negative.");
        if (Processing.OutputWidth < 64 || Processing.OutputHeight < 64)
            errors.Add("Processing output dimensions must be at least 64 pixels.");
        if (Processing.OutputWidth * 9 != Processing.OutputHeight * 16)
            errors.Add("Processing output dimensions must have an exact 16:9 ratio.");
        if (Processing.WebPQuality is < 1 or > 100) errors.Add("Processing:WebPQuality must be between 1 and 100.");
        if (Processing.DuplicateHashThreshold is < 0 or > 64)
            errors.Add("Processing:DuplicateHashThreshold must be between 0 and 64.");
        if (Generation.DefaultScenarioCount < 1) errors.Add("Generation:DefaultScenarioCount must be positive.");
        if (Generation.ScenarioBatchSize is < 1 or > 100) errors.Add("Generation:ScenarioBatchSize must be between 1 and 100.");
        if (string.IsNullOrWhiteSpace(Storage.DatabasePath) || string.IsNullOrWhiteSpace(Storage.GeneratedPath) ||
            string.IsNullOrWhiteSpace(Storage.TemporaryPath)) errors.Add("Every Storage path is required.");
        if (errors.Count > 0) throw new ConfigurationException(errors);
    }
}

public sealed class OpenAiOptions
{
    public string ImageModel { get; init; } = "gpt-image-1-mini";
    public string ScenarioModel { get; init; } = "gpt-5-mini";
    public string VisionModel { get; init; } = "gpt-5-mini";
    public string ImageQuality { get; init; } = "medium";
    public string ImageSize { get; init; } = "1536x1024";
    public int Concurrency { get; init; } = 3;
    public int MaximumRetries { get; init; } = 5;
    public int RequestTimeoutSeconds { get; init; } = 180;
    public decimal EstimatedCostPerImageUsd { get; init; } = 0.015m;
}

public sealed class ProcessingOptions
{
    public int OutputWidth { get; init; } = 512;
    public int OutputHeight { get; init; } = 288;
    public int WebPQuality { get; init; } = 80;
    public bool KeepOriginalFiles { get; init; }
    public int DuplicateHashThreshold { get; init; } = 8;
}

public sealed class GenerationOptions
{
    public int DefaultScenarioCount { get; init; } = 2000;
    public bool FamilyFriendly { get; init; } = true;
    public int ScenarioBatchSize { get; init; } = 50;
}

public sealed class StorageOptions
{
    public string DatabasePath { get; init; } = "data/clickbait-thumbnails.db";
    public string GeneratedPath { get; init; } = "generated";
    public string TemporaryPath { get; init; } = "tmp";
}

public sealed class ConfigurationException(IEnumerable<string> errors)
    : Exception("Configuration is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(x => $"- {x}")))
{
    public IReadOnlyList<string> Errors { get; } = errors.ToArray();
}
