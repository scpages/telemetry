using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Integrator.Telemetry;

namespace Integrator;

/// <summary>The status + settings window. Sections live on tabs so nothing scrolls.</summary>
internal sealed class MainForm : Form
{
    private readonly TrayApp _tray;

    // status indicators
    private readonly Label _dotGame = Dot(), _dotTele = Dot(), _dotSchema = Dot(), _dotUpload = Dot();
    private readonly Label _lblGame = new(), _lblTele = new(), _lblSchema = new(), _lblUpload = new();

    // settings
    private readonly TextBox _url = new() { Width = 340 };
    private readonly CheckBox _ssl = new() { Text = "Verify TLS certificate", AutoSize = true };
    private readonly ComboBox _auth = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly TextBox _token = new() { Width = 340 };
    private readonly TextBox _source = new() { Width = 180 };
    private readonly TextBox _interval = new() { Width = 70 };
    private readonly Label _testResult = new() { AutoSize = true, MaximumSize = new Size(440, 0) };

    // controls
    private readonly Button _startStop = new() { Width = 120, Height = 32 };
    private readonly CheckBox _send = new() { Text = "Send data to the server (upload)", AutoSize = true };
    private readonly Label _lastUpload = new() { AutoSize = true };

    // local server
    private readonly CheckBox _localOn = new() { Text = "Expose latest frame over local HTTP (no auth)", AutoSize = true };
    private readonly TextBox _localBind = new() { Width = 180 };
    private readonly TextBox _localPort = new() { Width = 70 };
    private readonly Label _localStatus = new() { AutoSize = true };
    private readonly Button _localOpen = new() { Text = "Open in browser", Width = 150, Height = 28, Enabled = false };

