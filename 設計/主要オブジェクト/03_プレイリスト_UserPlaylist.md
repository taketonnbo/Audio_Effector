# オブジェクト抽出テーブル: プレイリスト (UserPlaylist)

## 1. 基本情報
- **オブジェクト名**: プレイリスト
- **英語名**: UserPlaylist
- **分類**: プライマリオブジェクト / コンテナオブジェクト
- **責務**: ユーザーが定義した独自の楽曲コレクションを管理し、任意の再生順序および2×2サムネイル情報を保持する。

## 2. コレクション / シングル構成

> [!IMPORTANT]
> **コレクション / シングル構成**:
> - **コレクション表示**: `PlaylistSelectorView.xaml`
>   - 登録済みプレイリストをカード型（`WrapPanel`）で一覧表示。各カードには最大4枚の2×2分割サムネイル画像（ディレクトリ単位で重複排除）、タイトル、削除ボタンが表示される。
> - **シングル表示**: `PlaylistTracksView.xaml`
>   - プレイリストカードをクリックすることで画面遷移（`ViewType.PlaylistTracks`）して表示される。
>   - 上部に背景ぼかしアート、ヘッダー（2×2分割サムネイル、タイトル、閉じるボタン `X`）、下部に収録曲リストが配置される。なお、「お気に入り」画面（`ViewType.Favorites`）も同じ `PlaylistTracksView.xaml` を共用して表示する。

## 3. 属性（プロパティ）定義

| プロパティ名 | 型 | 説明 | コレクション表示 | シングル表示 | 実装プロパティ ([UserPlaylist.cs](../../AudioEffector/Domain/Entities/UserPlaylist.cs)) |
| :--- | :--- | :--- | :---: | :---: | :--- |
| **Id** | `PlaylistId` | プレイリストの一意識別子 | - | - | `UserPlaylist.Id` |
| **Name** | `string` | プレイリストのタイトル | ○ | ○ | `UserPlaylist.Name` |
| **TrackIds** | `IReadOnlyList<TrackId>` | 登録楽曲のTrackId一覧 | - | - | `UserPlaylist.TrackIds` |
| **TrackPaths** | `List<string>` | 登録楽曲のファイルパス一覧（`playlists.json` 永続化対象） | - | ○ (全曲リスト解決用) | `UserPlaylist.TrackPaths` |
| **TrackCount** | `int` | 登録曲数 | - | - | `UserPlaylist.TrackCount` |
| **ThumbnailTrackPaths** | `ObservableCollection<string>` | 2×2分割サムネイル用楽曲パス（最大4件） | ○ | ○ | `UserPlaylist.ThumbnailTrackPaths` |
| **Thumbnail1〜4** | `string?` | 固定インデクサ回避用の安全なサムネイルパス参照プロパティ | ○ | ○ | `UserPlaylist.Thumbnail1`〜`Thumbnail4` |
| **CreatedAt / UpdatedAt** | `DateTime` | 作成日時・更新日時 | - | - | `UserPlaylist.CreatedAt`, `UserPlaylist.UpdatedAt` |
| **TotalDuration / IsSmartPlaylist / FilterRule** | 各種 | 合計再生時間・スマートプレイリスト抽出条件 | - | - | 拡張構想 (#233) |

## 4. アクション（操作）定義

| アクション名 | 動詞 | トリガー / 操作 | 影響・結果 | 状態 | 関連コマンド / 実装 |
| :--- | :--- | :--- | :--- | :---: | :--- |
| **新規作成** | Create | 上部「`+ Create New Playlist`」ボタン | `InputBox` で名称を入力し新規プレイリストを作成・永続化 | 実装済み | `CreatePlaylistCommand` ([PlaylistViewModel.cs](../../AudioEffector/Presentation/ViewModels/PlaylistViewModel.cs)) |
| **シングル詳細表示** | Open | プレイリストカードの左クリック、または右クリック「開く」 | `PlaylistTracksView` へ遷移し収録曲を非同期ロード（世代番号で競合防止） | 実装済み | `SelectPlaylistCommand` |
| **一括再生する** | Play | カード右クリック「再生」 | プレイリスト全曲をキューに読み込み先頭から再生開始（空リスト時は無動作） | 実装済み | `PlayPlaylistCommand` |
| **シャッフル再生** | Shuffle Play | カード右クリック「シャッフル再生」 | シャッフルを有効化してプレイリスト内のランダム曲から再生開始 | 実装済み | `ShufflePlayPlaylistCommand` |
| **名前を変更する** | Rename | カード右クリック「名前の変更」 | `InputBox` ダイアログ（現在名プリセット）で名称を更新・保存 | 実装済み | `RenamePlaylistCommand` |
| **削除する** | Delete | カード右上の「`-`」ボタン、または右クリック「削除」 | 確認 `MessageBox` のうえプレイリストを削除・保存 | 実装済み | `DeletePlaylistCommand` |
| **楽曲を追加する** | Add Track | 楽曲/アルバムの `+` ボタン、または右クリック「プレイリストに追加...」 | `PlaylistSelectionDialog` で選択したプレイリストへ重複を除いて楽曲を追加 | 実装済み | `ShowAddToPlaylistDialogCommand` |
| **楽曲を除外する** | Remove Track | シングルビュー内の楽曲右端「`-`」ボタン | 確認ダイアログのうえプレイリストから該当曲を除外（お気に入り表示時は確認なしで即解除） | 実装済み | `RemoveFromPlaylistCommand` |
| **一覧へ戻る** | Close | シングルビュー右上の閉じるボタン「`X`」 | `PlaylistSelectorView`（プレイリスト一覧）へ戻る | 実装済み | `ShowPlaylistSelectorCommand` |
| **曲順並び替え / M3U入出力 / スマートプレイリスト** | Reorder / M3U / Smart | D&D曲順変更（Domain/App層 `ReorderTrackAsync` は実装済み、UI未接続）、M3Uエクスポート/インポート | プレイリストの高度な編集・外部連携 | 拡張構想 (#233) | [PlaylistApplicationService.cs](../../AudioEffector/Application/ApplicationServices/PlaylistApplicationService.cs) |

## 5. 拡張設計指針
- **UI上でのD&D曲順並び替え接続**: Domain層（`UserPlaylist.Reorder`）およびApplication層（`PlaylistApplicationService.ReorderTrackAsync`）に実装済みの順序変更ロジックを `PlaylistTracksView` のドラッグ＆ドロップ操作へ接続する。
- **スマートプレイリスト・M3U連携**: 条件抽出による動的プレイリストおよび標準 `.m3u` / `.m3u8` ファイルのインポート・エクスポートへの拡張。
