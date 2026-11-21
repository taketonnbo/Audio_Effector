using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// アーティスト一覧を表示するビュー
/// </summary>
public partial class ArtistsView : UserControl
{
    /// <summary>
    /// インスタンスを初期化します
    /// </summary>
    public ArtistsView()
    {
        InitializeComponent();
    }

    private void ArtistsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || DataContext is not ArtistsViewModel viewModel)
        {
            return;
        }

        // スクロールバー・ボタン・余白のダブルクリックによる誤再生を防止
        var item = FindClickedListBoxItem(e.OriginalSource as DependencyObject);
        if (item?.DataContext is ArtistGroup artist &&
            viewModel.PlayArtistCommand.CanExecute(artist))
        {
            viewModel.PlayArtistCommand.Execute(artist);
            e.Handled = true;
        }
    }

    private void ArtistTracksListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || DataContext is not ArtistsViewModel viewModel)
        {
            return;
        }

        // お気に入りボタン・スクロールバー・余白のダブルクリックによる誤再生を防止
        var item = FindClickedListBoxItem(e.OriginalSource as DependencyObject);
        if (item?.DataContext is AudioEffector.Domain.Entities.Track track &&
            viewModel.PlayTrackCommand.CanExecute(track))
        {
            viewModel.PlayTrackCommand.Execute(track);
            e.Handled = true;
        }
    }

    private static ListBoxItem? FindClickedListBoxItem(DependencyObject? source)
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
}
