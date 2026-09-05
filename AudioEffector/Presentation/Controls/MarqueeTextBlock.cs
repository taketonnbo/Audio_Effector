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
    private readonly TextBlock _staticTextBlock;
    private readonly Canvas _scrollCanvas;
    private readonly TextBlock _scrollTextBlock1;
    private readonly TextBlock _scrollTextBlock2;
    private readonly TranslateTransform _transform;
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

        _staticTextBlock = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        _scrollTextBlock1 = new TextBlock
        {
            TextTrimming = TextTrimming.None
        };

        _scrollTextBlock2 = new TextBlock
        {
            TextTrimming = TextTrimming.None
        };

        _scrollCanvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            RenderTransform = _transform,
            Visibility = Visibility.Collapsed
        };
        _scrollCanvas.Children.Add(_scrollTextBlock1);
        _scrollCanvas.Children.Add(_scrollTextBlock2);

        _container = new Grid
        {
            ClipToBounds = true,
            Background = Brushes.Transparent // マウスイベントを確実に受信するため透明背景
        };
        _container.Children.Add(_staticTextBlock);
        _container.Children.Add(_scrollCanvas);

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
            var text = e.NewValue as string ?? string.Empty;
            control._staticTextBlock.Text = text;
            control._scrollTextBlock1.Text = text;
            control._scrollTextBlock2.Text = text;

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

        if (e.Property == ForegroundProperty ||
            e.Property == FontSizeProperty ||
            e.Property == FontWeightProperty ||
            e.Property == FontFamilyProperty ||
            e.Property == FontStyleProperty ||
            e.Property == HorizontalContentAlignmentProperty)
        {
            UpdateTextBlockStyle();
        }
    }

    private void UpdateTextBlockStyle()
    {
        ApplyStyleToTextBlock(_staticTextBlock);
        ApplyStyleToTextBlock(_scrollTextBlock1);
        ApplyStyleToTextBlock(_scrollTextBlock2);
        _staticTextBlock.HorizontalAlignment = HorizontalContentAlignment;
    }

    private void ApplyStyleToTextBlock(TextBlock textBlock)
    {
        textBlock.Foreground = Foreground;
        textBlock.FontSize = FontSize;
        textBlock.FontWeight = FontWeight;
        textBlock.FontFamily = FontFamily;
        textBlock.FontStyle = FontStyle;
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

        // 最新のスタイル（Foreground, FontSize等）を確実に全TextBlockへ適用
        UpdateTextBlockStyle();

        // 実際の TextBlock の描画サイズを計測
        _scrollTextBlock1.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _scrollTextBlock2.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var measuredWidth = Math.Max(textWidth, _scrollTextBlock1.DesiredSize.Width);

        // 垂直中央揃えのための Y 座標計算（_staticTextBlock の高さを基準にして完全に一致させる）
        var containerHeight = ActualHeight > 0 ? ActualHeight : _staticTextBlock.ActualHeight;
        var textHeight = _scrollTextBlock1.DesiredSize.Height > 0 ? _scrollTextBlock1.DesiredSize.Height : _staticTextBlock.ActualHeight;
        var top = Math.Max(0.0, (containerHeight - textHeight) / 2.0);

        // テキスト間の余白（約60pxまたは表示幅の35%）
        var gap = Math.Max(60.0, availableWidth * 0.35);

        // Canvas 内に 2 つの TextBlock を配置（親の幅に制限されず全文字が確実にレンダリングされる）
        Canvas.SetLeft(_scrollTextBlock1, 0.0);
        Canvas.SetTop(_scrollTextBlock1, top);

        Canvas.SetLeft(_scrollTextBlock2, measuredWidth + gap);
        Canvas.SetTop(_scrollTextBlock2, top);

        // 静止用テキストは Hidden（非表示にするがレイアウトサイズは維持して親コンテナの縮小・チャタリングを防止）
        _staticTextBlock.Visibility = Visibility.Hidden;
        _scrollCanvas.Visibility = Visibility.Visible;

        // 末尾まで確実に表示するためのスクロール距離（少し余裕を持たせる）
        var overflowDistance = (measuredWidth - availableWidth) + 30.0;
        var scrollSpeed = Math.Max(10.0, ScrollSpeed);

        // 1. 先頭から末尾までのスクロール時間
        var tScroll1 = TimeSpan.FromSeconds(Math.Max(0.5, overflowDistance / scrollSpeed));
        // 2. 末尾での静止時間（1.2秒）
        var endPause = EndDelay;
        // 3. 末尾から頭（_scrollTextBlock2）が初期位置 X=0 に到達するまでのスクロール時間（同じ速度）
        var returnDistance = (measuredWidth + gap) - overflowDistance;
        var tScroll2 = TimeSpan.FromSeconds(Math.Max(0.5, returnDistance / scrollSpeed));
        // 4. 頭（初期位置 X=0）に復帰した状態での静止時間（1.2秒）
        var returnPause = EndDelay;

        var t0 = TimeSpan.Zero;
        var t1 = tScroll1;
        var t2 = t1 + endPause;
        var t3 = t2 + tScroll2;
        var t4 = t3 + returnPause;

        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(t4),
            RepeatBehavior = RepeatBehavior.Forever
        };

        // 1. 開始: X=0 から末尾 (-overflowDistance) まで等速スクロール
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(t0)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflowDistance, KeyTime.FromTimeSpan(t1)));

        // 2. 末尾で 1.2 秒間静止
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(-overflowDistance, KeyTime.FromTimeSpan(t2)));

        // 3. 速度はそのままで、頭（_scrollTextBlock2）が X=0 に到達する位置 (-(measuredWidth + gap)) まで右から左へスクロール
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-(measuredWidth + gap), KeyTime.FromTimeSpan(t3)));

        // 4. 頭が初期表示 (X=0) に戻った位置で 1.2 秒間静止して次周回へループ
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(-(measuredWidth + gap), KeyTime.FromTimeSpan(t4)));

        _transform.BeginAnimation(TranslateTransform.XProperty, animation);
        IsScrolling = true;
    }

    private void StopMarquee()
    {
        _hoverTimer?.Stop();
        _hoverTimer = null;

        _transform.BeginAnimation(TranslateTransform.XProperty, null);
        IsScrolling = false;
    }

    private void ResetToStaticView()
    {
        _transform.BeginAnimation(TranslateTransform.XProperty, null);
        _transform.X = 0;
        _scrollCanvas.Visibility = Visibility.Collapsed;
        _staticTextBlock.Visibility = Visibility.Visible;
    }

    #endregion
}
