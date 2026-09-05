using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AudioEffector.Presentation.Controls;

/// <summary>
/// テキストが表示枠幅を超えて見切れている場合に、マウスオーバー時のみ右から左へ滑らかにループスクロール（マーキー表示）するカスタムコントロール。
/// </summary>
public class MarqueeTextBlock : ContentControl
{
    private readonly Grid _container;
    private readonly TextBlock _textBlock;
    private readonly TranslateTransform _transform;
    private Storyboard? _storyboard;
    private DispatcherTimer? _hoverTimer;

    #region Dependency Properties

    /// <summary>
    /// 表示するテキストを識別する依存関係プロパティ。
    /// </summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure, OnTextPropertyChanged));

    /// <summary>
    /// スクロール速度（ピクセル/秒、既定値: 40.0）を識別する依存関係プロパティ。
    /// </summary>
    public static readonly DependencyProperty ScrollSpeedProperty =
        DependencyProperty.Register(
            nameof(ScrollSpeed),
            typeof(double),
            typeof(MarqueeTextBlock),
            new PropertyMetadata(40.0));

    /// <summary>
    /// マウスホバーからスクロール開始までの待機時間（既定値: 500ms）を識別する依存関係プロパティ。
    /// </summary>
    public static readonly DependencyProperty HoverDelayProperty =
        DependencyProperty.Register(
            nameof(HoverDelay),
            typeof(TimeSpan),
            typeof(MarqueeTextBlock),
            new PropertyMetadata(TimeSpan.FromMilliseconds(500)));

    /// <summary>
    /// スクロールが末尾に達した後の待機時間（既定値: 1200ms）を識別する依存関係プロパティ。
    /// </summary>
    public static readonly DependencyProperty EndDelayProperty =
        DependencyProperty.Register(
            nameof(EndDelay),
            typeof(TimeSpan),
            typeof(MarqueeTextBlock),
            new PropertyMetadata(TimeSpan.FromMilliseconds(1200)));

    /// <summary>
    /// 親要素などのホバー状態と連動させるための外部ホバーフラグを識別する依存関係プロパティ。
    /// </summary>
    public static readonly DependencyProperty IsHoveredProperty =
        DependencyProperty.Register(
            nameof(IsHovered),
            typeof(bool),
            typeof(MarqueeTextBlock),
            new PropertyMetadata(false, OnIsHoveredChanged));

