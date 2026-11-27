using System.Text.Json;
using Integrator.Protobuf;

namespace Integrator.Telemetry;

/// <summary>
/// The pump. When running and the game is up, it pulls a frame, decodes it to named
/// JSON and POSTs it every interval. Exposes granular status for the window/tray.
/// Every step is wrapped so nothing escapes to the UI.
/// </summary>
internal sealed class Worker : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly TimeSpan IdlePoll = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SchemaRetry = TimeSpan.FromSeconds(30);

    private readonly AppConfig _cfg;
    private readonly GameClient _game;
    private readonly Uploader _uploader;

    private FrameDecoder? _decoder;
    private Schema? _schema;
    private string _rootType = "";
    private DateTime _schemaRetryAt = DateTime.MinValue;


    private CancellationTokenSource? _cts;
    private Task? _loop;

    // ---- status (read by the window/tray) ----
    public string Phase { get; private set; } = "stopped";
    public bool Paused { get; private set; }
    public bool GameRunning { get; private set; }
    public bool TelemetryUp { get; private set; }         // gRPC pull succeeded recently
    public bool SchemaLoaded => _decoder != null;
    public bool UploadUp { get; private set; }            // last upload succeeded
    public DateTime? LastOkUtc { get; private set; }
    public string? LastError { get; private set; }
    public long Sent { get; private set; }
    public string? LastRawJson { get; private set; }       // decoded frame (what the game gave)
    public string? LastProcessedJson { get; private set; } // the envelope that gets uploaded
    public Dictionary<string, object?>? LastFrame { get; private set; } // decoded frame object (for the local server)

    public Worker(AppConfig cfg)
    {
        _cfg = cfg;
        _game = new GameClient(cfg.Source);
        _uploader = new Uploader(cfg.Url, cfg.UrlSslVerify, cfg.EffectiveToken);
    }

    public void Start(bool paused = false)
    {
        Paused = paused;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Pause() { Paused = true; Phase = "stopped"; }
    public void Resume() { Paused = false; }

    private async Task RunAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_cfg.IntervalSeconds);
        while (!ct.IsCancellationRequested)
        {
            TimeSpan wait = interval;
            try
            {
                GameRunning = GameProcess.IsRunning();

                if (Paused)
                {
                    Phase = "stopped";
                    wait = IdlePoll;
                }
                else if (!GameRunning)
                {
                    Phase = "waiting for game";
                    TelemetryUp = false;
                    wait = IdlePoll;
                }
                else if (!_cfg.SendData && !_cfg.LocalServerEnabled && !_cfg.SaveToFile)
                {
                    Phase = "idle (all destinations off)";
                    wait = IdlePoll;
                }
                else if (!EnsureSchema())
                {
                    wait = interval;
                }
                else
                {
                    var (_, processed) = await CaptureAsync(ct);   // sets TelemetryUp + LastFrame
                    if (_cfg.SaveToFile) WriteFile(processed);
                    if (_cfg.SendData)
                    {
                        await _uploader.PostAsync(processed, ct);
                        UploadUp = true;
                        LastOkUtc = DateTime.UtcNow;
                        Sent++;
                        Phase = "uploading";
                    }
                    else
                    {
                        Phase = _cfg.SaveToFile ? "saving to file" : "serving locally";
                    }
                    LastError = null;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                LastError = Describe(e);
                // distinguish a game-side failure from an upload-side one for the indicators
                if (e is IOException || (e is HttpRequestException && _lastStage == Stage.Pull)) TelemetryUp = false;
                else UploadUp = false;
            }

            try { await Task.Delay(wait, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private enum Stage { Pull, Upload }
    private Stage _lastStage = Stage.Pull;

    /// <summary>Pull one frame, decode it, and build the upload envelope. Returns (raw, processed).</summary>
    public async Task<(string raw, string processed)> CaptureAsync(CancellationToken ct)
    {
        if (_decoder == null && !EnsureSchema())
            throw new InvalidOperationException(LastError ?? "schema not loaded");

        _lastStage = Stage.Pull;
        byte[] frame = await _game.PullFrameAsync(ct);
        Dictionary<string, object?> decoded = _decoder!.Decode(frame, _rootType);
        TelemetryUp = true;
        LastFrame = decoded;

        // RAW = schema-less decode (field numbers); POLISHED = named decode above.
        string raw = JsonSerializer.Serialize(RawDecoder.Decode(frame), JsonOpts);
        var envelope = new Dictionary<string, object?>
        {
            ["capturedAt"] = DateTime.UtcNow.ToString("o"),
            ["source"] = _cfg.Source,
            ["telemetry"] = decoded,
        };
        string processed = JsonSerializer.Serialize(envelope, JsonOpts);
        LastRawJson = raw;
        LastProcessedJson = processed;
        _lastStage = Stage.Upload;
        return (raw, processed);
    }

    /// <summary>Pull one frame and return just the upload JSON (used by console mode).</summary>
    public async Task<string> BuildJsonAsync(CancellationToken ct) => (await CaptureAsync(ct)).processed;

    /// <summary>Write the latest JSON to the configured file (overwrites). Errors are reported, not thrown.</summary>
    private void WriteFile(string json)
    {
        string p = _cfg.FilePath;
        if (string.IsNullOrWhiteSpace(p)) return;
        try
        {
            string? dir = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(p, json);
        }
        catch (Exception e) { LastError = "file: " + e.Message; Log.Write("file", e); }
    }

    private bool EnsureSchema()
    {
        if (_decoder != null) return true;
        if (DateTime.UtcNow < _schemaRetryAt) return false;
        try
        {
            Schema schema = AppCore.LoadSchema(_cfg);
            _schema = schema;
            _decoder = new FrameDecoder(schema);
            _rootType = AppCore.RootType(schema);
            return true;
        }
        catch (Exception e)
        {
            Phase = "schema error";
            LastError = "schema: " + e.Message;
            _schemaRetryAt = DateTime.UtcNow + SchemaRetry;
            return false;
        }
    }

    /// <summary>
    /// Call ListTriggerableActions and return the decoded response as a JSON string.
    /// The game must be running. Requires schema (same as PullFrameAsync).
    /// </summary>
    public async Task<string> ListActionsJsonAsync(CancellationToken ct)
    {
        if (_decoder == null && !EnsureSchema())
            throw new InvalidOperationException(LastError ?? "schema not loaded");
        byte[] raw = await _game.ListActionsRawAsync(ct);
        string typeName = AppCore.ListActionsResponseType(_schema!);
        var decoded = _decoder!.Decode(raw, typeName);
        return JsonSerializer.Serialize(decoded, JsonOpts);
    }

    /// <summary>
    /// Call TriggerGameAction and return the status string (OK, FILTERED, MAP_INACTIVE, FAILED).
    /// The game must be running and trigger actions must be enabled in user.cfg.
    /// </summary>
    public async Task<string> TriggerActionAsync(
        string actionName, int holdMs, CancellationToken ct,
        string source = "UI", string? requestBody = null)
    {
        if (_decoder == null && !EnsureSchema())
            throw new InvalidOperationException(LastError ?? "schema not loaded");
        try
        {
            byte[] raw = await _game.TriggerActionRawAsync(actionName, holdMs, ct);
            string typeName = AppCore.TriggerResponseType(_schema!);
            var decoded = _decoder!.Decode(raw, typeName);
            string status = decoded.TryGetValue("status", out var s) ? s?.ToString() ?? "OK" : "OK";
            AppendLog(new LogEntry(DateTime.Now, source, requestBody, status, "{\"status\":\"" + status + "\"}"));
            return status;
        }
        catch (Exception e)
        {
            AppendLog(new LogEntry(DateTime.Now, source, requestBody, "ERROR", Describe(e)));
            throw;
        }
    }

    /// <summary>
    /// Pull one frame on demand (independent of the interval loop), log it, and return the
    /// processed JSON. Useful for external tools that want a fresh snapshot without polling.
    /// </summary>
    public async Task<string> ForcePullAsync(CancellationToken ct, string source = "UI pull")
    {
        if (_decoder == null && !EnsureSchema())
            throw new InvalidOperationException(LastError ?? "schema not loaded");
        try
        {
            var (_, processed) = await CaptureAsync(ct);
            AppendLog(new LogEntry(DateTime.Now, source, null, "OK", processed));
            return processed;
        }
        catch (Exception e)
        {
            AppendLog(new LogEntry(DateTime.Now, source, null, "ERROR", Describe(e)));
            throw;
        }
    }

    private void AppendLog(LogEntry entry)
    {
        if (!_cfg.EventLogEnabled) return;
        try
        {
            string path = _cfg.EffectiveLogPath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            if (File.Exists(path) && new FileInfo(path).Length > _cfg.EventLogMaxMb * 1024L * 1024L)
            {
                string bak = path + ".1";
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(path, bak);
            }

            File.AppendAllText(path, BuildLogLine(entry) + "\n");
        }
        catch { }
    }

    private static string BuildLogLine(LogEntry e)
    {
        var d = new Dictionary<string, string?> { ["ts"] = e.Timestamp.ToString("o"), ["source"] = e.Source };
        if (e.RequestBody != null) d["req"] = e.RequestBody;
        d["status"] = e.Status;
        if (e.ResponseBody != null) d["res"] = e.ResponseBody;
        return JsonSerializer.Serialize(d);
    }

    private static string Describe(Exception e)
    {
        while (e.InnerException != null && (e is AggregateException || e is HttpRequestException))
            e = e.InnerException;
        return e.Message;
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _loop?.Wait(TimeSpan.FromSeconds(3)); } catch { }
        _cts?.Dispose();
        _game.Dispose();
        _uploader.Dispose();
    }
}
