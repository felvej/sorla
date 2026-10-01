using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Sorla.Core.Audio;
using Sorla.Platform;

namespace Sorla.UI;

public enum IndicatorState
{
    Hidden,
    Recording,
    Transcribing,
    Message,
}

// A small black island hanging from the top edge of the screen with a red dot and a live waveform, like Sorla on the
// Mac. It never takes focus and lets clicks through, so the text still lands in the app being dictated into.
public sealed class RecordingIndicator : Form
{
    private const int BarCount = 7;
    private const float BaseWidth = 132;
    private const float BaseHeight = 34;
    private const float MessageMaxWidth = 460;

    private static readonly Color BarTop = Color.White;
    private static readonly Color BarBottom = Color.FromArgb(214, 235, 255);

    private readonly System.Windows.Forms.Timer animation = new() { Interval = 16 };
    private readonly BandSmoother smoother = new();
    private float[] bands = new float[SpectrumAnalyzer.BandCount];
    private IndicatorState state = IndicatorState.Hidden;
    private string message = "";
    private DateTime messageUntil;
    private bool warning;
    private bool muted;
    private Screen? screen;

    public RecordingIndicator()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        animation.Tick += (_, _) => Render();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    public IndicatorState State => state;

    public void ShowRecording()
    {
        warning = false;
        muted = false;
        smoother.Reset();
        bands = new float[SpectrumAnalyzer.BandCount];
        SetState(IndicatorState.Recording);
    }

    public void ShowTranscribing() => SetState(IndicatorState.Transcribing);

    public void ShowWarning() => warning = true;

    public void SetMuted(bool value) => muted = value;

    public void ShowMessage(string text, TimeSpan duration)
    {
        message = text;
        messageUntil = DateTime.UtcNow + duration;
        SetState(IndicatorState.Message);
    }

    public void HideIndicator() => SetState(IndicatorState.Hidden);

    // Called from the UI thread with the newest spectrum of the microphone.
    public void UpdateSpectrum(float[] spectrum) => bands = smoother.Update(spectrum);

    private void SetState(IndicatorState next)
    {
        state = next;
        if (next == IndicatorState.Hidden)
        {
            animation.Stop();
            Hide();
            return;
        }
        // The screen with the mouse is the one being looked at.
        screen = Screen.FromPoint(Cursor.Position);
        if (!Visible)
        {
            Show();
        }
        animation.Start();
        Render();
    }

    private static double Time => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private void Render()
    {
        if (state == IndicatorState.Message && DateTime.UtcNow >= messageUntil)
        {
            HideIndicator();
            return;
        }
        var target = screen ?? Screen.PrimaryScreen!;
        var scale = DeviceDpi / 96f;
        using var font = new Font("Segoe UI", 9.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        var width = BaseWidth * scale;
        if (state == IndicatorState.Message)
        {
            using var measure = Graphics.FromHwnd(0);
            var textSize = measure.MeasureString(message, font, (int)(MessageMaxWidth * scale));
            width = Math.Max(width, textSize.Width + 32 * scale);
        }
        var height = state == IndicatorState.Message ? Math.Max(BaseHeight * scale, MeasureHeight(font, width, scale)) : BaseHeight * scale;
        var w = (int)Math.Ceiling(width);
        var h = (int)Math.Ceiling(height);

        using var bitmap = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using (var shape = IslandPath(w, h, Math.Min(h / 2f, 18 * scale)))
            using (var fill = new SolidBrush(Color.FromArgb(245, 0, 0, 0)))
            {
                g.FillPath(fill, shape);
            }
            switch (state)
            {
                case IndicatorState.Recording:
                    DrawDot(g, scale, h);
                    DrawBars(g, scale, w, h, VisualizerBars.Magnitudes(bands, Time, BarCount), warning || muted ? 0.35f : 1f);
                    break;
                case IndicatorState.Transcribing:
                    var shimmer = VisualizerBars.Shimmer(Time, BarCount);
                    DrawBars(g, scale, w, h, shimmer.Select(s => 0.12f + 0.25f * s).ToArray(), 1f, shimmer);
                    break;
                case IndicatorState.Message:
                    using (var text = new SolidBrush(Color.White))
                    {
                        var layout = new RectangleF(16 * scale, 0, w - 32 * scale, h);
                        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString(message, font, text, layout, format);
                    }
                    break;
            }
        }

        var x = target.Bounds.Left + (target.Bounds.Width - w) / 2;
        var y = target.Bounds.Top;
        Present(bitmap, x, y);
    }

    private float MeasureHeight(Font font, float width, float scale)
    {
        using var measure = Graphics.FromHwnd(0);
        return measure.MeasureString(message, font, (int)(width - 32 * scale)).Height + 16 * scale;
    }

    private static GraphicsPath IslandPath(int w, int h, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddLine(0, 0, w, 0);
        path.AddLine(w, 0, w, h - radius);
        path.AddArc(w - d, h - d, d, d, 0, 90);
        path.AddLine(w - radius, h, radius, h);
        path.AddArc(0, h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawDot(Graphics g, float scale, int h)
    {
        var size = 8 * scale;
        var pulse = 0.75f + 0.25f * (float)Math.Sin(Time * 4);
        using var brush = new SolidBrush(Color.FromArgb((int)(255 * pulse), 255, 59, 48));
        g.FillEllipse(brush, 16 * scale, (h - size) / 2, size, size);
    }

    private static void DrawBars(Graphics g, float scale, int w, int h, float[] magnitudes, float opacity, float[]? highlight = null)
    {
        var barWidth = 3.5f * scale;
        var gap = 4f * scale;
        var total = BarCount * barWidth + (BarCount - 1) * gap;
        var left = (w - total) / 2 + 10 * scale;
        var maxHeight = h - 12 * scale;
        var minHeight = 3.5f * scale;
        for (var i = 0; i < BarCount; i++)
        {
            var barHeight = minHeight + magnitudes[i] * (maxHeight - minHeight);
            var rect = new RectangleF(left + i * (barWidth + gap), (h - barHeight) / 2, barWidth, barHeight);
            var alpha = (int)(255 * opacity * (highlight == null ? 1 : 0.45f + 0.55f * highlight[i]));
            using var brush = new LinearGradientBrush(new RectangleF(rect.X, rect.Y - 1, rect.Width, rect.Height + 2),
                Color.FromArgb(alpha, BarTop), Color.FromArgb(alpha, BarBottom), LinearGradientMode.Vertical);
            using var path = Icons.RoundedRect(rect, barWidth / 2);
            g.FillPath(brush, path);
        }
    }

    // Per-pixel alpha through UpdateLayeredWindow, so the rounded corners are smooth on any background.
    private void Present(Bitmap bitmap, int x, int y)
    {
        var screenDc = Native.GetDC(0);
        var memoryDc = Native.CreateCompatibleDC(screenDc);
        var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        var old = Native.SelectObject(memoryDc, hBitmap);
        try
        {
            var size = new Native.SIZE { cx = bitmap.Width, cy = bitmap.Height };
            var source = new Native.POINT();
            var destination = new Native.POINT { X = x, Y = y };
            var blend = new Native.BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = Native.AC_SRC_ALPHA };
            Native.UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.SelectObject(memoryDc, old);
            Native.DeleteObject(hBitmap);
            Native.DeleteDC(memoryDc);
            Native.ReleaseDC(0, screenDc);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) animation.Dispose();
        base.Dispose(disposing);
    }
}
