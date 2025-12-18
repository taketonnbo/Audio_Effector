using System.Collections.Generic;

namespace AudioEffector.Models
{
    /// <summary>
    /// ユーザー作成のプレイリストを表すクラス。
    /// </summary>
    public class UserPlaylist
    {
        /// <summary>
        /// プレイリスト名。
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// プレイリストに含まれるトラックのファイルパスリスト。
        /// </summary>
        public List<string> TrackPaths { get; set; } = new List<string>();

        /// <summary>
        /// サムネイル表示用のトラックパリスト（シリアライズ対象外）。
        /// UIバインディング用です。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public System.Collections.ObjectModel.ObservableCollection<string> ThumbnailTrackPaths { get; set; } = new System.Collections.ObjectModel.ObservableCollection<string>();
    }
}
