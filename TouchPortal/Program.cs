using System.Net.Sockets;
using System.Text;
using System.Text.Json;

const int    Version  = 12;                         // bump on every change; keep in sync with entry.tp "version"
const string PluginId = Settings.Id;

var settings = new Settings();                      // live config, updated from TP
using var cts = new CancellationTokenSource();
var log = new Log(Path.Combine(AppContext.BaseDirectory, "JsonHttpFetcher.log"));
log.Write($"--- JsonHttpFetcher v{Version} starting ---");

var createdStates = new HashSet<string>();          // state ids we've created in TP

try
{
    using var client = new TcpClient();
    await client.ConnectAsync("127.0.0.1", 12136, cts.Token);
    var stream = client.GetStream();
    var reader = new StreamReader(stream, Encoding.UTF8);
    var writeLock = new SemaphoreSlim(1, 1);

    async Task Send(object msg)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg) + "\n");
        await writeLock.WaitAsync(cts.Token);
        try { await stream.WriteAsync(bytes, cts.Token); }
        finally { writeLock.Release(); }
    }

    // Make TP's registered states match the user's configured Values: create new
    // ones, remove ones that were dropped. Keeps button bindings stable for
    // entries whose label didn't change.
    async Task Reconcile()
    {
        var wanted = settings.Metrics.Select(m => m.StateId).ToHashSet();
        foreach (var id in createdStates.Where(id => !wanted.Contains(id)).ToList())
        {
            await Send(new { type = "removeState", id });
            createdStates.Remove(id);
            log.Write($"removeState {id}");
        }
        foreach (var m in settings.Metrics)
            if (createdStates.Add(m.StateId))
            {
                await Send(new { type = "createState", id = m.StateId, desc = m.Label, defaultValue = "", parentGroup = "JSON Fetcher" });
                log.Write($"createState {m.StateId} ({m.Label} = {m.Path})");
            }
    }

    await Send(new { type = "pair", id = PluginId });
    log.Write("paired");
    await Reconcile();

    var poller = PollLoop(cts.Token);               // start pushing values
    await ReadLoop(cts.Token);                       // handle TP messages until closePlugin
    await poller;

    async Task ReadLoop(CancellationToken ct)
    {
        string? line;
        while (!ct.IsCancellationRequested && (line = await reader.ReadLineAsync(ct)) is not null)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "info":                        // initial settings snapshot
                    if (root.TryGetProperty("settings", out var init)) settings.Apply(init);
                    log.Write($"info: {settings}");
                    await Reconcile();
                    break;
                case "settings":                    // user changed settings in TP
                    settings.Apply(root.GetProperty("values"));
                    log.Write($"settings changed: {settings}");
                    await Reconcile();
                    break;
                case "closePlugin":                 // TP asked us to exit
                    log.Write("closePlugin");
                    cts.Cancel();
                    return;
            }
        }
    }

    async Task PollLoop(CancellationToken ct)
    {
        // UseProxy=false: talk directly to the local endpoint, bypassing any
        // Windows system/WPAD proxy (which otherwise 404s localhost requests).
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        var lastLogged = new Dictionary<string, string>();   // log a value only when it changes
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var metrics = settings.Metrics;      // snapshot of current config
                var body = await http.GetStringAsync(settings.Endpoint, ct);
                using var doc = JsonDocument.Parse(body);
                foreach (var m in metrics)
                {
                    string value;
                    try { value = Extract(doc.RootElement, m.Path); }
                    catch { value = "n/a"; }         // missing/invalid path -> visible marker
                    await Send(new { type = "stateUpdate", id = m.StateId, value });
                    if (!lastLogged.TryGetValue(m.StateId, out var prev) || prev != value)
                    {
                        log.Write($"{m.Label} = {value}");
                        lastLogged[m.StateId] = value;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.Write($"poll error: {ex.Message}"); }

            try { await Task.Delay(TimeSpan.FromSeconds(settings.Interval), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    // Walk a dotted path (array indexes allowed), return the leaf as a string.
    static string Extract(JsonElement root, string path)
    {
        var el = root;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            el = el.ValueKind == JsonValueKind.Array && int.TryParse(part, out var i)
                ? el[i] : el.GetProperty(part);
        return el.ValueKind == JsonValueKind.String ? el.GetString()! : el.GetRawText();
    }
}
catch (Exception ex) { log.Write($"fatal: {ex}"); }

// --- helpers ---

// One configured value: a friendly Label, the JSON Path to read, and the TP
// state id derived from the label.
record Metric(string Label, string Path, string StateId);

sealed class Settings
{
    public const string Id = "jsonhttpfetcher";     // plugin id + state-prefix (single source of truth)

    readonly object _gate = new();
    string _endpoint = "http://127.0.0.1:8080";
    int _interval = 3;
    List<Metric> _metrics = Parse("Balance=actor.state.uec_balance");

    public string Endpoint      { get { lock (_gate) return _endpoint; } }
    public int    Interval      { get { lock (_gate) return _interval; } }
    public List<Metric> Metrics { get { lock (_gate) return _metrics; } }

    // TP sends settings as an array of single-key objects, keyed by display name.
    public void Apply(JsonElement array)
    {
        lock (_gate)
            foreach (var item in array.EnumerateArray())
                foreach (var p in item.EnumerateObject())
                    switch (p.Name)
                    {
                        case "Endpoint URL":    _endpoint = p.Value.GetString() ?? _endpoint; break;
                        case "Values":          _metrics = Parse(p.Value.GetString() ?? ""); break;
                        case "Refresh Seconds":
                            if (int.TryParse(p.Value.GetString(), out var s)) _interval = Math.Max(1, s);
                            break;
                    }
    }

    // "Label=path, path2, My Label=a.b.c" -> list of metrics with unique state ids.
    static List<Metric> Parse(string raw)
    {
        var list = new List<Metric>();
        var used = new HashSet<string>();
        // entries separated by space, comma, semicolon or newline
        foreach (var chunk in raw.Split(new[] { ' ', '\t', '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string label, path;
            var eq = chunk.IndexOf('=');
            if (eq >= 0) { label = chunk[..eq].Trim(); path = chunk[(eq + 1)..].Trim(); }
            else { path = chunk.Trim(); label = path; }
            if (path.Length == 0) continue;
            if (label.Length == 0) label = path;

            var id = Id + "." + Slug(label);
            var baseId = id; var n = 2;
            while (!used.Add(id)) id = $"{baseId}_{n++}";    // de-dupe clashing labels
            list.Add(new Metric(label, path, id));
        }
        return list;
    }

    static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.Length == 0 ? "value" : sb.ToString();
    }

    public override string ToString() =>
        $"{Endpoint} every {Interval}s, {Metrics.Count} values: " +
        string.Join(", ", Metrics.Select(m => $"{m.Label}={m.Path}"));
}

sealed class Log
{
    readonly string _path;
    public Log(string path) => _path = path;
    public void Write(string msg)
    {
        try { File.AppendAllText(_path, $"{DateTime.Now:HH:mm:ss} {msg}{Environment.NewLine}"); }
        catch { /* logging must never crash the plugin */ }
    }
}
