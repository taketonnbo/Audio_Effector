using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// 同期歌詞・非同期歌詞の表示、自動中央寄せスクロール、手動スクロール一時停止および行クリックシークを提供する再利用可能ビューです
/// </summary>
public partial class LyricsView : UserControl
{
    private const long PROGRAMMATIC_SCROLL_GUARD_MS = 180;

    /// <summary>
    /// ミニプレイヤー向けコンパクトモードで描画するかどうかを制御する依存関係プロパティ
    /// </summary>
    public static readonly DependencyProperty IsCompactModeProperty = DependencyProperty.Register(
        nameof(IsCompactMode),
        typeof(bool),
        typeof(LyricsView),
        new PropertyMetadata(false, OnIsCompactModeChanged));

    private LyricsViewModel? _subscribedViewModel;
    private ScrollViewer? _scrollViewer;
    private ItemsPresenter? _itemsPresenter;
    private LyricLineItemViewModel? _pressedLine;
    private bool _isProgrammaticScroll;
    private long _programmaticScrollGuardUntilTicks;
    private int _pendingScrollGeneration;

    /// <summary>
    /// ミニプレイヤー向けのコンパクトモード表示を有効にするかどうかを取得または設定します
    /// </summary>
    public bool IsCompactMode
    {
        get => (bool)GetValue(IsCompactModeProperty);
        set => SetValue(IsCompactModeProperty, value);
    }

    /// <summary>
    /// <see cref="LyricsView"/> の新しいインスタンスを初期化します
    /// </summary>
    public LyricsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// 指定されたクリック元要素が行クリックシークを発火してよい要素かどうかを判定します
    /// </summary>
    /// <param name="originalSource">クリック元のビジュアル要素</param>
    /// <returns>シーク操作を発火してよい場合は <see langword="true"/></returns>
    internal static bool ShouldTriggerLineSeek(DependencyObject? originalSource)
    {
        DependencyObject? current = originalSource;
        while (current != null)
        {
            if (current is ButtonBase or ScrollBar or Thumb)
            {
                return false;
            }

            current = GetParentObject(current);
        }

        return originalSource != null;
    }

