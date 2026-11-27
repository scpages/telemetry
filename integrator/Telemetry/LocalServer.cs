using System.Net;
using System.Text;
using System.Text.Json;

namespace Integrator.Telemetry;

/// <summary>
/// A tiny no-auth HTTP server that exposes telemetry data and game actions.
///
/// Routes:
///   GET  /                    → JSON index of available endpoints
///   GET  /api/v1/telemetry    → latest cached frame; add ?path=a.b.c for one value (missing → "-")
///   POST /api/v1/telemetry    → pull a fresh frame from the game right now; returns the new frame
///   GET  /api/v1/actions      → ListTriggerableActions response (requires game running + schema)
///   POST /api/v1/action       → TriggerGameAction; body: { "action": "v_deploy_landing_system", "hold_ms": 0 }
///                               response: { "status": "OK" }
///
/// Pure BCL (HttpListener). Bind address and port are configurable.
/// </summary>
internal sealed class LocalServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private const string BasePath = "/api/v1";

    private readonly Func<Dictionary<string, object?>?> _frameProvider;
    private readonly Func<CancellationToken, Task<string>>? _actionsProvider;
    private readonly Func<string, int, CancellationToken, Task<string>>? _triggerAction;
    private readonly Func<CancellationToken, Task<string>>? _forcePull;

    private HttpListener? _listener;
    private Thread? _thread;

    public string? Error { get; private set; }
    public string Prefix { get; private set; } = "";
    public bool Running => _listener?.IsListening == true;

    public LocalServer(
        Func<Dictionary<string, object?>?> frameProvider,
        Func<CancellationToken, Task<string>>? actionsProvider = null,
        Func<string, int, CancellationToken, Task<string>>? triggerAction = null,
        Func<CancellationToken, Task<string>>? forcePull = null)
    {
        _frameProvider = frameProvider;
        _actionsProvider = actionsProvider;
        _triggerAction = triggerAction;
        _forcePull = forcePull;
    }

    public void Start(string bind, int port)
    {
        Stop();
        Error = null;
        try
        {
            string host = string.IsNullOrWhiteSpace(bind) || bind == "0.0.0.0" || bind == "*" ? "+" : bind;
            Prefix = $"http://{host}:{port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(Prefix);
            _listener.Start();
            _thread = new Thread(Loop) { IsBackground = true, Name = "local-http" };
            _thread.Start();
        }
        catch (Exception e)
        {
            Error = e.Message;
            _listener = null;
        }
    }

    private void Loop()
    {
        var listener = _listener;
        while (listener != null && listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = listener.GetContext(); }
            catch { break; }
            try { Handle(ctx); } catch { /* never let one request kill the loop */ }
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        res.AddHeader("Access-Control-Allow-Origin", "*");
        res.ContentType = "application/json; charset=utf-8";

        string path = req.Url?.AbsolutePath ?? "/";
        string method = req.HttpMethod;

        if (path == "/" && method == "GET")
        {
            HandleIndex(res);
            return;
        }
        if (path == BasePath + "/telemetry" && method == "GET")
        {
            HandleTelemetry(req, res);
            return;
        }
        if (path == BasePath + "/telemetry" && method == "POST")
        {
            HandleForcePull(res);
            return;
        }
        if (path == BasePath + "/actions" && method == "GET")
        {
            HandleActions(res);
            return;
        }
        if (path == BasePath + "/action" && method == "POST")
        {
            HandleTrigger(req, res);
            return;
        }

        Write(res, 404, "{\"error\":\"not found\"}");
    }

    private static void HandleIndex(HttpListenerResponse res)
    {
        var index = new
        {
            endpoints = new[]
            {
                new { method = "GET",  path = BasePath + "/telemetry", description = "Latest cached telemetry frame. Add ?path=a.b.c for a single value." },
                new { method = "POST", path = BasePath + "/telemetry", description = "Pull a fresh frame from the game right now; returns the new frame." },
                new { method = "GET",  path = BasePath + "/actions",   description = "List all triggerable game actions (requires game running)." },
                new { method = "POST", path = BasePath + "/action",    description = "Trigger a game action. Body: { \"action\": \"v_deploy_landing_system\", \"hold_ms\": 0 }. Returns: { \"status\": \"OK\" }." },
            }
        };
        Write(res, 200, JsonSerializer.Serialize(index, JsonOpts));
    }

    private void HandleTelemetry(HttpListenerRequest req, HttpListenerResponse res)
    {
        Dictionary<string, object?>? frame = _frameProvider();
        string? valuePath = req.QueryString["path"];
        object? payload = frame == null ? null
            : string.IsNullOrEmpty(valuePath) ? frame
            : JsonPath.Get(frame, valuePath);
        Write(res, 200, JsonSerializer.Serialize(payload, JsonOpts));
    }

    private void HandleForcePull(HttpListenerResponse res)
    {
        if (_forcePull == null) { Write(res, 501, "{\"error\":\"force pull not available\"}"); return; }
        try
        {
            string json = _forcePull(CancellationToken.None).GetAwaiter().GetResult();
            Write(res, 200, json);
        }
        catch (Exception e) { Write(res, 500, JsonSerializer.Serialize(new { error = e.Message })); }
    }

    private void HandleActions(HttpListenerResponse res)
    {
        if (_actionsProvider == null) { Write(res, 501, "{\"error\":\"actions not available\"}"); return; }
        try
        {
            string json = _actionsProvider(CancellationToken.None).GetAwaiter().GetResult();
            Write(res, 200, json);
        }
        catch (Exception e) { Write(res, 500, JsonSerializer.Serialize(new { error = e.Message })); }
    }

    private void HandleTrigger(HttpListenerRequest req, HttpListenerResponse res)
    {
        if (_triggerAction == null) { Write(res, 501, "{\"error\":\"trigger not available\"}"); return; }

        string body;
        using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            body = reader.ReadToEnd();

        string? actionName = null;
        int holdMs = 0;
        try
        {
            var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (doc.RootElement.TryGetProperty("action", out var a)) actionName = a.GetString();
            if (doc.RootElement.TryGetProperty("hold_ms", out var h)) holdMs = h.GetInt32();
        }
        catch { }

        if (string.IsNullOrEmpty(actionName)) { Write(res, 400, "{\"error\":\"missing \\\"action\\\" field\"}"); return; }

        try
        {
            string status = _triggerAction(actionName, holdMs, CancellationToken.None).GetAwaiter().GetResult();
            Write(res, 200, JsonSerializer.Serialize(new { status }));
        }
        catch (Exception e) { Write(res, 500, JsonSerializer.Serialize(new { error = e.Message })); }
    }

    private static void Write(HttpListenerResponse res, int statusCode, string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        res.StatusCode = statusCode;
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes, 0, bytes.Length);
        res.OutputStream.Close();
    }

    public void Stop()
    {
        try { _listener?.Stop(); _listener?.Close(); } catch { }
        _listener = null;
    }

    public void Dispose() => Stop();
}
