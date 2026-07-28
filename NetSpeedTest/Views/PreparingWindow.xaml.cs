using System;
using System.Windows;
using System.Windows.Threading;

namespace NetSpeedTest.Views;

public partial class PreparingWindow : Window
{
    private int _targetPercent;
    private readonly DispatcherTimer _animTimer = new() { Interval = TimeSpan.FromMilliseconds(25) };

    public PreparingWindow()
    {
        InitializeComponent();
        _animTimer.Tick += (_, _) =>
        {
            if (ProgBar.Value < _targetPercent)
                ProgBar.Value = Math.Min(ProgBar.Value + 2, _targetPercent);
            if (_targetPercent >= 100 && ProgBar.Value >= 100)
                _animTimer.Stop();
        };
        _animTimer.Start();
        Closed += (_, _) => _animTimer.Stop();
    }

    public void UpdateProgress(int percent, string status)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _targetPercent = percent;
            StatusText.Text = status;
        });
    }
}
