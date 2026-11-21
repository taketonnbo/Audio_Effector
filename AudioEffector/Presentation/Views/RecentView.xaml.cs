using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// 最近再生した曲一覧を表示するビュー
/// </summary>
public partial class RecentView : UserControl
{
    private const double DefaultVerticalScrollBarWidth = 8.0;

    private ScrollViewer? _recentScrollViewer;

    /// <summary>
    /// インスタンスを初期化します
    /// </summary>
    public RecentView()
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
        if (_recentScrollViewer != null)
        {
            return;
        }

        _recentScrollViewer = FindVisualChild<ScrollViewer>(RecentListBox);
        if (_recentScrollViewer != null)
        {
            _recentScrollViewer.ScrollChanged += OnRecentScrollChanged;
            _recentScrollViewer.SizeChanged += OnRecentSizeChanged;
        }
    }

    private void DetachScrollViewer()
    {
        if (_recentScrollViewer != null)
        {
            _recentScrollViewer.ScrollChanged -= OnRecentScrollChanged;
            _recentScrollViewer.SizeChanged -= OnRecentSizeChanged;
            _recentScrollViewer = null;
        }
    }

    private void OnRecentScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateColumnHeaderMargin();
    }

    private void OnRecentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateColumnHeaderMargin();
    }

    private void UpdateColumnHeaderMargin()
    {
        if (_recentScrollViewer == null)
        {
            return;
        }

        double rightOffset = 0.0;
        if (_recentScrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible)
        {
            var verticalScrollBar = _recentScrollViewer.Template?.FindName("PART_VerticalScrollBar", _recentScrollViewer) as ScrollBar;
            rightOffset = verticalScrollBar != null && verticalScrollBar.ActualWidth > 0
                ? verticalScrollBar.ActualWidth
                : DefaultVerticalScrollBarWidth;
        }

        if (ColumnHeaderGrid.Margin.Right != rightOffset)
        {
            ColumnHeaderGrid.Margin = new Thickness(0, 0, rightOffset, 0);
        }
    }

    private void RecentListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not RecentViewModel viewModel)
        {
            return;
        }

        // お気に入りボタン・スクロールバー・空白領域のダブルクリックによる誤再生を防止
        var item = FindClickedHistoryItem(e.OriginalSource as DependencyObject);
        if (item?.DataContext is RecentTrackEntry entry &&
            viewModel.PlayEntryCommand.CanExecute(entry))
        {
            viewModel.PlayEntryCommand.Execute(entry);
            e.Handled = true;
        }
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RecentViewModel viewModel || !viewModel.HasHistory)
        {
            return;
        }

        var result = MessageBox.Show(
            "最近再生した曲の履歴をすべて削除しますか？",
            "履歴のクリア",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            viewModel.ClearHistory();
        }
    }

    private static ListBoxItem? FindClickedHistoryItem(DependencyObject? source)
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
