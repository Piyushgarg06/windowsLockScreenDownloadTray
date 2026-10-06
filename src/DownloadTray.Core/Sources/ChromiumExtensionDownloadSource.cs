using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using DownloadTray.Core.Models;

namespace DownloadTray.Core.Sources;

/// <summary>
/// Receives download events from the Chromium browser extension via the Native Messaging
/// protocol (4-byte little-endian length prefix + UTF-8 JSON body over stdin/stdout).
///
/// Works with Chrome, Edge, Brave, Opera, Vivaldi, and any other Chromium-based browser
/// that supports the chrome.downloads API and chrome.runtime.connectNative().
///
/// BUILD-SPEC reference: Section 4.2, 4.3, 5.
/// </summary>
public sealed class ChromiumExtensionDownloadSource : IDownloadSource, IDisposable
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------
    private readonly string _browserSlug;
    private readonly Stream _inputStream;
    private readonly Stream _outputStream;
    private readonly ConcurrentDictionary<string, DownloadActivity> _downloads = new();
    private CancellationTokenSource? _cts;

    // -------------------------------------------------------------------------
    // IDownloadSource
    // -------------------------------------------------------------------------
    public string SourceName => $"Chromium Native Messaging ({_browserSlug})";

    public event EventHandler<DownloadActivity>? DownloadChanged;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------
    /// <param name="browserSlug">Human-readable browser slug, e.g. "brave", "chrome", "edge".</param>
    /// <param name="inputStream">Stream to read native messages from (typically Console.OpenStandardInput()).</param>
    /// <param name="outputStream">Stream to write native messages to (typically Console.OpenStandardOutput()).</param>
    public ChromiumExtensionDownloadSource(
        string browserSlug,
        Stream inputStream,
        Stream outputStream)
    {
        _browserSlug = browserSlug;
        _inputStream = inputStream;
        _outputStream = outputStream;
    }

    // -------------------------------------------------------------------------
    // IDownloadSource — GetActiveDownloadsAsync
    // -------------------------------------------------------------------------
    public Task<IReadOnlyList<DownloadActivity>> GetActiveDownloadsAsync()
    {
        IReadOnlyList<DownloadActivity> snapshot = _downloads.Values.ToList();
        return Task.FromResult(snapshot);
    }

    // -------------------------------------------------------------------------
    // IDownloadSource — StartAsync
    // -------------------------------------------------------------------------
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await ReadLoopAsync(_cts.Token);
    }

    // -------------------------------------------------------------------------
    // Native Messaging read loop
    // -------------------------------------------------------------------------
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var lengthBuffer = new byte[4];

        while (!ct.IsCancellationRequested)
        {
            // --- Read 4-byte little-endian message length ---
            int bytesRead = 0;
            try
            {
                bytesRead = await ReadExactAsync(_inputStream, lengthBuffer, 4, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[NativeHost] Fatal read error: {ex.Message}");
                break;
            }

            if (bytesRead == 0) break; // Browser closed the channel.

            int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
            if (messageLength <= 0 || messageLength > 1_048_576) // sanity: max 1 MB
            {
                Console.Error.WriteLine($"[NativeHost] Invalid message length {messageLength}, skipping.");
                continue;
            }

            // --- Read message body ---
            var bodyBuffer = new byte[messageLength];
            try
            {
                await ReadExactAsync(_inputStream, bodyBuffer, messageLength, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[NativeHost] Body read error: {ex.Message}");
                break;
            }

            // --- Parse and dispatch ---
            try
            {
                var json = JsonNode.Parse(bodyBuffer);
                if (json != null) ProcessMessage(json);
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"[NativeHost] JSON parse error: {ex.Message}");
            }
        }

        // Mark all tracked downloads as Unknown when the channel drops (BUILD-SPEC §13).
        MarkAllStaleDueToDisconnect();
    }

    // -------------------------------------------------------------------------
    // Message dispatch
    // -------------------------------------------------------------------------
    private void ProcessMessage(JsonNode msg)
    {
        var type = msg["type"]?.GetValue<string>();

        switch (type)
        {
            case "downloads.onCreated":
            case "downloads.sync": // sent on reconnect with all in-progress downloads
                HandleCreatedOrSync(msg["download"]);
                break;

            case "downloads.onChanged":
                HandleChanged(msg["delta"]);
                break;

            case "downloads.onErased":
                HandleErased(msg["downloadId"]?.GetValue<int>().ToString() ?? string.Empty);
                break;

            default:
                // Ignore unknown message types — forward-compatible.
                break;
        }
    }

    private void HandleCreatedOrSync(JsonNode? d)
    {
        if (d == null) return;
        var activity = ParseDownloadItem(d);
        _downloads[activity.Id] = activity;
        RaiseChanged(activity);
    }

    private void HandleChanged(JsonNode? delta)
    {
        if (delta == null) return;

        var id = delta["id"]?.GetValue<int>().ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(id)) return;

        // Get or create the activity for this download.
        var activity = _downloads.GetOrAdd(id, _ => new DownloadActivity
        {
            Id = id,
            Source = _browserSlug,
            StartedAt = DateTimeOffset.UtcNow,
            State = DownloadState.Starting
        });

        // Apply delta fields (each is {current, previous} — we only care about `current`).
        if (delta["filename"]?["current"]?.GetValue<string>() is { } filename)
            activity.Filename = System.IO.Path.GetFileName(filename);

        if (delta["bytesReceived"]?["current"] is JsonNode brNode)
            activity.DownloadedBytes = brNode.GetValue<long>();

        if (delta["totalBytes"]?["current"] is JsonNode tbNode)
        {
            var tb = tbNode.GetValue<long>();
            activity.TotalBytes = tb > 0 ? tb : null; // -1 means unknown
        }

        if (delta["state"]?["current"]?.GetValue<string>() is { } state)
            activity.State = ParseState(state);

        activity.LastUpdatedAt = DateTimeOffset.UtcNow;
        _downloads[id] = activity;
        RaiseChanged(activity);
    }

    private void HandleErased(string id)
    {
        if (_downloads.TryRemove(id, out var activity))
        {
            activity.State = DownloadState.Unknown;
            activity.LastUpdatedAt = DateTimeOffset.UtcNow;
            RaiseChanged(activity);
        }
    }

    // -------------------------------------------------------------------------
    // Parsing helpers
    // -------------------------------------------------------------------------
    private DownloadActivity ParseDownloadItem(JsonNode d)
    {
        var id = d["id"]?.GetValue<int>().ToString() ?? Guid.NewGuid().ToString();
        var filenameRaw = d["filename"]?.GetValue<string>() ?? string.Empty;
        var bytesReceived = d["bytesReceived"]?.GetValue<long>() ?? 0;
        var totalBytesRaw = d["totalBytes"]?.GetValue<long>() ?? -1;
        var stateStr = d["state"]?.GetValue<string>() ?? "in_progress";

        return new DownloadActivity
        {
            Id = id,
            Filename = System.IO.Path.GetFileName(filenameRaw),
            Source = _browserSlug,
            DownloadedBytes = bytesReceived,
            TotalBytes = totalBytesRaw > 0 ? totalBytesRaw : null,
            State = ParseState(stateStr),
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static DownloadState ParseState(string browserState) => browserState switch
    {
        "in_progress" => DownloadState.Downloading,
        "complete" => DownloadState.Completed,
        "interrupted" => DownloadState.Failed,
        _ => DownloadState.Unknown
    };

    // -------------------------------------------------------------------------
    // Disconnect handling (BUILD-SPEC §13)
    // -------------------------------------------------------------------------
    private void MarkAllStaleDueToDisconnect()
    {
        foreach (var kv in _downloads)
        {
            if (kv.Value.State == DownloadState.Downloading ||
                kv.Value.State == DownloadState.Starting)
            {
                kv.Value.State = DownloadState.Unknown;
                kv.Value.LastUpdatedAt = DateTimeOffset.UtcNow;
                RaiseChanged(kv.Value);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Sending a message back to the extension
    // -------------------------------------------------------------------------
    public async Task SendAsync(JsonNode message, CancellationToken ct = default)
    {
        var json = message.ToJsonString();
        var body = System.Text.Encoding.UTF8.GetBytes(json);
        var header = BitConverter.GetBytes(body.Length);
        await _outputStream.WriteAsync(header, ct);
        await _outputStream.WriteAsync(body, ct);
        await _outputStream.FlushAsync(ct);
    }

    // -------------------------------------------------------------------------
    // Utility
    // -------------------------------------------------------------------------
    private void RaiseChanged(DownloadActivity activity) =>
        DownloadChanged?.Invoke(this, activity);

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer, totalRead, count - totalRead, ct);
            if (read == 0) return totalRead; // EOF
            totalRead += read;
        }
        return totalRead;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
