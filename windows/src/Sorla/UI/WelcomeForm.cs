using Sorla.Core;
using Sorla.Core.Transcription;
using Sorla.Platform;

namespace Sorla.UI;

// First-run window: what Sorla does, the model download, and a box to try dictating in.
public sealed class WelcomeForm : Form
{
    private readonly Settings settings;
    private readonly ModelHost model;
    private readonly ModelInstall install;
    private readonly Label modelStatus;
    private readonly ProgressBar progress;
    private readonly Button download;
    private readonly Label tryLabel;

    public TextBox TryBox { get; }

    public WelcomeForm(Settings settings, ModelHost model, ModelInstall install)
    {
        this.settings = settings;
        this.model = model;
        this.install = install;

        Text = Strings.Get("welcome.title");
        Icon = Icons.TrayIcon(TrayMark.Ready);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Segoe UI", 9.5f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(20);

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        // Sizes are given at 96 DPI and scaled to the screen; fonts in points scale by themselves.
        var width = LogicalToDeviceUnits(520);

        var header = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 10) };
        header.Controls.Add(new PictureBox { Image = Icons.Render(LogicalToDeviceUnits(48)), Size = new Size(LogicalToDeviceUnits(48), LogicalToDeviceUnits(48)), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 12, 0) });
        var titles = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
        titles.Controls.Add(new Label { Text = "Sorla", Font = new Font("Segoe UI Semibold", 16f), AutoSize = true });
        titles.Controls.Add(new Label { Text = Strings.Get("app.tagline"), ForeColor = Color.DimGray, AutoSize = true });
        header.Controls.Add(titles);
        layout.Controls.Add(header);

        layout.Controls.Add(Paragraph(Strings.Get("welcome.intro"), width));

        var sizeMb = ModelFiles.TotalBytes(settings.ModelVariant) / 1_000_000;
        layout.Controls.Add(Paragraph(Strings.Format("welcome.step.model", sizeMb), width, bold: true));
        modelStatus = Paragraph("", width);
        layout.Controls.Add(modelStatus);
        progress = new ProgressBar { Width = width, Height = LogicalToDeviceUnits(10), Maximum = 1000, Visible = false, Margin = new Padding(3, 0, 3, 6) };
        layout.Controls.Add(progress);
        download = new Button { Text = Strings.Get("welcome.download"), AutoSize = true, Margin = new Padding(3, 0, 3, 12) };
        download.Click += async (_, _) => await install.RunAsync();
        layout.Controls.Add(download);

        tryLabel = Paragraph("", width, bold: true);
        layout.Controls.Add(tryLabel);
        TryBox = new TextBox { Multiline = true, Width = width, Height = LogicalToDeviceUnits(80), PlaceholderText = Strings.Get("welcome.tryPlaceholder"), ScrollBars = ScrollBars.Vertical };
        layout.Controls.Add(TryBox);

        var micName = MicrophoneSource.DefaultDeviceName;
        layout.Controls.Add(Paragraph(micName != null ? Strings.Format("welcome.mic", micName) : Strings.Get("welcome.noMic"), width, dim: micName != null));
        if (micName == null)
        {
            var open = new LinkLabel { Text = Strings.Get("issue.openPrivacy"), AutoSize = true };
            open.LinkClicked += (_, _) => SettingsPages.Microphone();
            layout.Controls.Add(open);
        }

        var done = new Button { Text = Strings.Get("welcome.done"), AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 12, 3, 3) };
        done.Click += (_, _) => Close();
        layout.Controls.Add(done);
        Controls.Add(layout);
        AcceptButton = done;

        install.Changed += Refresh;
        model.StateChanged += Refresh;
        FormClosed += (_, _) =>
        {
            install.Changed -= Refresh;
            model.StateChanged -= Refresh;
            settings.HasCompletedOnboarding = true;
            settings.Save();
        };
        Refresh();
    }

    private new void Refresh()
    {
        if (IsDisposed) return;
        tryLabel.Text = Strings.Format("welcome.step.try", settings.TriggerKey.DisplayName());
        progress.Visible = install.IsRunning;
        download.Visible = !install.IsRunning && model.State is ModelState.Missing or ModelState.Failed;
        if (install.IsRunning && install.Progress is { } p)
        {
            progress.Value = (int)Math.Clamp(p.Fraction * 1000, 0, 1000);
            modelStatus.Text = p.Fraction >= 1 ? Strings.Get("welcome.verifying") : Strings.Format("welcome.downloading", p.CurrentFile, p.Fraction);
            return;
        }
        modelStatus.Text = install.Error != null ? Strings.Format("welcome.failed", install.Error) : model.State switch
        {
            ModelState.Ready => Strings.Get("welcome.installed"),
            ModelState.Loading => Strings.Get("welcome.loading"),
            ModelState.Failed => Strings.Format("issue.modelFailed.body", model.FailureMessage ?? ""),
            _ => Strings.Get("status.noModel"),
        };
    }

    private static Label Paragraph(string text, int width, bool bold = false, bool dim = false) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(width, 0),
        Margin = new Padding(3, 0, 3, 10),
        Font = bold ? new Font("Segoe UI Semibold", 9.5f) : null,
        ForeColor = dim ? Color.DimGray : SystemColors.ControlText,
    };
}
