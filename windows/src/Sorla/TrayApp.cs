using Microsoft.Win32;
using Sorla.Core;
using Sorla.Core.Audio;
using Sorla.Core.Dictation;
using Sorla.Core.Transcription;
using Sorla.Platform;
using Sorla.UI;

namespace Sorla;

// Sorla lives in the notification area, like it lives in the menu bar on the Mac.
public sealed class TrayApp : ApplicationContext
{
    private readonly Settings settings;
    private readonly ModelHost model;
    private readonly ModelInstall install;
    private readonly RecordingIndicator indicator = new();
    private readonly Feedback feedback = new();
    private readonly MicrophoneService microphone;
    private readonly Func<string?, IAudioSource> createSource;
    private string appliedMicrophoneId;
    private MicrophoneReadiness appliedReadiness;
    private readonly DictationController dictation;
    private readonly InputHook hook;
    private readonly HotKeyWindow hotKeys = new();
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem statusItem;
    private readonly ToolStripMenuItem dictateItem;
    private readonly ToolStripMenuItem pasteLastItem;
    private readonly HashSet<Issue> dismissedIssues = [];
    private PushToTalkGesture gesture;
    private SettingsForm? settingsForm;
    private WelcomeForm? welcomeForm;
    private TrayMark currentMark = (TrayMark)(-1);

