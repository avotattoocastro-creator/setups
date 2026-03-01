using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AvoPerformanceSetupAI.Services.Agent;

/// <summary>
/// Typed HTTP client for the AVO Performance remote Agent.
/// All methods throw <see cref="AgentException"/> on network / HTTP errors so
/// callers only need to catch one exception type.
/// </summary>
public sealed class AgentApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string     _baseUrl;

    public AgentApiClient(string host, int port, string token)
    {
        _baseUrl = $"http://{host}:{port}";
        _http    = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Add("X-API-TOKEN", token);
    }

    // ── Connectivity ──────────────────────────────────────────────────────────

    /// <summary>Returns true if the agent responds to a HEAD /api/reference/root within 10 s.</summary>
    public async Task<bool> PingAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, $"{_baseUrl}/api/reference/root");
            using var res = await _http.SendAsync(req);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ── Reference library ─────────────────────────────────────────────────────

    /// <summary>GET /api/reference/root — returns the configured root folder path.</summary>
    public Task<ReferenceRootResponse> GetReferenceRootAsync()
        => GetAsync<ReferenceRootResponse>("/api/reference/root");

    /// <summary>POST /api/admin/referenceRoot/browse — agent opens a folder picker on the sim PC.</summary>
    public Task<BrowseRootResponse> BrowseReferenceRootAsync()
        => PostAsync<BrowseRootResponse>("/api/admin/referenceRoot/browse", null);

    /// <summary>GET /api/reference/cars — list of car folder names.</summary>
    public Task<List<string>> GetCarsAsync()
        => GetStringListFromEndpointAsync("/api/reference/cars");

    /// <summary>GET /api/reference/tracks?car=... — list of track folder names for a car.</summary>
    public Task<List<string>> GetTracksAsync(string car)
        => GetStringListFromEndpointAsync($"/api/reference/tracks?car={Uri.EscapeDataString(car)}");

    /// <summary>GET /api/reference/setups?car=...&amp;track=... — list of setup files.</summary>
    public async Task<List<SetupItem>> GetSetupsAsync(string car, string track)
    {
        var files = await GetStringListFromEndpointAsync(
            $"/api/reference/setups?car={Uri.EscapeDataString(car)}&track={Uri.EscapeDataString(track)}");
        return files
            .Select(f => new SetupItem { FileName = f, Car = car, Track = track })
            .ToList();
    }

    /// <summary>GET /api/reference/setup/read — returns raw INI text of the setup.</summary>
    public Task<string> ReadSetupAsync(string car, string track, string fileName)
        => GetStringAsync(
            $"/api/reference/setup/read?car={Uri.EscapeDataString(car)}" +
            $"&track={Uri.EscapeDataString(track)}" +
            $"&file={Uri.EscapeDataString(fileName)}");

    // ── Save / apply ──────────────────────────────────────────────────────────

    /// <summary>POST /api/setup/save — persists a generated setup on the simulator PC.</summary>
    public Task<SaveResult> SaveSetupAsync(
        string car, string track, string fileName, string setupText, bool overwrite = true)
        => PostAsync<SaveResult>("/api/setup/save", new SaveSetupRequest
        {
            Car       = car,
            Track     = track,
            FileName  = fileName,
            SetupText = setupText,
            Overwrite = overwrite,
        });

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<T> GetAsync<T>(string path)
    {
        try
        {
            var res = await _http.GetAsync(_baseUrl + path);
            await EnsureSuccessAsync(res);
            var result = await res.Content.ReadFromJsonAsync<T>(JsonOpts);
            return result ?? throw new AgentException("Empty response from agent.");
        }
        catch (AgentException) { throw; }
        catch (Exception ex)   { throw new AgentException($"Agent no accesible: {ex.Message}", ex); }
    }

    private async Task<string> GetStringAsync(string path)
    {
        try
        {
            var res = await _http.GetAsync(_baseUrl + path);
            await EnsureSuccessAsync(res);
            return await res.Content.ReadAsStringAsync();
        }
        catch (AgentException) { throw; }
        catch (Exception ex)   { throw new AgentException($"Agent no accesible: {ex.Message}", ex); }
    }

    /// <summary>
    /// GET <paramref name="path"/> and return the body as a string list,
    /// tolerating both raw JSON arrays and wrapped objects.
    /// </summary>
    private async Task<List<string>> GetStringListFromEndpointAsync(string path)
    {
        try
        {
            var res = await _http.GetAsync(_baseUrl + path);
            await EnsureSuccessAsync(res);
            return await ReadStringListAsync(res);
        }
        catch (AgentException) { throw; }
        catch (Exception ex)
        {
            throw new AgentException($"Agent no accesible en {_baseUrl}{path}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Deserializes an HTTP response into a <c>List&lt;string&gt;</c>.
    /// Accepts both a plain JSON array (<c>["a","b"]</c>) and a wrapped object
    /// (<c>{{ "cars":[...] }}</c>, <c>{{ "tracks":[...] }}</c>,
    ///  <c>{{ "setups":[...] }}</c>, <c>{{ "data":[...] }}</c>).
    /// Logs and rethrows a detailed <see cref="AgentException"/> when the format
    /// is not recognised.
    /// </summary>
    private static async Task<List<string>> ReadStringListAsync(HttpResponseMessage response)
    {
        var rawJson = await response.Content.ReadAsStringAsync();

        // 1) Try direct array deserialization
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(rawJson, JsonOpts);
            if (list is not null) return list;
        }
        catch (JsonException) { /* fall through */ }

        // 2) Try wrapped object: { "cars":[...] }, { "tracks":[...] }, { "setups":[...] }, { "data":[...] }
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "cars", "tracks", "setups", "data", "items", "results" })
                {
                    if (doc.RootElement.TryGetProperty(key, out var prop) &&
                        prop.ValueKind == JsonValueKind.Array)
                    {
                        var result = prop.EnumerateArray()
                                        .Where(e => e.ValueKind == JsonValueKind.String)
                                        .Select(e => e.GetString()!)
                                        .ToList();
                        AvoPerformanceSetupAI.Services.AppLogger.Instance.Warn(
                            $"Agent devolvió lista envuelta en propiedad '{key}'. " +
                            $"Considera actualizar el Agent para devolver un array plano.");
                        return result;
                    }
                }
            }
        }
        catch (JsonException) { /* fall through to detailed error */ }

        // 3) Could not parse — log raw content and throw
        var preview = rawJson.Length > 300 ? rawJson[..300] + "…" : rawJson;
        AvoPerformanceSetupAI.Services.AppLogger.Instance.Error(
            $"Error de deserialización JSON del Agent. JSON recibido: {preview}");
        throw new AgentException(
            $"Respuesta JSON no reconocida del Agent. " +
            $"Se esperaba array de strings o objeto con propiedad 'cars'/'tracks'/'setups'/'data'. " +
            $"JSON: {preview}");
    }

    private async Task<T> PostAsync<T>(string path, object? body)
    {
        try
        {
            using var req  = body is null
                ? new HttpRequestMessage(HttpMethod.Post, _baseUrl + path)
                  { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") }
                : new HttpRequestMessage(HttpMethod.Post, _baseUrl + path)
                  { Content = JsonContent.Create(body, options: JsonOpts) };

            var res = await _http.SendAsync(req);
            await EnsureSuccessAsync(res);
            var result = await res.Content.ReadFromJsonAsync<T>(JsonOpts);
            return result ?? throw new AgentException("Empty response from agent.");
        }
        catch (AgentException) { throw; }
        catch (Exception ex)   { throw new AgentException($"Agent no accesible: {ex.Message}", ex); }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode) return;
        var body = await res.Content.ReadAsStringAsync();
        throw res.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => new AgentException("Token inválido (401)."),
            System.Net.HttpStatusCode.NotFound     => new AgentException("Endpoint no encontrado (404)."),
            _ => new AgentException($"Error HTTP {(int)res.StatusCode}: {body}")
        };
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Typed exception thrown by <see cref="AgentApiClient"/> on every error path.</summary>
public sealed class AgentException : Exception
{
    public AgentException(string message) : base(message) { }
    public AgentException(string message, Exception inner) : base(message, inner) { }
}
