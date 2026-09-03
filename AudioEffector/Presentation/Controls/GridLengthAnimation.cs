using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace AudioEffector.Presentation.Controls;

/// <summary>
/// Gridの行や列（GridLength）のサイズ変更をスムーズに行うためのアニメーションタイムラインクラス
/// </summary>
public class GridLengthAnimation : AnimationTimeline
{
    public static readonly DependencyProperty FromProperty = DependencyProperty.Register("From", typeof(GridLength), typeof(GridLengthAnimation));
    public GridLength From
    {
        get { return (GridLength)GetValue(FromProperty); }
        set { SetValue(FromProperty, value); }
    }

    public static readonly DependencyProperty ToProperty = DependencyProperty.Register("To", typeof(GridLength), typeof(GridLengthAnimation));
    public GridLength To
    {
        get { return (GridLength)GetValue(ToProperty); }
        set { SetValue(ToProperty, value); }
    }

    public static readonly DependencyProperty EasingFunctionProperty = DependencyProperty.Register("EasingFunction", typeof(IEasingFunction), typeof(GridLengthAnimation));
    public IEasingFunction EasingFunction
    {
        get { return (IEasingFunction)GetValue(EasingFunctionProperty); }
        set { SetValue(EasingFunctionProperty, value); }
    }

    public override Type TargetPropertyType => typeof(GridLength);

    protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        if (!animationClock.CurrentProgress.HasValue)
            return To;

        double progress = animationClock.CurrentProgress.Value;
        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        double fromVal = From.Value;
        double toVal = To.Value;

        if (fromVal > toVal)
        {
            // 閉じる時
            double newVal = Math.Max(0.0, fromVal - ((fromVal - toVal) * progress));
            return new GridLength(newVal, GridUnitType.Star);
        }
        else
        {
            // 開く時
            double newVal = Math.Max(0.0, fromVal + ((toVal - fromVal) * progress));
            return new GridLength(newVal, GridUnitType.Star);
        }
    }
}
