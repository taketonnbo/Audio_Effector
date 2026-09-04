namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// メインコンテンツ領域に表示する画面ビューの種類を表す列挙体
/// </summary>
public enum ViewType
{
    /// <summary>すべての曲一覧画面</summary>
    AllSongs,

    /// <summary>アルバム一覧画面</summary>
    Albums,

    /// <summary>アーティスト一覧画面</summary>
    Artists,

    /// <summary>フォルダー構造画面</summary>
    Folders,

    /// <summary>お気に入り一覧画面</summary>
    Favorites,

    /// <summary>プレイリスト選択画面</summary>
    Playlists,

    /// <summary>プレイリスト内楽曲一覧画面</summary>
    PlaylistTracks,

    /// <summary>最近再生した曲画面</summary>
    Recent,

    /// <summary>イコライザー設定画面</summary>
    Equalizer,

    /// <summary>デバイス同期管理画面</summary>
    DeviceSync
}
