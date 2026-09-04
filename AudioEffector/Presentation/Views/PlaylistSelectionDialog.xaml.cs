using System.Collections.ObjectModel;
using System.Windows;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// トラックをプレイリストに追加する際に使用する選択ダイアログ
/// </summary>
public partial class PlaylistSelectionDialog : Window
{
    /// <summary>
    /// 選択可能なプレイリスト一覧
    /// </summary>
    public ObservableCollection<UserPlaylist> Playlists { get; set; }

    /// <summary>
    /// ユーザーによって選択されたプレイリスト
    /// </summary>
    public UserPlaylist? SelectedPlaylist { get; set; }

    /// <summary>
    /// 追加対象のトラック
    /// </summary>
    public Track Track { get; set; }

    /// <summary>
    /// プレイリスト一覧と追加対象トラックを指定してインスタンスを初期化します
    /// </summary>
    /// <param name="playlists">選択可能なプレイリスト一覧</param>
    /// <param name="track">追加対象のトラック</param>
    public PlaylistSelectionDialog(ObservableCollection<UserPlaylist> playlists, Track track)
    {
        InitializeComponent();
        Playlists = playlists;
        Track = track;
        DataContext = this;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPlaylist != null)
        {
            DialogResult = true;
        }
        else
        {
            MessageBox.Show("プレイリストを選択してください", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