    // tabs
    private readonly FlowLayoutPanel _tabbar = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(10, 8, 0, 0), Margin = new Padding(0) };
    private readonly Panel _host = new() { Dock = DockStyle.Fill };
    private readonly List<(Button btn, Control page)> _tabs = new();

    private bool _loading;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    public MainForm(TrayApp tray, AppConfig cfg)
    {
        _tray = tray;
        Text = "SCTelemetry Integrator";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(524, 470);
        BackColor = Theme.Bg;

        _auth.Items.AddRange(new object[] { "None", "Bearer" });

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Bg };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = Header();
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_tabbar, 0, 1);
        root.Controls.Add(_host, 0, 2);
        Controls.Add(root);

        AddTab("Status", StatusPage());
        AddTab("Destination", DestinationPage());
        AddTab("Telemetry", TelemetryPage());
        AddTab("Local server", LocalServerPage());
        AddTab("About", AboutPage());

        LoadFrom(cfg);
        Theme.Apply(this);

        // Theme.Apply paints every panel with the card colour; keep the chrome on Bg
        // and only the content host as the lighter "card".
        root.BackColor = Theme.Bg;
        header.BackColor = Theme.Bg;
        _tabbar.BackColor = Theme.Bg;
        _host.BackColor = Theme.Panel;
        Select(_tabs[0].btn.Text);

        _auth.SelectedIndexChanged += (_, _) => _token.Enabled = _auth.SelectedIndex == 1;

        _timer.Tick += (_, _) => Refresh_();
        _timer.Start();
        Refresh_();
    }

    // ---- tab plumbing ----

    private void AddTab(string name, Control page)
    {
        page.Dock = DockStyle.Fill;
        _host.Controls.Add(page);

        var b = new Button
        {
            Text = name, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(70, 30), Padding = new Padding(10, 0, 10, 0),
            FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 3, 0),
            Font = new Font("Segoe UI", 9f), TabStop = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.Click += (_, _) => Select(name);
        _tabbar.Controls.Add(b);
        _tabs.Add((b, page));
    }

    private void Select(string name)
    {
        foreach (var (btn, page) in _tabs)
        {
            bool on = btn.Text == name;
            page.Visible = on;
            if (on) page.BringToFront();
            btn.BackColor = on ? Theme.Panel : Theme.Bg;
            btn.ForeColor = on ? Theme.Accent : Theme.Muted;
            btn.Font = new Font("Segoe UI", 9f, on ? FontStyle.Bold : FontStyle.Regular);
        }
    }

    // ---- pages ----

    private Control Header()
    {
        var logo = new Label { Text = "SCTELEMETRY", AutoSize = true, ForeColor = Theme.Accent, Font = new Font("Segoe UI", 14, FontStyle.Bold), Margin = new Padding(2, 2, 0, 0), Tag = "keep" };
        var sub = new Label { Text = "Integrator · Telemetry collector for Star Citizen", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(3, 0, 0, 8), Tag = "keep" };
        var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Top, WrapContents = false, Padding = new Padding(12, 10, 0, 0) };
        p.Controls.Add(logo); p.Controls.Add(sub);
        return p;
    }

    private Control StatusPage()
    {
        var g = Page();
        Full(g, StatusLine(_dotGame, _lblGame));
        Full(g, StatusLine(_dotTele, _lblTele));
        Full(g, StatusLine(_dotSchema, _lblSchema));
        Full(g, StatusLine(_dotUpload, _lblUpload));

        _startStop.Click += (_, _) => { _tray.ToggleRun(); Refresh_(); };
        Full(g, Spacer(6));
        Full(g, _startStop);
        _lastUpload.ForeColor = Theme.Muted;
        Full(g, _lastUpload);
        return g;
    }

    private Control DestinationPage()
    {
        var g = Page();
        _url.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _token.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        Labeled(g, "Upload URL", _url);
        Full(g, _ssl);
        Labeled(g, "Authorization", _auth);
        Labeled(g, "Token", _token);
        Full(g, _send);

        var test = new Button { Text = "Test server + credentials", Width = 200, Height = 28 };
        test.Click += async (_, _) => await OnTest();
        var save = new Button { Text = "Save", Width = 90, Height = 28 };
        save.Click += (_, _) => OnSave();
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
        buttons.Controls.Add(save); buttons.Controls.Add(test);
        Full(g, buttons);
        Full(g, _testResult);

        _send.CheckedChanged += (_, _) => { if (!_loading) _tray.SetSendData(_send.Checked); };
        return g;
    }

    private Control TelemetryPage()
    {
        var g = Page();
        Labeled(g, "Game source", _source);
        Labeled(g, "Interval (s)", _interval);

        var data = new Button { Text = "Show latest data", Width = 160, Height = 30 };
        data.Click += async (_, _) => await OnShowData();
        Full(g, Spacer(4));
        Full(g, data);
        Full(g, new Label { Text = "Pulls one frame from the game and shows the raw + processed JSON.", AutoSize = true, ForeColor = Theme.Muted, MaximumSize = new Size(460, 0), Tag = "keep" });
        return g;
    }

    private Control LocalServerPage()
    {
        var g = Page();
        Full(g, _localOn);
        Labeled(g, "Bind address", _localBind);
        Labeled(g, "Port", _localPort);
        Full(g, Spacer(2));
        Full(g, _localStatus);
        _localOpen.Click += (_, _) => OpenUrl(LocalUrl());
        Full(g, _localOpen);
        Full(g, new Label { Text = "Serves the latest frame with no auth. Add ?path=a.b.c for one value.", AutoSize = true, ForeColor = Theme.Muted, MaximumSize = new Size(460, 0), Tag = "keep" });

        _localOn.CheckedChanged += (_, _) => { if (!_loading) { _tray.SetLocal(_localOn.Checked, _localBind.Text.Trim(), ParsePort()); Refresh_(); } };
        return g;
    }

    private Control AboutPage()
    {
        var g = Page();
        Full(g, new Label { Text = "SCTELEMETRY Integrator", AutoSize = true, ForeColor = Theme.Accent, Font = new Font("Segoe UI", 12, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4), Tag = "keep" });
        Full(g, new Label { Text = "Reads Star Citizen's local gRPC telemetry, decodes it to readable JSON\nand uploads it (and/or serves it locally). Zero dependencies.", AutoSize = true, ForeColor = Theme.Text, MaximumSize = new Size(470, 0) });
        Full(g, Spacer(8));
        Full(g, new Label { Text = $"Version {AppCore.Version}", AutoSize = true, ForeColor = Theme.Muted, Tag = "keep" });
        var link = new LinkLabel { Text = AppCore.Repo, AutoSize = true };
        link.LinkClicked += (_, _) => OpenUrl(AppCore.Repo);
        Full(g, link);
        return g;
    }

    private int ParsePort() => int.TryParse(_localPort.Text.Trim(), out var p) && p is > 0 and < 65536 ? p : 8080;

    /// <summary>A browsable URL for the running local server (wildcard binds → localhost).</summary>
    private string LocalUrl()
    {
        string bind = _tray.Config.LocalBind;
        string host = string.IsNullOrWhiteSpace(bind) || bind is "0.0.0.0" or "*" or "+" ? "127.0.0.1" : bind;
        return $"http://{host}:{_tray.Config.LocalPort}/";
    }

    // ---- layout helpers ----

    private static Label Dot() => new() { Text = "●", AutoSize = true, Font = new Font("Segoe UI", 11), ForeColor = Theme.Grey, Margin = new Padding(0, 2, 6, 2) };

    private static Control StatusLine(Label dot, Label text)
    {
        text.AutoSize = true; text.Margin = new Padding(0, 2, 0, 2);
        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 2, 0, 2), WrapContents = false };
        row.Controls.Add(dot); row.Controls.Add(text);
        return row;
    }

    private static Control Spacer(int h) => new Label { Text = "", AutoSize = false, Height = h, Width = 1, Margin = new Padding(0) };

    private static TableLayoutPanel Page()
    {
        var g = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Panel, Padding = new Padding(16, 14, 16, 14), AutoScroll = false };
        g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return g;
    }

    private static void Labeled(TableLayoutPanel g, string label, Control field)
    {
        g.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 7, 12, 6), ForeColor = Theme.Text });
        field.Margin = new Padding(0, 4, 0, 4);
        g.Controls.Add(field);
    }

    private static void Full(TableLayoutPanel g, Control c)
    {
        if (c.Margin == new Padding(3)) c.Margin = new Padding(0, 5, 0, 5);  // default -> tidy
        g.Controls.Add(c);
        g.SetColumnSpan(c, 2);
    }

    private void LoadFrom(AppConfig cfg)
    {
        _loading = true;
        _url.Text = cfg.Url;
        _ssl.Checked = cfg.UrlSslVerify;
        _auth.SelectedIndex = cfg.UseBearer ? 1 : 0;
        _token.Text = cfg.Token;
        _token.Enabled = cfg.UseBearer;
        _source.Text = cfg.Source;
        _interval.Text = cfg.IntervalSeconds.ToString(CultureInfo.InvariantCulture);
        _send.Checked = cfg.SendData;
        _localOn.Checked = cfg.LocalServerEnabled;
        _localBind.Text = cfg.LocalBind;
        _localPort.Text = cfg.LocalPort.ToString(CultureInfo.InvariantCulture);
        _loading = false;
    }

    private AppConfig? ReadInto()
    {
        if (!double.TryParse(_interval.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var secs) || secs <= 0)
        {
            MessageBox.Show(this, "Interval must be a number greater than 0.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        if (_url.Text.Trim().Length == 0)
        {
            MessageBox.Show(this, "Upload URL is required.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        return new AppConfig
        {
            Url = _url.Text.Trim(),
            UrlSslVerify = _ssl.Checked,
            UseBearer = _auth.SelectedIndex == 1,
            Token = _token.Text.Trim(),
            Source = _source.Text.Trim().Length > 0 ? _source.Text.Trim() : "127.0.0.1:50051",
            IntervalSeconds = secs,
            SendData = _send.Checked,
            LocalServerEnabled = _localOn.Checked,
            LocalBind = _localBind.Text.Trim().Length > 0 ? _localBind.Text.Trim() : "127.0.0.1",
            LocalPort = ParsePort(),
        };
    }

    private void OnSave()
    {
        var cfg = ReadInto();
        if (cfg == null) return;
        _tray.ApplyConfig(cfg);
    }

    private async Task OnTest()
    {
        var cfg = ReadInto();
        if (cfg == null) return;
        _testResult.ForeColor = Theme.Muted;
        _testResult.Text = "testing…";
        try
        {
            TestResult r = await ServerTester.RunAsync(cfg.Url, cfg.UrlSslVerify, cfg.EffectiveToken, CancellationToken.None);
            string creds = r.CredsOk == null ? (cfg.UseBearer ? $"credentials: {r.Creds}" : "no token to test") : (r.CredsOk.Value ? "credentials: accepted" : $"credentials: {r.Creds}");
            _testResult.ForeColor = r.ServerUp && r.CredsOk != false ? Theme.Green : Theme.Red;
            _testResult.Text = $"Server {r.Server}\n{creds}";
        }
        catch (Exception e) { _testResult.ForeColor = Theme.Red; _testResult.Text = e.Message; }
    }

    private async Task OnShowData()
    {
        Worker? w = _tray.CurrentWorker;
        if (w == null) { MessageBox.Show(this, "Not started yet.", "Latest data"); return; }
        if (!w.GameRunning) { MessageBox.Show(this, "The game is not running.", "Latest data"); return; }
        try
        {
            var (raw, processed) = await w.CaptureAsync(CancellationToken.None);
            using var f = new DataForm(raw, processed);
            f.ShowDialog(this);
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Latest data", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void Refresh_()
    {
        Worker? w = _tray.CurrentWorker;
        bool running = w != null && !w.Paused;
        _startStop.Text = running ? "■  Stop" : "▶  Start";

        Set(_dotGame, _lblGame, "Game running", w?.GameRunning == true, "yes", "no");
        Set(_dotTele, _lblTele, "Game telemetry (gRPC)", w?.TelemetryUp == true, "connected", w?.GameRunning == true ? "down" : "idle", neutralWhenGameOff: true, gameOn: w?.GameRunning == true);
        Set(_dotSchema, _lblSchema, "Telemetry metadata", w?.SchemaLoaded == true, "loaded", "not loaded");

        if (!_tray.Config.SendData)
        {
            _dotUpload.ForeColor = Theme.Grey;
            _lblUpload.Text = "Upload endpoint: sending off";
            _lblUpload.ForeColor = Theme.Muted;
        }
        else
        {
            Set(_dotUpload, _lblUpload, "Upload endpoint", w?.UploadUp == true, "ok", (w?.LastError != null) ? "error" : "unknown");
        }

        _lastUpload.Text = w?.LastOkUtc is { } ok
            ? "Last successful upload: " + ok.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "Last successful upload: never";
        _lastUpload.ForeColor = Theme.Muted;

        UpdateLocalStatus();
        _tray.UpdateIcon();
    }

    private void UpdateLocalStatus()
    {
        LocalServer ls = _tray.Local;
        if (!_tray.Config.LocalServerEnabled) { _localStatus.Text = "Status: off"; _localStatus.ForeColor = Theme.Muted; }
        else if (ls.Error != null) { _localStatus.Text = "Status: error — " + ls.Error; _localStatus.ForeColor = Theme.Red; }
        else if (ls.Running) { _localStatus.Text = "Status: serving at " + ls.Prefix.Replace("+", _tray.Config.LocalBind); _localStatus.ForeColor = Theme.Green; }
        else { _localStatus.Text = "Status: starting…"; _localStatus.ForeColor = Theme.Muted; }

        _localOpen.Enabled = ls.Running;
    }

    private static void Set(Label dot, Label text, string caption, bool ok, string okWord, string badWord, bool neutralWhenGameOff = false, bool gameOn = true)
    {
        if (neutralWhenGameOff && !gameOn) { dot.ForeColor = Theme.Grey; text.Text = $"{caption}: idle"; text.ForeColor = Theme.Muted; return; }
        dot.ForeColor = ok ? Theme.Green : Theme.Red;
        text.Text = $"{caption}: {(ok ? okWord : badWord)}";
        text.ForeColor = Theme.Text;
    }

    public static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Hide to tray instead of exiting, unless the app is really shutting down.
        if (e.CloseReason == CloseReason.UserClosing && !_tray.Exiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _timer.Stop();
        base.OnFormClosing(e);
    }
}
