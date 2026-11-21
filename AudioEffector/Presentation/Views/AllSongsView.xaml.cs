using System.Windows.Controls;
using System.Windows.Input;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// 全ての曲一覧を表示するビュー
/// </summary>
public partial class AllSongsView : UserControl
{
    /// <summary>
    /// インスタンスを初期化します
    /// </summary>
    public AllSongsView()
    {
        InitializeComponent();
    }

    private void TracksListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is AllSongsViewModel viewModel &&
            TracksListBox.SelectedItem is Track track &&
            viewModel.PlayTrackCommand.CanExecute(track))
        {
            viewModel.PlayTrackCommand.Execute(track);
        }
    }
}
