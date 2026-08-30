using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace AudioEffector.Controls
{
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

        public override Type TargetPropertyType => typeof(GridLength);

        protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
        {
            if (!animationClock.CurrentProgress.HasValue)
                return To;

            double fromVal = From.Value;
            double toVal = To.Value;

            if (fromVal > toVal)
            {
                // 閉じる時
                double newVal = fromVal - ((fromVal - toVal) * animationClock.CurrentProgress.Value);
                return new GridLength(newVal, GridUnitType.Star);
            }
            else
            {
                // 開く時
                double newVal = fromVal + ((toVal - fromVal) * animationClock.CurrentProgress.Value);
                return new GridLength(newVal, GridUnitType.Star);
            }
        }
    }
}
