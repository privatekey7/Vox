using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Vox.App.Services;

namespace Vox.App.Views;

/// <summary>
/// Shows what Vox is doing near the bottom of the screen. The window is click-through and never
/// activated (WS_EX_NOACTIVATE | WS_EX_TRANSPARENT), so focus stays in the user's app.
/// </summary>
public partial class RecordingPill : Window
{
    private const int BarCount = 6;
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _hide = new();
    private long _startedAt;
    private float _level;
    private DictationState _shown;

    internal RecordingPill(AudioCapture audio)
    {
        InitializeComponent();
        for (var i = 0; i < BarCount; i++)
        {
            _bars[i] = new Rectangle { Width = 3, Height = 4, RadiusX = 1.5, RadiusY = 1.5, Fill = Brushes.White, Margin = new Thickness(0, 0, 3, 0) };
            Bars.Items.Add(_bars[i]);
        }

        audio.Level += peak => _level = Math.Max(_level, peak);
        _tick.Tick += (_, _) => Animate();
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            Hide();
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE,
                style | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW);
        };
    }

    internal void Show(DictationState state)
    {
        _hide.Stop();
        var previous = _shown;
        _shown = state;
        switch (state)
        {
            case DictationState.Listening:
                if (previous != DictationState.MicrophoneSilent)
                {
                    _startedAt = Stopwatch.GetTimestamp(); // not when the microphone came back mid-press
                }

                Listening.Visibility = Visibility.Visible;
                Message.Visibility = Visibility.Collapsed;
                _tick.Start();
                break;
            case DictationState.Recognizing:
                SetMessage("", "#60CDFF", "Распознаю…", "");
                break;
            case DictationState.Inserted:
                SetMessage(Mark, "", "Вставлено", "");
                HideAfter(TimeSpan.FromSeconds(0.5));
                break;
            case DictationState.NotHeard:
                SetMessage("", "#FCE100", "Не расслышал", "");
                HideAfter(TimeSpan.FromSeconds(0.5)); // owner: as quick as "Вставлено"
                break;
            case DictationState.NotInserted:
                // Short like "Не расслышал" (owner: a long label spoils the pill). The text is in the history.
                SetMessage("", "#FCE100", "Не вставилось", "");
                HideAfter(TimeSpan.FromSeconds(1.5));
                break;
            case DictationState.MicrophoneSilent:
                SetMessage("", "#FCE100", "Микрофон молчит", "");
                if (previous != DictationState.Listening)
                {
                    HideAfter(TimeSpan.FromSeconds(1.5)); // after the release; while the key is held it stays
                }

                break;
            case DictationState.ModelNotReady:
                SetMessage("\uE916", "#60CDFF", "Модель ещё загружается…", "подождите немного");
                HideAfter(TimeSpan.FromSeconds(2.5));
                break;
            case DictationState.ModelLoading:
                return; // loading is shown in the window that started it
            default:
                _tick.Stop();
                Hide();
                return;
        }

        ShowAtBottom();
    }

    internal void ShowLearned(string wrong, string correct, bool fixedTarget)
    {
        _hide.Stop();
        SetMessage(Mark, "", $"{(fixedTarget ? "Исправил" : "Запомнил")}: {wrong} → {correct}", "отменить: меню Vox в трее");
        ShowAtBottom();
        HideAfter(TimeSpan.FromSeconds(2)); // owner: long enough to read, short enough not to bother
    }

    internal void ShowUndone(string wrong, string correct)
    {
        _hide.Stop();
        SetMessage("\uE7A7", "#60CDFF", $"Отменено: {wrong} → {correct}", "");
        ShowAtBottom();
        HideAfter(TimeSpan.FromSeconds(2));
    }

    // The Vox mark instead of a glyph ("Вставлено", "Запомнил"; owner's variant B).
    private const string Mark = "mark";

    private void SetMessage(string glyph, string color, string text, string hint)
    {
        _tick.Stop();
        Listening.Visibility = Visibility.Collapsed;
        Message.Visibility = Visibility.Visible;
        var mark = glyph == Mark;
        MessageMark.Visibility = mark ? Visibility.Visible : Visibility.Collapsed;
        MessageIcon.Visibility = mark ? Visibility.Collapsed : Visibility.Visible;
        if (!mark)
        {
            MessageIcon.Text = glyph;
            MessageIcon.Foreground = (Brush)new BrushConverter().ConvertFromString(color)!;
        }
        MessageText.Text = text;
        MessageHint.Text = hint;
        MessageHint.Visibility = hint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideAfter(TimeSpan delay)
    {
        _hide.Interval = delay;
        _hide.Start();
    }

    private void Animate()
    {
        var elapsed = Stopwatch.GetElapsedTime(_startedAt);
        Timer.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        var level = Math.Min(1f, _level * 6f); // speech peaks are usually well below full scale
        _level = 0;
        for (var i = 0; i < BarCount; i++)
        {
            var shape = 0.55 + 0.45 * Math.Sin(i * 1.7 + elapsed.TotalSeconds * 9);
            _bars[i].Height = 4 + 14 * level * shape;
        }
    }

    private void ShowAtBottom()
    {
        if (!IsVisible)
        {
            // Invisible until placed: a fresh window flashed at its default spot for a frame.
            Opacity = 0;
            base.Show();
        }

        UpdateLayout();
        PlaceAtBottom();
        Opacity = 1;
    }

    private void PlaceAtBottom()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 24;
    }
}
