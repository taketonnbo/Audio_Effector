using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// メインウィンドウの右端からスライド表示される再生キューパネル
/// </summary>
public partial class PlayQueueSidePanel : UserControl
{
    private const double PanelWidth = 380.0;
    private const double AutoScrollEdgeMargin = 32.0;
    private const double AutoScrollStep = 14.0;

    private readonly DoubleAnimation _slideInAnimation;
    private readonly DoubleAnimation _slideOutAnimation;
    private MainViewModel? _subscribedViewModel;

    // ドラッグ＆ドロップ状態管理
    private Point _dragStartPoint;
    private QueueSection _dragSourceSection;
    private int _dragSourceIndex = -1;
    private Track? _dragSourceTrack;
    private ItemsControl? _dragSourceControl;
    private bool _isDragging;
    private QueueInsertionAdorner? _insertionAdorner;
    private UIElement? _adornedElement;

    /// <summary>
    /// インスタンスを初期化します
    /// </summary>
    public PlayQueueSidePanel()
    {
        InitializeComponent();

        // 起動時の初期状態: 完全に非表示（Collapsed）としてビジュアルツリー・レイアウト・アイテム生成を抑止
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        PanelTransform.X = PanelWidth;

        _slideInAnimation = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(350),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        _slideOutAnimation = new DoubleAnimation
        {
            To = PanelWidth,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        _slideOutAnimation.Completed += OnSlideOutCompleted;

        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SubscribeViewModel(DataContext as MainViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        HideEmptyUserQueueDropZone();
        RemoveInsertionAdorner();
        ResetDragState();
        UnsubscribeViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SubscribeViewModel(e.NewValue as MainViewModel);
    }

    private void SubscribeViewModel(MainViewModel? vm)
    {
        if (_subscribedViewModel == vm)
        {
            return;
        }

        UnsubscribeViewModel();

        if (vm != null)
        {
            _subscribedViewModel = vm;
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            // 初期状態の反映（アニメーションなし）
            UpdatePanelState(vm.IsPlayQueuePanelOpen, animate: false);
        }
    }

    private void UnsubscribeViewModel()
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPlayQueuePanelOpen) && sender is MainViewModel vm)
        {
            UpdatePanelState(vm.IsPlayQueuePanelOpen, animate: true);
        }
    }

    private void UpdatePanelState(bool isOpen, bool animate)
    {
        if (isOpen)
        {
            // 開く場合: 即座に Visible 化してスライドインアニメーションを実行
            Visibility = Visibility.Visible;
            IsHitTestVisible = true;

            if (animate)
            {
                PanelTransform.BeginAnimation(TranslateTransform.XProperty, _slideInAnimation);
            }
            else
            {
                PanelTransform.BeginAnimation(TranslateTransform.XProperty, null);
                PanelTransform.X = 0;
            }
        }
        else
        {
            // 閉じる場合
            if (animate && Visibility == Visibility.Visible)
            {
                PanelTransform.BeginAnimation(TranslateTransform.XProperty, _slideOutAnimation);
            }
            else
            {
                PanelTransform.BeginAnimation(TranslateTransform.XProperty, null);
                PanelTransform.X = PanelWidth;
                Visibility = Visibility.Collapsed;
                IsHitTestVisible = false;
            }
        }
    }

    private void OnSlideOutCompleted(object? sender, EventArgs e)
    {
        // アニメーション完了時に閉じている状態であれば Collapsed にして描画とヒットテストを完全停止
        if (_subscribedViewModel != null && !_subscribedViewModel.IsPlayQueuePanelOpen)
        {
            PanelTransform.BeginAnimation(TranslateTransform.XProperty, null);
            PanelTransform.X = PanelWidth;
            Visibility = Visibility.Collapsed;
            IsHitTestVisible = false;
        }
    }

    #region ドラッグ＆ドロップ並び替え処理

    private void OnUserQueuePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginDragCandidate(UserQueueItemsControl, QueueSection.UserQueue, e);
    }

    private void OnAlbumQueuePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginDragCandidate(AlbumQueueItemsControl, QueueSection.AlbumQueue, e);
    }

    private void BeginDragCandidate(ItemsControl itemsControl, QueueSection section, MouseButtonEventArgs e)
    {
        ResetDragState();

        if (e.OriginalSource is not DependencyObject source || IsNoDragOrigin(source))
        {
            return;
        }

        // 同一 Track の重複行でもクリック元のコンテナから正確な行インデックスを特定する
        var container = ItemsControl.ContainerFromElement(itemsControl, source);
        if (container == null)
        {
            return;
        }

        int index = itemsControl.ItemContainerGenerator.IndexFromContainer(container);
        if (index < 0 || index >= itemsControl.Items.Count || itemsControl.Items[index] is not Track track)
        {
            return;
        }

        _dragStartPoint = e.GetPosition(this);
        _dragSourceSection = section;
        _dragSourceIndex = index;
        _dragSourceTrack = track;
        _dragSourceControl = itemsControl;
    }

    private void OnQueuePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging || _dragSourceTrack == null || _dragSourceControl == null || _dragSourceIndex < 0)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetDragState();
            return;
        }

        Point currentPoint = e.GetPosition(this);
        if (!PlayQueueDragDropHelper.HasExceededDragThreshold(
                _dragStartPoint,
                currentPoint,
                SystemParameters.MinimumHorizontalDragDistance,
                SystemParameters.MinimumVerticalDragDistance))
        {
            return;
        }

        var payload = new QueueDragPayload(_dragSourceSection, _dragSourceIndex, _dragSourceTrack);
        var dataObject = new DataObject(PlayQueueDragDropHelper.DragDataFormat, payload);

        _isDragging = true;
        if (_dragSourceSection == QueueSection.AlbumQueue && DataContext is MainViewModel vm && !vm.HasUserQueue)
        {
            ShowEmptyUserQueueDropZone();
        }

        try
        {
            DragDrop.DoDragDrop(_dragSourceControl, dataObject, DragDropEffects.Move);
        }
        finally
        {
            HideEmptyUserQueueDropZone();
            RemoveInsertionAdorner();
            ResetDragState();
        }
    }

    private void OnQueuePreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            ResetDragState();
        }
    }

    private void OnEmptyUserQueueDragOver(object sender, DragEventArgs e)
    {
        if (!TryGetDragPayload(e, out var payload) ||
            !PlayQueueDragDropHelper.CanDrop(payload.SourceSection, QueueSection.UserQueue))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        RemoveInsertionAdorner();
        SetEmptyDropZoneHighlight(isHighlighted: true);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnEmptyUserQueueDragLeave(object sender, DragEventArgs e)
    {
        SetEmptyDropZoneHighlight(isHighlighted: false);
    }

    private void OnEmptyUserQueueDrop(object sender, DragEventArgs e)
    {
        SetEmptyDropZoneHighlight(isHighlighted: false);

        if (!TryGetDragPayload(e, out var payload) ||
            !PlayQueueDragDropHelper.CanDrop(payload.SourceSection, QueueSection.UserQueue))
        {
            e.Handled = true;
            return;
        }

        var sourceControl = GetItemsControlForSection(payload.SourceSection);
        int resolvedSourceIndex = PlayQueueDragDropHelper.ResolveCurrentSourceIndex(
            sourceControl.Items,
            payload.SourceIndex,
            payload.Track);

        if (resolvedSourceIndex >= 0 &&
            payload.SourceSection == QueueSection.AlbumQueue &&
            DataContext is MainViewModel vm)
        {
            vm.MoveAlbumQueueItemToUserQueue(resolvedSourceIndex, 0);
        }

        e.Handled = true;
    }

    private void OnUserQueueDragOver(object sender, DragEventArgs e)
    {
        HandleDragOver(UserQueueItemsControl, QueueSection.UserQueue, e);
    }

    private void OnAlbumQueueDragOver(object sender, DragEventArgs e)
    {
        HandleDragOver(AlbumQueueItemsControl, QueueSection.AlbumQueue, e);
    }

    private void HandleDragOver(ItemsControl itemsControl, QueueSection targetSection, DragEventArgs e)
    {
        if (!TryGetDragPayload(e, out var payload) ||
            !PlayQueueDragDropHelper.CanDrop(payload.SourceSection, targetSection))
        {
            e.Effects = DragDropEffects.None;
            RemoveInsertionAdorner();
            e.Handled = true;
            return;
        }

        // 自動スクロールを先に実行してから挿入ライン位置を算出する
        HandleAutoScroll(e.GetPosition(QueueScrollViewer));
        var (_, indicatorY) = ResolveDropPosition(itemsControl, e.GetPosition(itemsControl));
        ShowOrUpdateInsertionAdorner(itemsControl, indicatorY);

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnQueueDragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            Point pos = e.GetPosition(element);
            if (pos.X < 0 || pos.Y < 0 || pos.X > element.ActualWidth || pos.Y > element.ActualHeight)
            {
                RemoveInsertionAdorner();
            }
        }
        else
        {
            RemoveInsertionAdorner();
        }
    }

    private void OnUserQueueDrop(object sender, DragEventArgs e)
    {
        HandleDrop(UserQueueItemsControl, QueueSection.UserQueue, e);
    }

    private void OnAlbumQueueDrop(object sender, DragEventArgs e)
    {
        HandleDrop(AlbumQueueItemsControl, QueueSection.AlbumQueue, e);
    }

    private void HandleDrop(ItemsControl itemsControl, QueueSection targetSection, DragEventArgs e)
    {
        RemoveInsertionAdorner();

        if (!TryGetDragPayload(e, out var payload))
        {
            e.Handled = true;
            return;
        }

        // ドラッグ中の非同期キュー変動（次曲自動遷移等）によるインデックス陳腐化を検証・再解決する
        var sourceControl = GetItemsControlForSection(payload.SourceSection);
        int resolvedSourceIndex = PlayQueueDragDropHelper.ResolveCurrentSourceIndex(
            sourceControl.Items,
            payload.SourceIndex,
            payload.Track);

        if (resolvedSourceIndex < 0)
        {
            e.Handled = true;
            return;
        }

        var (rawInsertIndex, _) = ResolveDropPosition(itemsControl, e.GetPosition(itemsControl));
        int targetIndex = PlayQueueDragDropHelper.CalculateTargetIndex(
            payload.SourceSection,
            resolvedSourceIndex,
            targetSection,
            rawInsertIndex,
            itemsControl.Items.Count);

        if (targetIndex >= 0 && DataContext is MainViewModel vm)
        {
            if (payload.SourceSection == QueueSection.UserQueue && targetSection == QueueSection.UserQueue)
            {
                vm.MoveUserQueueItem(resolvedSourceIndex, targetIndex);
            }
            else if (payload.SourceSection == QueueSection.AlbumQueue && targetSection == QueueSection.AlbumQueue)
            {
                vm.MoveAlbumQueueItem(resolvedSourceIndex, targetIndex);
            }
            else if (payload.SourceSection == QueueSection.AlbumQueue && targetSection == QueueSection.UserQueue)
            {
                vm.MoveAlbumQueueItemToUserQueue(resolvedSourceIndex, targetIndex);
            }
        }

        e.Handled = true;
    }

    private ItemsControl GetItemsControlForSection(QueueSection section)
    {
        return section == QueueSection.UserQueue ? UserQueueItemsControl : AlbumQueueItemsControl;
    }

    private void ShowEmptyUserQueueDropZone()
    {
        if (EmptyUserQueueDropZone != null)
        {
            SetEmptyDropZoneHighlight(isHighlighted: false);
            EmptyUserQueueDropZone.Visibility = Visibility.Visible;
        }
    }

    private void HideEmptyUserQueueDropZone()
    {
        if (EmptyUserQueueDropZone != null)
        {
            SetEmptyDropZoneHighlight(isHighlighted: false);
            EmptyUserQueueDropZone.Visibility = Visibility.Collapsed;
        }
    }

    private void SetEmptyDropZoneHighlight(bool isHighlighted)
    {
        if (EmptyUserQueueDropZone == null)
        {
            return;
        }

        var brushKey = isHighlighted ? "ControlBackgroundHighlightBrush" : "ControlBackgroundBrush";
        if (TryFindResource(brushKey) is Brush brush)
        {
            EmptyUserQueueDropZone.Background = brush;
        }
    }

    private static bool TryGetDragPayload(DragEventArgs e, out QueueDragPayload payload)
    {
        if (e.Data.GetDataPresent(PlayQueueDragDropHelper.DragDataFormat) &&
            e.Data.GetData(PlayQueueDragDropHelper.DragDataFormat) is QueueDragPayload extracted)
        {
            payload = extracted;
            return true;
        }

        payload = null!;
        return false;
    }

    private static (int InsertIndex, double IndicatorY) ResolveDropPosition(ItemsControl itemsControl, Point pointerPosition)
    {
        int count = itemsControl.Items.Count;
        if (count == 0)
        {
            return (0, 0.0);
        }

        double lastBottomY = 0.0;
        for (int i = 0; i < count; i++)
        {
            if (itemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
            {
                continue;
            }

            Point topLeft = container.TranslatePoint(new Point(0, 0), itemsControl);
            double height = container.ActualHeight;
            double bottomY = topLeft.Y + height;
            lastBottomY = bottomY;

            if (pointerPosition.Y <= bottomY)
            {
                double relativeY = pointerPosition.Y - topLeft.Y;
                int insertIndex = PlayQueueDragDropHelper.ResolveInsertIndex(i, relativeY, height, count);
                double indicatorY = insertIndex == i ? topLeft.Y : bottomY;
                return (insertIndex, indicatorY);
            }
        }

        return (count, lastBottomY);
    }

    private void ShowOrUpdateInsertionAdorner(UIElement targetElement, double indicatorY)
    {
        if (_insertionAdorner != null && _adornedElement == targetElement)
        {
            _insertionAdorner.UpdateY(indicatorY);
            return;
        }

        RemoveInsertionAdorner();

        var layer = AdornerLayer.GetAdornerLayer(targetElement);
        if (layer == null)
        {
            return;
        }

        _insertionAdorner = new QueueInsertionAdorner(targetElement, indicatorY);
        _adornedElement = targetElement;
        layer.Add(_insertionAdorner);
    }

    private void RemoveInsertionAdorner()
    {
        if (_insertionAdorner != null && _adornedElement != null)
        {
            var layer = AdornerLayer.GetAdornerLayer(_adornedElement);
            layer?.Remove(_insertionAdorner);
        }

        _insertionAdorner = null;
        _adornedElement = null;
    }

    private void HandleAutoScroll(Point positionInScrollViewer)
    {
        if (QueueScrollViewer == null || QueueScrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        if (positionInScrollViewer.Y < AutoScrollEdgeMargin)
        {
            QueueScrollViewer.ScrollToVerticalOffset(Math.Max(0, QueueScrollViewer.VerticalOffset - AutoScrollStep));
        }
        else if (positionInScrollViewer.Y > QueueScrollViewer.ViewportHeight - AutoScrollEdgeMargin)
        {
            QueueScrollViewer.ScrollToVerticalOffset(Math.Min(QueueScrollViewer.ScrollableHeight, QueueScrollViewer.VerticalOffset + AutoScrollStep));
        }
    }

    private void ResetDragState()
    {
        _isDragging = false;
        _dragSourceIndex = -1;
        _dragSourceTrack = null;
        _dragSourceControl = null;
    }

    private static bool IsNoDragOrigin(DependencyObject current)
    {
        DependencyObject? node = current;
        while (node != null)
        {
            if (node is FrameworkElement fe && Equals(fe.Tag, "NoDrag"))
            {
                return true;
            }
            node = GetParentObject(node);
        }

        return false;
    }

    private static DependencyObject? GetParentObject(DependencyObject child)
    {
        if (child is Visual || child is System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(child);
        }

        if (child is FrameworkContentElement fce)
        {
            return fce.Parent;
        }

        return LogicalTreeHelper.GetParent(child);
    }

    #endregion
}
