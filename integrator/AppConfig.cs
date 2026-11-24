using System.Globalization;
using System.Text;

namespace Integrator;

/// <summary>
/// The tool's settings. Backed by a simple INI file, editable from the Settings
/// window or by hand. Keys: interval, url, auth, token, url_ssl_verify, source, exe,
/// send_data, local_server, local_bind, local_port, file_save, file_path.
/// </summary>
internal sealed class AppConfig
{
    public double IntervalSeconds { get; set; } = 1.0;
    public string Url { get; set; } = "https://example.com/api/v1/telemetry";
    public bool UseBearer { get; set; } = true;             // authorization: none | bearer
    public string Token { get; set; } = "";                 // bearer token sent on upload
    public bool UrlSslVerify { get; set; } = true;
    public string Source { get; set; } = "127.0.0.1:50051";
    public string ExePath { get; set; } = "";               // empty = auto-detect

    public bool SendData { get; set; } = true;              // upload to the URL on/off
    public bool LocalServerEnabled { get; set; } = false;   // expose latest frame over local HTTP
    public string LocalBind { get; set; } = "127.0.0.1";
    public int LocalPort { get; set; } = 8080;

    public bool SaveToFile { get; set; } = false;           // write the latest frame to a file on disk
    public string FilePath { get; set; } = "";              // destination file (empty = disabled)

    /// <summary>The token actually sent (empty when auth is None).</summary>
    public string EffectiveToken => UseBearer ? Token : "";

    public static AppConfig Default() => new();

    public static AppConfig Load(string path)
    {
        var ini = Ini.Parse(File.ReadAllLines(path));
        var cfg = new AppConfig();

        if (ini.TryGetValue("interval", out var interval) &&
            double.TryParse(interval, NumberStyles.Float, CultureInfo.InvariantCulture, out var secs) && secs > 0)
            cfg.IntervalSeconds = secs;

        if (ini.TryGetValue("url", out var url)) cfg.Url = url.Trim();
        if (ini.TryGetValue("auth", out var auth)) cfg.UseBearer = !auth.Trim().Equals("none", StringComparison.OrdinalIgnoreCase);
        if (ini.TryGetValue("token", out var token)) cfg.Token = token.Trim();
        if (ini.TryGetValue("url_ssl_verify", out var verify)) cfg.UrlSslVerify = ParseBool(verify, true);
        if (ini.TryGetValue("source", out var source) && source.Trim().Length > 0) cfg.Source = source.Trim();
        if (ini.TryGetValue("exe", out var exe)) cfg.ExePath = exe.Trim();

        if (ini.TryGetValue("send_data", out var send)) cfg.SendData = ParseBool(send, true);
        if (ini.TryGetValue("local_server", out var ls)) cfg.LocalServerEnabled = ParseBool(ls, false);
        if (ini.TryGetValue("local_bind", out var lb) && lb.Trim().Length > 0) cfg.LocalBind = lb.Trim();
        if (ini.TryGetValue("local_port", out var lp) && int.TryParse(lp.Trim(), out var port) && port is > 0 and < 65536) cfg.LocalPort = port;

        if (ini.TryGetValue("file_save", out var fs)) cfg.SaveToFile = ParseBool(fs, false);
        if (ini.TryGetValue("file_path", out var fp)) cfg.FilePath = fp.Trim();

        return cfg;
    }

    public void Save(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# StarCitizen_Telemetry configuration");
        sb.AppendLine("# Edited by the Settings window, but plain text if you prefer.");
        sb.AppendLine();
        sb.AppendLine("# How often to pull data from the game, in seconds (decimals allowed).");
        sb.AppendLine($"interval = {IntervalSeconds.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine("# HTTP endpoint the decoded JSON is POSTed to.");
        sb.AppendLine($"url = {Url}");
        sb.AppendLine();
        sb.AppendLine("# Authorization: none | bearer");
        sb.AppendLine($"auth = {(UseBearer ? "bearer" : "none")}");
        sb.AppendLine("# Bearer token sent as 'Authorization: Bearer <token>' when auth = bearer.");
        sb.AppendLine($"token = {Token}");
        sb.AppendLine();
        sb.AppendLine("# Verify the endpoint's TLS certificate (true/false).");
        sb.AppendLine($"url_ssl_verify = {(UrlSslVerify ? "true" : "false")}");
        sb.AppendLine();
        sb.AppendLine("# The game's local gRPC server (ip:port).");
        sb.AppendLine($"source = {Source}");
        sb.AppendLine();
        sb.AppendLine("# Optional path to StarCitizen.exe (empty = auto-detect).");
        sb.AppendLine($"exe = {ExePath}");
        sb.AppendLine();
        sb.AppendLine("# Upload the data to the URL above (true/false).");
        sb.AppendLine($"send_data = {(SendData ? "true" : "false")}");
        sb.AppendLine("# Expose the latest frame over a local HTTP server (no auth).");
        sb.AppendLine($"local_server = {(LocalServerEnabled ? "true" : "false")}");
        sb.AppendLine($"local_bind = {LocalBind}");
        sb.AppendLine($"local_port = {LocalPort}");
        sb.AppendLine();
        sb.AppendLine("# Write the latest frame (the uploaded JSON) to a file on disk (true/false).");
        sb.AppendLine($"file_save = {(SaveToFile ? "true" : "false")}");
        sb.AppendLine("# Destination file for the above (empty = disabled).");
        sb.AppendLine($"file_path = {FilePath}");

        File.WriteAllText(path, sb.ToString());
    }

    private static bool ParseBool(string s, bool fallback)
    {
        s = s.Trim().ToLowerInvariant();
        return s switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => fallback,
        };
    }
}
