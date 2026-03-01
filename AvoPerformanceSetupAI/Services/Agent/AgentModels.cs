namespace AvoPerformanceSetupAI.Services.Agent;

// ── DTOs returned / sent by AgentApiClient ────────────────────────────────────

/// <summary>Response from GET /api/reference/root</summary>
public sealed class ReferenceRootResponse
{
    public string Path { get; set; } = string.Empty;
}

/// <summary>Response from POST /api/admin/referenceRoot/browse (returns chosen path)</summary>
public sealed class BrowseRootResponse
{
    public string Path { get; set; } = string.Empty;
}

/// <summary>Single setup file entry returned by GET /api/reference/setups</summary>
public sealed class SetupItem
{
    public string FileName  { get; set; } = string.Empty;
    public string Car       { get; set; } = string.Empty;
    public string Track     { get; set; } = string.Empty;
}

/// <summary>Body for POST /api/setup/save</summary>
public sealed class SaveSetupRequest
{
    public string Car       { get; set; } = string.Empty;
    public string Track     { get; set; } = string.Empty;
    public string FileName  { get; set; } = string.Empty;
    public string SetupText { get; set; } = string.Empty;
    public bool   Overwrite { get; set; } = true;
}

/// <summary>Response from POST /api/setup/save</summary>
public sealed class SaveResult
{
    public bool   Success { get; set; }
    public string Path    { get; set; } = string.Empty;
    public string Error   { get; set; } = string.Empty;
}