    public TrayApp(Settings settings, Func<string?, IAudioSource> createSource, string? pasteGuard = null)
    {
        this.settings = settings;
        model = new ModelHost(settings);
        install = new ModelInstall(settings, model);
        feedback.Enabled = settings.PlaySounds;
        this.createSource = createSource;
        microphone = new MicrophoneService(() => createSource(settings.MicrophoneId)) { KeepWarm = settings.MicrophoneReadiness.KeepWarm() };
        appliedMicrophoneId = settings.MicrophoneId;
        appliedReadiness = settings.MicrophoneReadiness;
        dictation = new DictationController(settings, model, microphone, indicator, feedback);
        dictation.PasteOnlyIntoProcess = pasteGuard;
        gesture = new PushToTalkGesture(settings.RecordingMode);

        statusItem = new ToolStripMenuItem { Enabled = false };
        dictateItem = new ToolStripMenuItem(Strings.Get("menu.startDictation"), null, (_, _) => ToggleFromMenu());
        pasteLastItem = new ToolStripMenuItem(Strings.Get("menu.pasteLast"), null, (_, _) => dictation.PasteLast());
        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            statusItem,
            new ToolStripSeparator(),
            dictateItem,
            pasteLastItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem(Strings.Get("menu.welcome"), null, (_, _) => ShowWelcome()),
            new ToolStripMenuItem(Strings.Get("menu.settings"), null, (_, _) => ShowSettings()),
            new ToolStripMenuItem(Strings.Get("menu.about"), null, (_, _) => ShowAbout()),
            new ToolStripSeparator(),
            new ToolStripMenuItem(Strings.Get("menu.quit"), null, (_, _) => ExitThread()),
        ]);
        menu.Opening += (_, _) => pasteLastItem.Enabled = dictation.HasRecentTranscript && !dictation.IsBusy;

        tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Text = "Sorla" };
        tray.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            // NotifyIcon only opens its menu on right click; this opens it on left click too.
            typeof(NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(tray, null);
        };

        hook = new InputHook(settings.TriggerKey.VirtualKey());
        hook.Input += OnInput;
        hook.Install();
        hotKeys.PasteLast += () => dictation.PasteLast();

        dictation.StateChanged += UpdateStatus;
        dictation.IssueRaised += ShowIssue;
        model.StateChanged += UpdateStatus;
        install.Changed += UpdateStatus;

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        model.Load();
        PrepareMicrophoneIfAlways();
        UpdateStatus();
        if (!settings.HasCompletedOnboarding || !model.IsInstalled) ShowWelcome();
    }

    // Exposed for the automated end-to-end test, which types into this box.
    public WelcomeForm? Welcome => welcomeForm;

    private void OnInput(InputKind kind, double at)
    {
        // A release that never arrived (UAC prompt, lock screen) would leave a held gesture stuck.
        if (kind != InputKind.TriggerUp && gesture.IsWaitingForRelease && !hook.IsTriggerPhysicallyDown && kind != InputKind.TriggerDown)
        {
            hook.ForgetTriggerState();
            Apply(gesture.Handle(new GestureEvent.TriggerUp(at)));
        }
        GestureEvent e = kind switch
        {
            InputKind.TriggerDown => new GestureEvent.TriggerDown(at),
            InputKind.TriggerUp => new GestureEvent.TriggerUp(at),
            InputKind.OtherKeyDown => new GestureEvent.OtherKeyDown(at),
            InputKind.Click => new GestureEvent.Click(at),
            _ => new GestureEvent.Escape(),
        };
        // Esc only matters while something is recording; otherwise it belongs to the app in front.
        if (kind == InputKind.Escape && !dictation.IsRecording && !gesture.IsWaitingForRelease) return;
        Apply(gesture.Handle(e));
        if (kind == InputKind.TriggerUp && !dictation.IsRecording) dictation.RaisePendingIssue();
    }

    private void Apply(GestureAction? action)
    {
        switch (action)
        {
            case GestureAction.Start:
                dictation.Start();
                break;
            case GestureAction.Finish:
                dictation.Finish();
                break;
            case GestureAction.Cancel:
                dictation.Cancel(audible: true);
                break;
            case GestureAction.Discard:
                dictation.Cancel(audible: false);
                break;
        }
    }

    // Test only (with --fake-mic): one dictation started and stopped from code, without any key presses, so the whole
    // pipeline can be watched (for example for network use) without touching the user's keyboard or focus.
    public async void AutoDictate(TimeSpan length)
    {
        while (model.State == ModelState.Loading) await Task.Delay(200);
        Log.Info($"auto-dictate: model {model.State}");
        ToggleFromMenu();
        await Task.Delay(length);
        ToggleFromMenu();
    }

    private void ToggleFromMenu()
    {
        if (dictation.IsRecording)
        {
            gesture.Reset();
            dictation.Finish();
        }
        else
        {
            dictation.Start();
            if (dictation.IsRecording) gesture.RecordingStartedElsewhere();
            else dictation.RaisePendingIssue();
        }
    }

    private void UpdateStatus()
    {
        // A recording Sorla ended itself (the five-minute limit) must not leave the gesture waiting for a stop tap.
        if (!dictation.IsRecording && !gesture.IsWaitingForRelease) gesture.Reset();

        var key = settings.TriggerKey.DisplayName();
        var (status, mark) = true switch
        {
            _ when dictation.IsRecording => (Strings.Get("status.recording"), TrayMark.Recording),
            _ when dictation.IsBusy => (Strings.Get("status.transcribing"), TrayMark.Busy),
            _ when install.IsRunning => (Strings.Format("status.downloading", install.Progress?.Fraction ?? 0), TrayMark.Busy),
            _ when model.State == ModelState.Loading => (Strings.Get("status.loading"), TrayMark.Busy),
            _ when model.State == ModelState.Missing => (Strings.Get("status.noModel"), TrayMark.Problem),
            _ when model.State == ModelState.Failed => (Strings.Get("status.modelFailed"), TrayMark.Problem),
            _ => (Strings.Format(settings.RecordingMode == RecordingMode.Toggle ? "status.readyToggle" : "status.ready", key), TrayMark.Ready),
        };
        statusItem.Text = status;
        tray.Text = status.Length > 63 ? status[..63] : status;
        dictateItem.Text = Strings.Get(dictation.IsRecording ? "menu.stopDictation" : "menu.startDictation");
        if (mark != currentMark)
        {
            var old = tray.Icon;
            tray.Icon = Icons.TrayIcon(mark);
            old?.Dispose();
            currentMark = mark;
        }
    }

    private void ShowIssue(Issue issue)
    {
        if (issue == Issue.Silent)
        {
            // Often just a quiet tap, so it is said on the indicator rather than in a dialog.
            indicator.ShowMessage(Strings.Get("issue.silent.title"), TimeSpan.FromSeconds(3));
            return;
        }
        if (dismissedIssues.Contains(issue)) return;
        var sizeMb = ModelFiles.TotalBytes(settings.ModelVariant) / 1_000_000;
        var (title, body, action, run) = issue switch
        {
            Issue.NoMicrophone => ("issue.noMicrophone.title", Strings.Get("issue.noMicrophone.body"), "issue.openPrivacy", (Action)SettingsPages.Microphone),
            Issue.Silent => ("issue.silent.title", Strings.Get("issue.silent.body"), "issue.openSound", SettingsPages.Sound),
            Issue.ModelMissing => ("issue.modelMissing.title", Strings.Format("issue.modelMissing.body", sizeMb), "issue.download", () => { ShowWelcome(); _ = install.RunAsync(); }),
            Issue.ModelLoading => ("issue.modelLoading.title", Strings.Get("issue.modelLoading.body"), null, null),
            Issue.ModelFailed => ("issue.modelFailed.title", Strings.Format("issue.modelFailed.body", model.FailureMessage ?? ""), "menu.welcome", ShowWelcome),
            _ => ("issue.transcription.title", Strings.Get("issue.transcription.title"), null, (Action?)null),
        };
        indicator.HideIndicator();
        var page = new TaskDialogPage
        {
            Caption = "Sorla",
            Heading = Strings.Get(title),
            Text = body,
            Icon = TaskDialogIcon.Warning,
            AllowCancel = true,
        };
        var actionButton = action != null ? new TaskDialogButton(Strings.Get(action)) : null;
        var notNow = new TaskDialogButton(Strings.Get("issue.notNow"));
        if (actionButton != null) page.Buttons.Add(actionButton);
        page.Buttons.Add(notNow);
        var result = TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen);
        if (result == actionButton) run?.Invoke();
        else dismissedIssues.Add(issue);
    }

    public void ShowSettings()
    {
        if (settingsForm is { IsDisposed: false })
        {
            settingsForm.Activate();
            return;
        }
        settingsForm = new SettingsForm(settings, ApplySettings, () =>
        {
            if (model.IsInstalled) model.Reload();
            else ShowWelcome();
            UpdateStatus();
        });
        settingsForm.Show();
        settingsForm.Activate();
    }

    private void ApplySettings()
    {
        hook.TriggerVirtualKey = settings.TriggerKey.VirtualKey();
        hook.ForgetTriggerState();
        dictation.Cancel(audible: false);
        gesture = new PushToTalkGesture(settings.RecordingMode);
        microphone.KeepWarm = settings.MicrophoneReadiness.KeepWarm();
        if (appliedMicrophoneId != settings.MicrophoneId)
        {
            appliedMicrophoneId = settings.MicrophoneId;
            microphone.Reconfigure(() => createSource(settings.MicrophoneId));
        }
        else if (appliedReadiness != settings.MicrophoneReadiness && settings.MicrophoneReadiness != MicrophoneReadiness.Always)
        {
            // A shorter readiness takes effect now rather than after the old timer.
            microphone.Close();
        }
        appliedReadiness = settings.MicrophoneReadiness;
        PrepareMicrophoneIfAlways();
        dictation.SettingsChanged();
        UpdateStatus();
    }

    // "Always ready" keeps the microphone open from launch and after every unlock or wake.
    private void PrepareMicrophoneIfAlways()
    {
        if (settings.MicrophoneReadiness == MicrophoneReadiness.Always) microphone.Prepare();
    }

    public void ShowWelcome()
    {
        if (welcomeForm is { IsDisposed: false })
        {
            welcomeForm.Activate();
            return;
        }
        welcomeForm = new WelcomeForm(settings, model, install);
        welcomeForm.Show();
        welcomeForm.Activate();
    }

    private static void ShowAbout()
    {
        TaskDialog.ShowDialog(new TaskDialogPage
        {
            Caption = Strings.Get("about.title"),
            Heading = "Sorla",
            Text = Strings.Format("about.body", AppVersion.Current),
            Icon = TaskDialogIcon.Information,
        });
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            gesture.Reset();
            hook.ForgetTriggerState();
            dictation.Forget();
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
        {
            PrepareMicrophoneIfAlways();
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            gesture.Reset();
            hook.ForgetTriggerState();
            dictation.Forget();
        }
        else if (e.Mode == PowerModes.Resume)
        {
            PrepareMicrophoneIfAlways();
        }
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        install.Cancel();
        hook.Dispose();
        hotKeys.Dispose();
        dictation.Dispose();
        microphone.Dispose();
        tray.Visible = false;
        tray.Dispose();
        indicator.Dispose();
        feedback.Dispose();
        model.Dispose();
        base.ExitThreadCore();
    }
}
