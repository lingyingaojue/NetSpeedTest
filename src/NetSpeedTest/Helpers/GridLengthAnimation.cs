using System.Windows;
using System.Windows.Media.Animation;

namespace NetSpeedTest.Helpers;

public class GridLengthAnimation : AnimationTimeline
{
    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(GridLength), typeof(GridLengthAnimation));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(GridLength), typeof(GridLengthAnimation));

    public GridLength From
    {
        get => (GridLength)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public GridLength To
    {
        get => (GridLength)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public override Type TargetPropertyType => typeof(GridLength);

    protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        var from = (GridLength)GetValue(FromProperty);
        var to = (GridLength)GetValue(ToProperty);
        var progress = animationClock.CurrentProgress ?? 0.0;

        if (from.GridUnitType != to.GridUnitType)
            return progress < 0.5 ? from : to;

        return new GridLength(
            from.Value + (to.Value - from.Value) * progress,
            from.GridUnitType);
    }
}