    /// <summary>
    /// 現在マーキースクロールが実行中かどうかを示す読み取り専用依存関係プロパティのキー。
    /// </summary>
    private static readonly DependencyPropertyKey IsScrollingPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsScrolling),
            typeof(bool),
            typeof(MarqueeTextBlock),
            new PropertyMetadata(false));

    /// <summary>
    /// 現在マーキースクロールが実行中かどうかを示す依存関係プロパティ。
    /// </summary>
    public static readonly DependencyProperty IsScrollingProperty =
        IsScrollingPropertyKey.DependencyProperty;

    #endregion

    #region Properties

    /// <summary>
    /// 表示するテキストを取得または設定します。
    /// </summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// スクロール速度（ピクセル/秒）を取得または設定します。
    /// </summary>
    public double ScrollSpeed
    {
        get => (double)GetValue(ScrollSpeedProperty);
        set => SetValue(ScrollSpeedProperty, value);
    }

    /// <summary>
    /// マウスホバーからスクロール開始までの待機時間を取得または設定します。
    /// </summary>
    public TimeSpan HoverDelay
    {
        get => (TimeSpan)GetValue(HoverDelayProperty);
        set => SetValue(HoverDelayProperty, value);
    }

    /// <summary>
    /// スクロールが末尾に達した後の待機時間を取得または設定します。
    /// </summary>
    public TimeSpan EndDelay
    {
        get => (TimeSpan)GetValue(EndDelayProperty);
        set => SetValue(EndDelayProperty, value);
    }

    /// <summary>
    /// 親要素などのホバー状態と連動させるための外部ホバーフラグを取得または設定します。
    /// </summary>
    public bool IsHovered
    {
        get => (bool)GetValue(IsHoveredProperty);
        set => SetValue(IsHoveredProperty, value);
    }

    /// <summary>
    /// 現在マーキースクロールが実行中かどうかを取得します。
    /// </summary>
    public bool IsScrolling
    {
        get => (bool)GetValue(IsScrollingProperty);
        private set => SetValue(IsScrollingPropertyKey, value);
    }

    #endregion

    /// <summary>
    /// <see cref="MarqueeTextBlock"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    public MarqueeTextBlock()
    {
        Focusable = false;
        IsTabStop = false;

        _transform = new TranslateTransform();
        _textBlock = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            RenderTransform = _transform,
            VerticalAlignment = VerticalAlignment.Center
        };

        _container = new Grid
        {
            ClipToBounds = true,
            Background = Brushes.Transparent // マウスイベントを確実に受信するため透明背景
        };
        _container.Children.Add(_textBlock);

        Content = _container;

        Loaded += MarqueeTextBlock_Loaded;
        Unloaded += MarqueeTextBlock_Unloaded;
        SizeChanged += MarqueeTextBlock_SizeChanged;
        MouseEnter += MarqueeTextBlock_MouseEnter;
        MouseLeave += MarqueeTextBlock_MouseLeave;
    }

    #region Event Handlers & Lifecycle

    private void MarqueeTextBlock_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateTextBlockStyle();
    }

    private void MarqueeTextBlock_Unloaded(object sender, RoutedEventArgs e)
    {
        StopMarquee();
    }

    private void MarqueeTextBlock_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsActiveHover())
        {
            RestartMarqueeIfOverflowing();
        }
    }

    private void MarqueeTextBlock_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        HandleHoverStarted();
    }

    private void MarqueeTextBlock_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!IsHovered)
        {
            HandleHoverEnded();
        }
    }

    private static void OnIsHoveredChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeTextBlock control)
        {
            if ((bool)e.NewValue || control.IsMouseOver)
            {
                control.HandleHoverStarted();
            }
            else
            {
                control.HandleHoverEnded();
            }
        }
    }

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeTextBlock control)
        {
            control._textBlock.Text = e.NewValue as string ?? string.Empty;
            if (control.IsActiveHover())
            {
                control.RestartMarqueeIfOverflowing();
            }
            else
            {
                control.ResetToStaticView();
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == ForegroundProperty)
        {
            _textBlock.Foreground = Foreground;
        }
        else if (e.Property == FontSizeProperty)
        {
            _textBlock.FontSize = FontSize;
        }
        else if (e.Property == FontWeightProperty)
        {
            _textBlock.FontWeight = FontWeight;
        }
        else if (e.Property == FontFamilyProperty)
        {
            _textBlock.FontFamily = FontFamily;
        }
        else if (e.Property == FontStyleProperty)
        {
            _textBlock.FontStyle = FontStyle;
        }
        else if (e.Property == HorizontalContentAlignmentProperty)
        {
            _textBlock.HorizontalAlignment = HorizontalContentAlignment;
        }
    }

    private void UpdateTextBlockStyle()
    {
        _textBlock.Foreground = Foreground;
        _textBlock.FontSize = FontSize;
        _textBlock.FontWeight = FontWeight;
        _textBlock.FontFamily = FontFamily;
        _textBlock.FontStyle = FontStyle;
        _textBlock.HorizontalAlignment = HorizontalContentAlignment;
    }

    #endregion

    #region Marquee Logic

    private bool IsActiveHover() => IsMouseOver || IsHovered;

    private void HandleHoverStarted()
    {
        if (!IsOverflowing())
        {
            return;
        }

        StopMarquee();

        // 指定の遅延時間待機してからスクロールを開始する
        _hoverTimer = new DispatcherTimer
        {
            Interval = HoverDelay
        };
        _hoverTimer.Tick += (s, args) =>
        {
            _hoverTimer?.Stop();
            _hoverTimer = null;

            if (IsActiveHover() && IsOverflowing())
            {
                StartMarqueeAnimation();
            }
        };
        _hoverTimer.Start();
    }

    private void HandleHoverEnded()
    {
        StopMarquee();
        ResetToStaticView();
    }

    /// <summary>
    /// 現在の表示枠幅に対してテキストが見切れているかどうかを判定します。
    /// </summary>
    /// <returns>見切れている場合は true、それ以外は false。</returns>
    public bool IsOverflowing()
    {
        if (string.IsNullOrEmpty(Text) || ActualWidth <= 0)
        {
            return false;
        }

        var textWidth = MeasureTextWidth();
        return textWidth > ActualWidth;
    }

    /// <summary>
    /// 現在のフォント設定におけるテキストの描画幅を計測します。
    /// </summary>
    /// <returns>テキストの幅（ピクセル）。</returns>
    public double MeasureTextWidth()
    {
        var formattedText = new FormattedText(
            Text ?? string.Empty,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretches.Normal),
            FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        return formattedText.WidthIncludingTrailingWhitespace;
    }

    private void RestartMarqueeIfOverflowing()
    {
        StopMarquee();
        if (IsOverflowing())
        {
            StartMarqueeAnimation();
        }
        else
        {
            ResetToStaticView();
        }
    }

    private void StartMarqueeAnimation()
    {
        var textWidth = MeasureTextWidth();
        var availableWidth = ActualWidth;

        if (textWidth <= availableWidth || availableWidth <= 0)
        {
            ResetToStaticView();
            return;
        }

        // スクロール中は文字を省略せず左揃えで表示
        _textBlock.TextTrimming = TextTrimming.None;
        _textBlock.HorizontalAlignment = HorizontalAlignment.Left;

        var overflowDistance = textWidth - availableWidth + 15.0; // 少し余白を持たせる
        var scrollDuration = TimeSpan.FromSeconds(Math.Max(0.5, overflowDistance / Math.Max(10.0, ScrollSpeed)));
        var pauseDuration = EndDelay;
        var resetDuration = TimeSpan.FromMilliseconds(200);
        var loopWaitDuration = TimeSpan.FromMilliseconds(600);

        var totalDuration = scrollDuration + pauseDuration + resetDuration + loopWaitDuration;

        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(totalDuration),
            RepeatBehavior = RepeatBehavior.Forever
        };

        // 1. 開始位置 (0)
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));

        // 2. 左へスクロール完了位置 (-(overflowDistance))
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflowDistance, KeyTime.FromTimeSpan(scrollDuration)));

        // 3. 末尾表示位置で一時静止
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(-overflowDistance, KeyTime.FromTimeSpan(scrollDuration + pauseDuration)));

        // 4. 先頭位置 (0) へリセット
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(scrollDuration + pauseDuration + resetDuration)));

        // 5. 先頭位置で待機して再ループ
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(totalDuration)));

        _storyboard = new Storyboard();
        _storyboard.Children.Add(animation);

        Storyboard.SetTarget(_storyboard, _transform);
        Storyboard.SetTargetProperty(_storyboard, new PropertyPath(TranslateTransform.XProperty));

        _storyboard.Begin();
        IsScrolling = true;
    }

    private void StopMarquee()
    {
        _hoverTimer?.Stop();
        _hoverTimer = null;

        if (_storyboard != null)
        {
            _storyboard.Stop();
            _storyboard = null;
        }

        IsScrolling = false;
    }

    private void ResetToStaticView()
    {
        _transform.X = 0;
        _textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        _textBlock.HorizontalAlignment = HorizontalContentAlignment;
    }

    #endregion
}
