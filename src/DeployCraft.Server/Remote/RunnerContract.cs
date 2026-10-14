using System.Text.Json.Serialization;

namespace DeployCraft.Server.Remote;

// Miroir de DeployCraft.Runner/Job.cs : job.json et status.json échangés avec le runner.

public class RunnerJob
{
    [JsonPropertyName("steps")] public List<RunnerJobStep> Steps { get; set; } = [];
}

public class RunnerJobStep
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("command")] public string? Command { get; set; }
    [JsonPropertyName("arguments")] public string? Arguments { get; set; }
    [JsonPropertyName("successCodes")] public List<int> SuccessCodes { get; set; } = [0];
    [JsonPropertyName("timeoutSeconds")] public int TimeoutSeconds { get; set; }
    [JsonPropertyName("continueOnError")] public bool ContinueOnError { get; set; }
    /// <summary>Lancer l'étape dans la session de l'utilisateur connecté (le runner tourne en Système).</summary>
    [JsonPropertyName("runAsLoggedOnUser")] public bool RunAsLoggedOnUser { get; set; }
    /// <summary>Mode pull : fichier (UNC) que le runner copie lui-même dans exec avant l'étape.</summary>
    [JsonPropertyName("sourcePath")] public string? SourcePath { get; set; }
    [JsonPropertyName("includeDirectory")] public bool IncludeDirectory { get; set; }
}

public class RunnerStatus
{
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("steps")] public List<RunnerStepResult> Steps { get; set; } = [];
}

public class RunnerStepResult
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("exitCode")] public int? ExitCode { get; set; }
    [JsonPropertyName("commandLine")] public string? CommandLine { get; set; }
    [JsonPropertyName("output")] public string? Output { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("startedUtc")] public DateTime? StartedUtc { get; set; }
    [JsonPropertyName("endedUtc")] public DateTime? EndedUtc { get; set; }
}
