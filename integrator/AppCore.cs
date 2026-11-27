using System.Drawing;
using Integrator.Protobuf;

namespace Integrator;

/// <summary>Shared bootstrap: locate config, load the schema, prepare a worker.</summary>
internal static class AppCore
{
    public const string Repo = "https://github.com/scpages/telemetry";

    private static Icon? _appIcon;

    /// <summary>The app/window icon (same image as the website favicon), embedded in the exe.</summary>
    public static Icon? AppIcon
    {
        get
        {
            if (_appIcon != null) return _appIcon;
            try
            {
                using Stream? s = typeof(AppCore).Assembly.GetManifestResourceStream("Integrator.app.ico");
                if (s != null) _appIcon = new Icon(s);
            }
            catch { /* fall back to the default window icon */ }
            return _appIcon;
        }
    }

    public static string Version =>
        typeof(AppCore).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "config.ini");

    public static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScTelemetry");

    /// <summary>Write a starter config.ini next to the exe if none exists. Returns true if created.</summary>
    public static bool EnsureConfig()
    {
        if (File.Exists(ConfigPath)) return false;
        AppConfig.Default().Save(ConfigPath);
        return true;
    }

    public static AppConfig LoadConfig() => AppConfig.Load(ConfigPath);

    /// <summary>Resolve the game exe (config override or auto-detect).</summary>
    public static string ResolveExe(AppConfig cfg)
    {
        if (cfg.ExePath.Length > 0)
        {
            if (!File.Exists(cfg.ExePath))
                throw new FileNotFoundException($"config 'exe' path not found: {cfg.ExePath}");
            return cfg.ExePath;
        }
        return GameLocator.Find()
            ?? throw new FileNotFoundException(
                "Could not find StarCitizen.exe. Set 'exe' in config.ini to its full path.");
    }

    public static Schema LoadSchema(AppConfig cfg)
    {
        string exe = ResolveExe(cfg);
        return SchemaStore.Load(exe, CacheDir);
    }

    public static string RootType(Schema schema) =>
        schema.FindMessageEndingWith("TelemetryStreamResponse")
        ?? ".sc.game.telemetry.v1.TelemetryStreamResponse";

    public static string ListActionsResponseType(Schema schema) =>
        schema.FindMessageEndingWith("ListTriggerableActionsResponse")
        ?? ".sc.game.telemetry.v1.ListTriggerableActionsResponse";

    public static string TriggerResponseType(Schema schema) =>
        schema.FindMessageEndingWith("TriggerGameActionResponse")
        ?? ".sc.game.telemetry.v1.TriggerGameActionResponse";
}
