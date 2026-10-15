using Sorla.Core;
using Sorla.Core.Dictation;
using Sorla.Core.Transcription;
using Sorla.Platform;

namespace Sorla.UI;

public sealed class SettingsForm : Form
{
    private readonly Settings settings;
    private readonly Action changed;
    private readonly Action modelChanged;
    private readonly Label altGrWarning;

    public SettingsForm(Settings settings, Action changed, Action modelChanged)
    {
        this.settings = settings;
        this.changed = changed;
        this.modelChanged = modelChanged;

        Text = Strings.Get("settings.title");
        Icon = Icons.TrayIcon(TrayMark.Ready);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 9f);
        Padding = new Padding(16);

        var layout = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var trigger = Combo(Enum.GetValues<TriggerKey>().Select(k => (object)new Choice<TriggerKey>(k, k.DisplayName())), settings.TriggerKey);
        altGrWarning = new Label { Text = Strings.Get("settings.altGrWarning"), AutoSize = true, MaximumSize = new Size(LogicalToDeviceUnits(440), 0), ForeColor = Color.DimGray, Margin = new Padding(3, 0, 3, 8) };
        trigger.SelectedIndexChanged += (_, _) =>
        {
            settings.TriggerKey = ((Choice<TriggerKey>)trigger.SelectedItem!).Value;
            altGrWarning.Visible = settings.TriggerKey == TriggerKey.RightAlt;
            Save();
        };
        altGrWarning.Visible = settings.TriggerKey == TriggerKey.RightAlt;

        var mode = Combo(Enum.GetValues<RecordingMode>().Select(m => (object)new Choice<RecordingMode>(m, ModeName(m))), settings.RecordingMode);
        mode.SelectedIndexChanged += (_, _) =>
        {
            settings.RecordingMode = ((Choice<RecordingMode>)mode.SelectedItem!).Value;
            Save();
        };

        var variant = Combo(
            [
                new Choice<ModelVariant>(ModelVariant.Int8, Strings.Get("settings.modelInt8")),
                new Choice<ModelVariant>(ModelVariant.Int4, Strings.Get("settings.modelInt4")),
            ],
            settings.ModelVariant);
        variant.SelectedIndexChanged += (_, _) =>
        {
            var chosen = ((Choice<ModelVariant>)variant.SelectedItem!).Value;
            if (chosen == settings.ModelVariant) return;
            settings.ModelVariant = chosen;
            Save();
            modelChanged();
        };

        layout.Controls.Add(new Label { Text = Strings.Get("settings.trigger"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(trigger, 1, 0);
        layout.Controls.Add(altGrWarning, 1, 1);
        layout.Controls.Add(new Label { Text = Strings.Get("settings.mode"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(mode, 1, 2);

        // Microphone: Windows' default (named, so it is clear which one that is now) or a specific device.
        var microphone = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = LogicalToDeviceUnits(400), Anchor = AnchorStyles.Left };
        var defaultName = MicrophoneSource.DefaultDeviceName ?? "-";
        microphone.Items.Add(new MicrophoneChoice("", Strings.Format("settings.microphoneDefault", defaultName)));
        foreach (var device in MicrophoneSource.Devices()) microphone.Items.Add(new MicrophoneChoice(device.Id, device.Name));
        var chosenIndex = microphone.Items.Cast<MicrophoneChoice>().ToList().FindIndex(c => c.Id == settings.MicrophoneId);
        microphone.SelectedIndex = Math.Max(0, chosenIndex);
        microphone.SelectedIndexChanged += (_, _) =>
        {
            settings.MicrophoneId = ((MicrophoneChoice)microphone.SelectedItem!).Id;
            Save();
        };

        var readiness = Combo(Enum.GetValues<MicrophoneReadiness>().Select(r => (object)new Choice<MicrophoneReadiness>(r, r.DisplayName())), settings.MicrophoneReadiness);
        readiness.SelectedIndexChanged += (_, _) =>
        {
            settings.MicrophoneReadiness = ((Choice<MicrophoneReadiness>)readiness.SelectedItem!).Value;
            Save();
        };
        var readinessHelp = new Label { Text = Strings.Get("settings.readyHelp"), AutoSize = true, MaximumSize = new Size(LogicalToDeviceUnits(400), 0), ForeColor = Color.DimGray, Margin = new Padding(3, 0, 3, 8) };

        layout.Controls.Add(new Label { Text = Strings.Get("settings.microphone"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 10, 3, 3) }, 0, 3);
        microphone.Margin = new Padding(3, 8, 3, 3);
        layout.Controls.Add(microphone, 1, 3);
        layout.Controls.Add(new Label { Text = Strings.Get("settings.ready"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        layout.Controls.Add(readiness, 1, 4);
        layout.Controls.Add(readinessHelp, 1, 5);

        var row = 6;
        foreach (var (key, get, set) in new (string, Func<bool>, Action<bool>)[]
        {
            ("settings.keepClipboard", () => settings.KeepClipboardContent, v => settings.KeepClipboardContent = v),
            ("settings.keepLast", () => settings.KeepLastTranscription, v => settings.KeepLastTranscription = v),
            ("settings.sounds", () => settings.PlaySounds, v => settings.PlaySounds = v),
        })
        {
            var box = new CheckBox { Text = Strings.Get(key), Checked = get(), AutoSize = true, Margin = new Padding(3, 6, 3, 0) };
            box.CheckedChanged += (_, _) => { set(box.Checked); Save(); };
            layout.Controls.Add(box, 0, row);
            layout.SetColumnSpan(box, 2);
            row++;
        }

        var login = new CheckBox { Text = Strings.Get("settings.launchAtLogin"), Checked = LoginItem.IsEnabled, AutoSize = true, Margin = new Padding(3, 6, 3, 0) };
        login.CheckedChanged += (_, _) => LoginItem.Set(login.Checked);
        layout.Controls.Add(login, 0, row);
        layout.SetColumnSpan(login, 2);
        row++;

        layout.Controls.Add(new Label { Text = Strings.Get("settings.model"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 14, 3, 3) }, 0, row);
        variant.Margin = new Padding(3, 12, 3, 3);
        layout.Controls.Add(variant, 1, row);
        row++;

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0) };
        var close = new Button { Text = Strings.Get("settings.close"), AutoSize = true, DialogResult = DialogResult.OK };
        close.Click += (_, _) => Close();
        var folder = new Button { Text = Strings.Get("settings.openModelFolder"), AutoSize = true };
        folder.Click += (_, _) => SettingsPages.Folder(settings.EffectiveModelDirectory);
        buttons.Controls.Add(close);
        buttons.Controls.Add(folder);
        layout.Controls.Add(buttons, 0, row);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = close;
        CancelButton = close;
    }

    public static string ModeName(RecordingMode mode) =>
        Strings.Get(mode == RecordingMode.Toggle ? "mode.toggle" : "mode.pushToTalk");

    private void Save()
    {
        settings.Save();
        changed();
    }

    private ComboBox Combo<T>(IEnumerable<object> items, T selected) where T : struct
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = LogicalToDeviceUnits(400), Anchor = AnchorStyles.Left };
        foreach (var item in items) combo.Items.Add(item);
        combo.SelectedIndex = Math.Max(0, combo.Items.Cast<Choice<T>>().ToList().FindIndex(c => c.Value.Equals(selected)));
        return combo;
    }

    private sealed record MicrophoneChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record Choice<T>(T Value, string Name)
    {
        public override string ToString() => Name;
    }
}
