using System.Drawing;
using System.Windows.Forms;

namespace Integrator;

/// <summary>Shows the latest frame: raw (decoded from the game) and processed (uploaded).</summary>
internal sealed class DataForm : Form
{
    public DataForm(string raw, string processed)
    {
        Text = "Latest data";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 560);
        BackColor = Theme.Bg;
        if (AppCore.AppIcon is { } appIcon) Icon = appIcon;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 380,
            BackColor = Theme.Line,
        };

        split.Panel1.Controls.Add(MakePane("Raw — field numbers, straight from gRPC (no schema)", raw));
        split.Panel2.Controls.Add(MakePane("Polished — decoded to names (what gets uploaded)", processed));

        Controls.Add(split);
    }

    private static Control MakePane(string title, string text)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(8) };
        var box = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Cascadia Mono", 9, FontStyle.Regular, GraphicsUnit.Point),
            BackColor = Color.FromArgb(4, 9, 14), ForeColor = Color.FromArgb(207, 232, 242),
            BorderStyle = BorderStyle.FixedSingle,
            Text = string.IsNullOrEmpty(text) ? "(no data — is the game loaded in?)" : text,
        };
        var label = new Label { Dock = DockStyle.Top, Text = title, Height = 24, ForeColor = Theme.Accent, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        panel.Controls.Add(box);
        panel.Controls.Add(label);
        return panel;
    }
}
