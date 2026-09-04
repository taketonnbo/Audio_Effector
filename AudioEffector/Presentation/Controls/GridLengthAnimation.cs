using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace AudioEffector.Presentation.Controls;

/// <summary>
/// Gridの行や列（GridLength）のサイズ変更をスムーズに行うためのアニメーションタイムラインクラス
/// </summary>
public class GridLengthAnimation : AnimationTimeline
{
    /// <summary>
    /// 開始サイズを表す依存関係プロパティ
    /// </summary>
    public static readonly DependencyProperty FromProperty = DependencyProperty.Register("From", typeof(GridLength), typeof(GridLengthAnimation));

    /// <summary>
    /// アニメーション開始時のサイズを取得または設定します
    /// </summary>
    public GridLength From
    {
        get { return (GridLength)GetValue(FromProperty); }
        set { SetValue(FromProperty, value); }
    }

    /// <summary>
    /// 終了サイズを表す依存関係プロパティ
    /// </summary>
    public static readonly DependencyProperty ToProperty = DependencyProperty.Register("To", typeof(GridLength), typeof(GridLengthAnimation));

    /// <summary>
    /// アニメーション終了時のサイズを取得または設定します
    /// </summary>
    public GridLength To
    {
        get { return (GridLength)GetValue(ToProperty); }
        set { SetValue(ToProperty, value); }
    }

    /// <summary>
    /// イージング関数を表す依存関係プロパティ
    /// </summary>
    public static readonly DependencyProperty EasingFunctionProperty = DependencyProperty.Register("EasingFunction", typeof(IEasingFunction), typeof(GridLengthAnimation));

    /// <summary>
    /// アニメーションのイージング関数を取得または設定します
    /// </summary>
    public IEasingFunction EasingFunction
    {
        get { return (IEasingFunction)GetValue(EasingFunctionProperty); }
        set { SetValue(EasingFunctionProperty, value); }
    }

    /// <summary>
    /// アニメーション対象のプロパティの型を取得します
    /// </summary>
    public override Type TargetPropertyType => typeof(GridLength);

    /// <summary>
    /// クラスの新しいインスタンスを作成します
    /// </summary>
    /// <returns>新しいインスタンス</returns>
    protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

    /// <summary>
    /// アニメーションの現在値を計算して取得します
    /// </summary>
    /// <param name="defaultOriginValue">アニメーションの基準となる元の値</param>
    /// <param name="defaultDestinationValue">アニメーションの基準となる目標値</param>
    /// <param name="animationClock">アニメーションの進行状況を表すクロック</param>
    /// <returns>計算された現在値</returns>
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
