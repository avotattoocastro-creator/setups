using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AvoPerformanceSetupAI.Services.Agent;

/// <summary>
/// Consumes live structured log entries from the Agent over WebSocket.
/// Connect with <see cref="StartAsync"/>, disconnect with <see cref="StopAsync"/>.
/// Each received entry fires <see cref="OnLog"/>.
/// </summary>
public sealed class AgentLogStream : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOpts =
        new(JsonSerializerDefaults.Web);

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _readTask;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Fired on the background receive loop when a valid log entry arrives.</summary>
    public event Action<AgentLogEntry>? OnLog;

    /// <summary>
    /// Opens the WebSocket connection to <paramref name="wsUrl"/> and starts receiving
    /// log entries asynchronously. Safe to call again after <see cref="StopAsync"/>.
    /// </summary>
    public async Task StartAsync(string wsUrl)
    {
        await StopAsync().ConfigureAwait(false);

        _cts = new CancellationTokenSource();
        _ws  = new ClientWebSocket();

        try
        {
            await _ws.ConnectAsync(new Uri(wsUrl), _cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // If the connection fails we clean up silently; the caller can retry.
            _ws.Dispose();
            _ws = null;
            _cts.Dispose();
            _cts = null;
            return;
        }

        _readTask = ReadLoopAsync(_ws, _cts.Token);
    }

    /// <summary>Closes the WebSocket and stops the receive loop.</summary>
    public async Task StopAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        if (_ws is { State: WebSocketState.Open or WebSocketState.CloseReceived or WebSocketState.CloseSent })
        {
            try
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _ws.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, "Stop", closeCts.Token)
                    .ConfigureAwait(false);
            }
            catch { /* ignore errors during graceful close */ }
        }

        if (_readTask is not null)
        {
            try { await _readTask.ConfigureAwait(false); }
            catch { /* already cancelled */ }
            _readTask = null;
        }

        _ws?.Dispose();
        _ws = null;
        _cts?.Dispose();
        _cts = null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Prefer calling <see cref="StopAsync"/> explicitly from async code.
    /// This synchronous overload is provided for <see cref="IDisposable"/> compatibility;
    /// it signals cancellation and does not wait for the receive loop to fully drain.
    /// </remarks>
    public void Dispose()
    {
        _cts?.Cancel();
        _ws?.Dispose();
        _ws  = null;
        _cts?.Dispose();
        _cts = null;
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private async Task ReadLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var sb     = new StringBuilder();

        try
        {
            while (!ct.IsCancellationRequested &&
                   ws.State == WebSocketState.Open)
            {
                sb.Clear();

                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(
                        new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                        return;

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                TryDispatch(sb.ToString());
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch { /* network error — exit silently */ }
    }

    private void TryDispatch(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            var entry = JsonSerializer.Deserialize<AgentLogEntry>(json, _jsonOpts);
            if (entry is not null)
                OnLog?.Invoke(entry);
        }
        catch { /* invalid JSON — ignore */ }
    }
}