    private static void OnIsCompactModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LyricsView view)
        {
            view.UpdateViewportCenterPadding();
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        if (IsLoaded)
        {
            AttachViewModel(e.NewValue as LyricsViewModel);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as LyricsViewModel);
        AttachScrollViewer();
        UpdateViewportCenterPadding();

        if (_subscribedViewModel is { HasLyrics: true, IsSynced: true, CurrentLineIndex: >= 0, IsAutoScrollPaused: false } vm)
        {
            ScrollToLineIndex(vm.CurrentLineIndex);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachViewModel();
        DetachScrollViewer();
        _pressedLine = null;
    }

    private void AttachViewModel(LyricsViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            return;
        }

        DetachViewModel();
        if (viewModel == null)
        {
            return;
        }

        _subscribedViewModel = viewModel;
        _subscribedViewModel.ScrollToLineRequested += OnScrollToLineRequested;
    }

    private void DetachViewModel()
    {
        if (_subscribedViewModel == null)
        {
            return;
        }

        _subscribedViewModel.ScrollToLineRequested -= OnScrollToLineRequested;
        _subscribedViewModel = null;
    }

    private void AttachScrollViewer()
    {
        if (_scrollViewer != null)
        {
            return;
        }

        LyricsListBox.ApplyTemplate();
        _scrollViewer = FindVisualChild<ScrollViewer>(LyricsListBox);
        _itemsPresenter = FindVisualChild<ItemsPresenter>(LyricsListBox);

        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged += OnScrollViewerScrollChanged;
            _scrollViewer.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnScrollBarUserInteraction), true);
            _scrollViewer.AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler(OnScrollBarScroll), true);
        }
    }

    private void DetachScrollViewer()
    {
        if (_scrollViewer == null)
        {
            return;
        }

        _scrollViewer.ScrollChanged -= OnScrollViewerScrollChanged;
        _scrollViewer.RemoveHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnScrollBarUserInteraction));
        _scrollViewer.RemoveHandler(ScrollBar.ScrollEvent, new ScrollEventHandler(OnScrollBarScroll));
        _scrollViewer = null;
        _itemsPresenter = null;
    }

    private void OnScrollToLineRequested(int lineIndex)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ScrollToLineIndex(lineIndex)));
            return;
        }

        ScrollToLineIndex(lineIndex);
    }

    private void ScrollToLineIndex(int lineIndex)
    {
        if (LyricsListBox.Visibility != Visibility.Visible ||
            lineIndex < 0 ||
            lineIndex >= LyricsListBox.Items.Count)
        {
            return;
        }

        AttachScrollViewer();
        int generation = ++_pendingScrollGeneration;

        BeginProgrammaticScrollGuard();
        try
        {
            object? item = LyricsListBox.Items[lineIndex];
            if (item == null)
            {
                return;
            }

            if (LyricsListBox.ItemContainerGenerator.ContainerFromIndex(lineIndex) is not FrameworkElement container)
            {
                LyricsListBox.ScrollIntoView(item);
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (generation != _pendingScrollGeneration ||
                        _subscribedViewModel is { IsAutoScrollPaused: true })
                    {
                        return;
                    }

                    CenterContainerInViewport(lineIndex);
                }));
                return;
            }

            CenterContainerInViewport(lineIndex);
        }
        finally
        {
            EndProgrammaticScrollGuard();
        }
    }

    private void CenterContainerInViewport(int lineIndex)
    {
        if (_scrollViewer == null ||
            lineIndex < 0 ||
            lineIndex >= LyricsListBox.Items.Count)
        {
            return;
        }

        if (LyricsListBox.ItemContainerGenerator.ContainerFromIndex(lineIndex) is not FrameworkElement container)
        {
            return;
        }

        double viewportHeight = _scrollViewer.ViewportHeight;
        if (viewportHeight <= 0)
        {
            return;
        }

        BeginProgrammaticScrollGuard();
        try
        {
            Point positionInViewport = container.TransformToAncestor(_scrollViewer).Transform(new Point(0, 0));
            double containerCenterY = positionInViewport.Y + (container.ActualHeight / 2.0);
            double desiredOffset = _scrollViewer.VerticalOffset + (containerCenterY - (viewportHeight / 2.0));
            double clampedOffset = Math.Clamp(desiredOffset, 0.0, _scrollViewer.ScrollableHeight);

            if (Math.Abs(clampedOffset - _scrollViewer.VerticalOffset) > 0.5)
            {
                _scrollViewer.ScrollToVerticalOffset(clampedOffset);
            }
        }
        catch (InvalidOperationException)
        {
            // ビジュアルツリー切替途中の座標変換失敗は安全に無視する
        }
        finally
        {
            EndProgrammaticScrollGuard();
        }
    }

    private void BeginProgrammaticScrollGuard()
    {
        _isProgrammaticScroll = true;
        _programmaticScrollGuardUntilTicks = Stopwatch.GetTimestamp()
            + (Stopwatch.Frequency * PROGRAMMATIC_SCROLL_GUARD_MS / 1000);
    }

    private void EndProgrammaticScrollGuard()
    {
        _isProgrammaticScroll = false;
        _programmaticScrollGuardUntilTicks = Stopwatch.GetTimestamp()
            + (Stopwatch.Frequency * PROGRAMMATIC_SCROLL_GUARD_MS / 1000);
    }

    private bool IsInProgrammaticScrollGuard()
    {
        return _isProgrammaticScroll || Stopwatch.GetTimestamp() <= _programmaticScrollGuardUntilTicks;
    }

    private void OnScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.VerticalChange) < 0.5)
        {
            return;
        }

        if (IsInProgrammaticScrollGuard())
        {
            return;
        }

        _subscribedViewModel?.NotifyManualScroll();
    }

    private void OnScrollBarUserInteraction(object sender, DragStartedEventArgs e)
    {
        _subscribedViewModel?.NotifyManualScroll();
    }

    private void OnScrollBarScroll(object sender, ScrollEventArgs e)
    {
        if (!IsInProgrammaticScrollGuard())
        {
            _subscribedViewModel?.NotifyManualScroll();
        }
    }

    private void LyricsListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _subscribedViewModel?.NotifyManualScroll();
    }

    private void LyricsListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
        {
            _subscribedViewModel?.NotifyManualScroll();
        }
    }

    private void LyricsListBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateViewportCenterPadding();
    }

    private void UpdateViewportCenterPadding()
    {
        AttachScrollViewer();
        if (_itemsPresenter == null)
        {
            return;
        }

        double height = LyricsListBox.ActualHeight;
        double verticalPadding = height > 0
            ? Math.Max(IsCompactMode ? 8.0 : 16.0, Math.Min((height / 2.0) - (IsCompactMode ? 16.0 : 24.0), 160.0))
            : (IsCompactMode ? 8.0 : 16.0);

        _itemsPresenter.Margin = new Thickness(0, verticalPadding, 0, verticalPadding);
    }

    private void LyricRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ShouldTriggerLineSeek(e.OriginalSource as DependencyObject))
        {
            _pressedLine = null;
            return;
        }

        _pressedLine = (sender as FrameworkElement)?.DataContext as LyricLineItemViewModel;
    }

    private void LyricRow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (_pressedLine == null || !ShouldTriggerLineSeek(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if ((sender as FrameworkElement)?.DataContext is not LyricLineItemViewModel releasedLine ||
                !ReferenceEquals(_pressedLine, releasedLine))
            {
                return;
            }

            if (DataContext is LyricsViewModel vm &&
                releasedLine.IsSeekable &&
                vm.SeekToLineCommand.CanExecute(releasedLine))
            {
                vm.SeekToLineCommand.Execute(releasedLine);
                e.Handled = true;
            }
        }
        finally
        {
            _pressedLine = null;
            // 行クリック後の残留選択ハイライトを防止するため SelectedItem を即時解除する
            LyricsListBox.SelectedItem = null;
        }
    }

    private void LyricRow_MouseLeave(object sender, MouseEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LyricLineItemViewModel leftLine &&
            ReferenceEquals(_pressedLine, leftLine))
        {
            _pressedLine = null;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T matched)
            {
                return matched;
            }

            T? descendant = FindVisualChild<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static DependencyObject? GetParentObject(DependencyObject child)
    {
        if (child is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(child);
        }

        return LogicalTreeHelper.GetParent(child);
    }
}
