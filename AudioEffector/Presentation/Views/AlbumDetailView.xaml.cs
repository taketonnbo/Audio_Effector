using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// 選択したアルバムの詳細情報および全収録曲一覧を表示するビュー
/// </summary>
public partial class AlbumDetailView : UserControl
{
    private const double DefaultVerticalScrollBarWidth = 8.0;

    private ScrollViewer? _tracksScrollViewer;
    private ScrollBar? _verticalScrollBar;
    private Border? _pressedTrackRow;

    /// <summary>
    /// インスタンスを初期化します
    /// </summary>
    public AlbumDetailView()
    {
        InitializeComponent();
        Loaded += OnViewLoaded;
        Unloaded += OnViewUnloaded;
    }

    private void OnViewLoaded(object sender, RoutedEventArgs e)
    {
        AttachScrollViewer();
        UpdateColumnHeaderMargin();
    }

    private void OnViewUnloaded(object sender, RoutedEventArgs e)
    {
        DetachScrollViewer();
        _pressedTrackRow = null;
    }

    private void AttachScrollViewer()
    {
        if (_tracksScrollViewer != null)
        {
            return;
        }

        _tracksScrollViewer = FindVisualChild<ScrollViewer>(TracksListBox);
        if (_tracksScrollViewer != null)
        {
            _verticalScrollBar = _tracksScrollViewer.Template?.FindName("PART_VerticalScrollBar", _tracksScrollViewer) as ScrollBar;
            _tracksScrollViewer.ScrollChanged += OnTracksScrollChanged;
            _tracksScrollViewer.SizeChanged += OnTracksSizeChanged;
        }
    }

    private void DetachScrollViewer()
    {
        if (_tracksScrollViewer != null)
        {
            _tracksScrollViewer.ScrollChanged -= OnTracksScrollChanged;
            _tracksScrollViewer.SizeChanged -= OnTracksSizeChanged;
            _tracksScrollViewer = null;
        }

        _verticalScrollBar = null;
    }

    private void OnTracksScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportHeightChange == 0 && e.ExtentHeightChange == 0)
        {
            return;
        }

        UpdateColumnHeaderMargin();
    }

    private void OnTracksSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateColumnHeaderMargin();
    }

    private void UpdateColumnHeaderMargin()
    {
        if (_tracksScrollViewer == null)
        {
            return;
        }

        double rightOffset = 0.0;
        if (_tracksScrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible)
        {
            _verticalScrollBar ??= _tracksScrollViewer.Template?.FindName("PART_VerticalScrollBar", _tracksScrollViewer) as ScrollBar;
            rightOffset = _verticalScrollBar != null && _verticalScrollBar.ActualWidth > 0
                ? _verticalScrollBar.ActualWidth
                : DefaultVerticalScrollBarWidth;
        }

        if (ColumnHeaderGrid.Margin.Right != rightOffset)
        {
            ColumnHeaderGrid.Margin = new Thickness(0, 0, rightOffset, 0);
        }
    }

    /// <summary>
    /// 収録曲行で左ボタンが押下された際、子ボタン・チェックボックス以外の領域であれば押下行を記録します
    /// </summary>
    private void TrackRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1)
        {
            _pressedTrackRow = null;
            return;
        }

        _pressedTrackRow = ShouldTriggerRowPlay(e.OriginalSource as DependencyObject)
            ? sender as Border
            : null;
    }

    /// <summary>
    /// 押下中の収録曲行からカーソルが離れた場合、ドラッグキャンセル扱いとして押下記録をクリアします
    /// </summary>
    private void TrackRow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(_pressedTrackRow, sender))
        {
            _pressedTrackRow = null;
        }
    }

    /// <summary>
    /// 収録曲行クリック時、同一行上での押下・離上かつ子ボタン・チェックボックス以外の領域であれば楽曲を再生します
    /// </summary>
    private void TrackRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var pressedRow = _pressedTrackRow;
        _pressedTrackRow = null;

        if (!ReferenceEquals(pressedRow, sender) || !ShouldTriggerRowPlay(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: AudioEffector.Domain.Entities.Track track } &&
            DataContext is AlbumDetailViewModel viewModel &&
            viewModel.PlayTrackCommand.CanExecute(track))
        {
            viewModel.PlayTrackCommand.Execute(track);
            e.Handled = true;
        }
    }

    /// <summary>
    /// クリック元要素が楽曲行クリック再生を発火すべき対象かどうかを判定します
    /// </summary>
    /// <param name="originalSource">クリックされた元の要素</param>
    /// <returns>子ボタン・チェックボックス・スクロールバー以外の領域であれば true</returns>
    public static bool ShouldTriggerRowPlay(DependencyObject? originalSource)
    {
        if (originalSource == null)
        {
            return false;
        }

        var current = originalSource;
        while (current != null)
        {
            if (current is ButtonBase or ScrollBar)
            {
                return false;
            }

            current = GetParentObject(current);
        }

        return true;
    }

    private static DependencyObject? GetParentObject(DependencyObject child)
    {
        if (child is ContentElement contentElement)
        {
            return ContentOperations.GetParent(contentElement) ?? LogicalTreeHelper.GetParent(contentElement);
        }

        if (child is Visual || child is System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(child) ?? LogicalTreeHelper.GetParent(child);
        }

        return LogicalTreeHelper.GetParent(child);
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T target)
            {
                return target;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }
}
