using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// 全ての曲一覧を表示するビュー
/// </summary>
public partial class AllSongsView : UserControl
{
    private const double DefaultVerticalScrollBarWidth = 8.0;

    private ScrollViewer? _tracksScrollViewer;

    /// <summary>
    /// インスタンスを初期化します
    /// </summary>
    public AllSongsView()
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
    }

    private void OnTracksScrollChanged(object sender, ScrollChangedEventArgs e)
    {
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
            var verticalScrollBar = _tracksScrollViewer.Template?.FindName("PART_VerticalScrollBar", _tracksScrollViewer) as ScrollBar;
            rightOffset = verticalScrollBar != null && verticalScrollBar.ActualWidth > 0
                ? verticalScrollBar.ActualWidth
                : DefaultVerticalScrollBarWidth;
        }

        if (ColumnHeaderGrid.Margin.Right != rightOffset)
        {
            ColumnHeaderGrid.Margin = new Thickness(0, 0, rightOffset, 0);
        }
    }

    private void TracksListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not AllSongsViewModel viewModel)
        {
            return;
        }

        // お気に入りボタン・スクロールバー・空白領域のダブルクリックによる誤再生を防止
        var item = FindClickedTrackItem(e.OriginalSource as DependencyObject);
        if (item?.DataContext is AudioEffector.Domain.Entities.Track track &&
            viewModel.PlayTrackCommand.CanExecute(track))
        {
            viewModel.PlayTrackCommand.Execute(track);
            e.Handled = true;
        }
    }

    private static ListBoxItem? FindClickedTrackItem(DependencyObject? source)
    {
        var current = source;
        while (current != null)
        {
            if (current is ButtonBase or ScrollBar)
            {
                return null;
            }

            if (current is ListBoxItem listBoxItem)
            {
                return listBoxItem;
            }

            current = GetParentObject(current);
        }

        return null;
    }

    private static DependencyObject? GetParentObject(DependencyObject child)
    {
        if (child is ContentElement contentElement)
        {
            return ContentOperations.GetParent(contentElement) ?? LogicalTreeHelper.GetParent(contentElement);
        }

        return VisualTreeHelper.GetParent(child) ?? LogicalTreeHelper.GetParent(child);
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
