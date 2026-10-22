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
        switch (state)
        {
            case DictationState.Listening:
                _startedAt = Stopwatch.GetTimestamp();
                Listening.Visibility = Visibility.Visible;
                Message.Visibility = Visibility.Collapsed;
                _tick.Start();
                break;
            case DictationState.Recognizing:
                SetMessage("", "#60CDFF", "Распознаю…", "");
                break;
            case DictationState.Inserted:
                SetMessage("", "#6CCB5F", "Вставлено", "");
                HideAfter(TimeSpan.FromSeconds(0.5));
                break;
            case DictationState.NotHeard:
                SetMessage("", "#FCE100", "Не расслышал — скажите ещё раз", "или проверьте микрофон");
                HideAfter(TimeSpan.FromSeconds(3));
                break;
            case DictationState.ModelLoading:
                return; // loading is shown in the window that started it
            default:
                _tick.Stop();
                Hide();
                return;
        }

        if (!IsVisible)
        {
            base.Show();
        }

        UpdateLayout();
        PlaceAtBottom();
    }

    internal void ShowLearned(string wrong, string correct)
    {
        _hide.Stop();
        SetMessage("\uE82D", "#60CDFF", $"Запомнил: {wrong} → {correct}", "отменить — в Настройки → Словарь");
        if (!IsVisible)
        {
            base.Show();
        }

        UpdateLayout();
        PlaceAtBottom();
        HideAfter(TimeSpan.FromSeconds(4));
    }

    private void SetMessage(string glyph, string color, string text, string hint)
    {
        _tick.Stop();
        Listening.Visibility = Visibility.Collapsed;
        Message.Visibility = Visibility.Visible;
        MessageIcon.Text = glyph;
        MessageIcon.Foreground = (Brush)new BrushConverter().ConvertFromString(color)!;
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

    private void PlaceAtBottom()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 24;
    }
}
