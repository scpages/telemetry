using System.Drawing;
using System.Windows.Forms;
using Integrator.Telemetry;

namespace Integrator;

/// <summary>Owns the config, the worker and the window; shows a status-coloured tray icon.</summary>
internal sealed class TrayApp : IDisposable
{
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private AppConfig _cfg;
    private Worker? _worker;
    private MainForm? _window;
    private readonly LocalServer _local;

    public bool Exiting { get; private set; }
    public Worker? CurrentWorker => _worker;
    public AppConfig Config => _cfg;
    public LocalServer Local => _local;

    public TrayApp()
    {
        if (!File.Exists(AppCore.ConfigPath)) AppCore.EnsureConfig();
        try { _cfg = AppCore.LoadConfig(); } catch { _cfg = AppConfig.Default(); }

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show window", null, (_, _) => ShowWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _tray = new NotifyIcon { Text = "SCTelemetry Integrator", Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => ShowWindow();

        _local = new LocalServer(() => _worker?.LastFrame);
        StartWorker(paused: false);
        ConfigureLocal();
        UpdateIcon();

        _timer.Tick += (_, _) => UpdateIcon();
        _timer.Start();

        ShowWindow();   // show on first launch
    }

    private void StartWorker(bool paused)
    {
        _worker?.Dispose();
        _worker = new Worker(_cfg);
        _worker.Start(paused);
    }

    public void ApplyConfig(AppConfig cfg)
    {
        bool wasPaused = _worker?.Paused ?? false;
        _cfg = cfg;
        try { cfg.Save(AppCore.ConfigPath); } catch { }
        StartWorker(wasPaused);
        ConfigureLocal();
    }

    private void ConfigureLocal()
    {
        if (_cfg.LocalServerEnabled) _local.Start(_cfg.LocalBind, _cfg.LocalPort);
        else _local.Stop();
    }

    public void ToggleRun()
    {
        if (_worker == null) { StartWorker(false); return; }
        if (_worker.Paused) _worker.Resume(); else _worker.Pause();
    }

    // Live toggles (the worker reads _cfg each tick, so no restart needed for these).
    public void SetSendData(bool on) { _cfg.SendData = on; SaveCfg(); }

    public void SetLocal(bool on, string bind, int port)
    {
        _cfg.LocalServerEnabled = on;
        _cfg.LocalBind = string.IsNullOrWhiteSpace(bind) ? "127.0.0.1" : bind;
        _cfg.LocalPort = port is > 0 and < 65536 ? port : 8080;
        SaveCfg();
        ConfigureLocal();
    }

    public void SetFile(bool on, string filePath)
    {
        _cfg.SaveToFile = on;
        _cfg.FilePath = filePath?.Trim() ?? "";
        SaveCfg();
    }

    private void SaveCfg() { try { _cfg.Save(AppCore.ConfigPath); } catch { } }

    private void ShowWindow()
    {
        if (_window == null || _window.IsDisposed)
            _window = new MainForm(this, _cfg);
        _window.Show();
        _window.WindowState = FormWindowState.Normal;
        _window.Activate();
    }

    public void UpdateIcon()
    {
        _tray.Icon = IconFactory.Make(StatusColor());
        _tray.Text = Trim("SCTelemetry Integrator · " + StatusText(), 63);
    }

    private Color StatusColor()
    {
        var w = _worker;
        if (string.IsNullOrWhiteSpace(_cfg.Url) || w == null || w.Paused) return Theme.Orange;  // not configured / stopped
        if (!w.GameRunning) return Theme.Grey;                                                   // no game
        if (w.LastOkUtc is { } ok && (DateTime.UtcNow - ok).TotalSeconds < 15) return Theme.Green; // uploading
        return Theme.Red;                                                                        // game up but telemetry/upload issue
    }

    private string StatusText()
    {
        var w = _worker;
        if (w == null) return "stopped";
        if (w.Paused) return "stopped";
        if (!w.GameRunning) return "waiting for game";
        if (w.LastError != null) return "error: " + Trim(w.LastError, 40);
        if (w.LastOkUtc is { } ok) return $"uploading · {w.Sent} sent · {(int)(DateTime.UtcNow - ok).TotalSeconds}s ago";
        return w.Phase;
    }

    private void ExitApp()
    {
        Exiting = true;
        Application.Exit();
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    public void Dispose()
    {
        _timer.Dispose();
        _local.Dispose();
        _worker?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
    }
}
