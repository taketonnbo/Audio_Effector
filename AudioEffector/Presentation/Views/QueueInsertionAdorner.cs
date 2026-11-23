using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// 再生キューのドラッグ＆ドロップ時に挿入先位置をネオンシアン色の水平ラインで表示する軽量アドーナ
/// </summary>
public sealed class QueueInsertionAdorner : Adorner
{
    private static readonly Brush DefaultCyanBrush = CreateFrozenBrush(Color.FromRgb(0x00, 0xFF, 0xFF));
    private const double LineThickness = 2.0;
    private const double EndpointRadius = 3.0;
    private const double UpdateEpsilon = 0.5;

    private readonly Brush _indicatorBrush;
    private readonly Pen _linePen;
    private double _lineY;

    /// <summary>
    /// インスタンスを初期化します
    /// </summary>
    /// <param name="adornedElement">装飾対象のUI要素</param>
    /// <param name="initialY">初期描画Y座標</param>
    public QueueInsertionAdorner(UIElement adornedElement, double initialY)
        : base(adornedElement)
    {
        IsHitTestVisible = false;
        _lineY = initialY;
        _indicatorBrush = ResolveIndicatorBrush(adornedElement);
        _linePen = new Pen(_indicatorBrush, LineThickness);
        if (_linePen.CanFreeze)
        {
            _linePen.Freeze();
        }
    }

    /// <summary>
    /// 挿入ラインのY座標を更新します（微小変化時は再描画をスキップ）
    /// </summary>
    /// <param name="newY">新しいY座標</param>
    public void UpdateY(double newY)
    {
        if (Math.Abs(_lineY - newY) < UpdateEpsilon)
        {
            return;
        }

        _lineY = newY;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double width = AdornedElement.RenderSize.Width;
        if (width <= 0)
        {
            return;
        }

        double y = Math.Clamp(_lineY, EndpointRadius, Math.Max(EndpointRadius, AdornedElement.RenderSize.Height));
        double startX = EndpointRadius;
        double endX = Math.Max(startX, width - EndpointRadius);

        drawingContext.DrawLine(_linePen, new Point(startX, y), new Point(endX, y));
        drawingContext.DrawEllipse(_indicatorBrush, null, new Point(startX, y), EndpointRadius, EndpointRadius);
        drawingContext.DrawEllipse(_indicatorBrush, null, new Point(endX, y), EndpointRadius, EndpointRadius);
    }

    private static Brush ResolveIndicatorBrush(UIElement element)
    {
        if (element is FrameworkElement fe)
        {
            // ダーク（#00FFFF）・ライト（#0078D7）両テーマで視認性を確保するため NeonCyanBrush を優先参照
            if (fe.TryFindResource("NeonCyanBrush") is Brush themeAccentBrush)
            {
                return themeAccentBrush;
            }

            if (fe.TryFindResource("AlwaysNeonCyanBrush") is Brush fallbackAccentBrush)
            {
                return fallbackAccentBrush;
            }
        }

        return DefaultCyanBrush;
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
