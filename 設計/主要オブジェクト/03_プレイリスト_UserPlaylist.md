# オブジェクト抽出テーブル: プレイリスト (UserPlaylist)

## 1. 基本情報
- **オブジェクト名**: プレイリスト
- **英語名**: UserPlaylist
- **分類**: プライマリオブジェクト / コンテナオブジェクト
- **責務**: ユーザーが定義した独自の楽曲コレクションを管理し、任意の再生順序およびメタデータを保持する。

## 2. コレクション / シングル構成

> [!IMPORTANT]
> **実装仕様 (As-Is)**:
> - **コレクション表示**: `PlaylistSelectorView.xaml`
>   - 登録済みプレイリストをカード型（WrapPanel）で一覧表示。各カードには4分割サムネイル画像、タイトル、曲数が表示される。
> - **シングル表示**: `PlaylistTracksView.xaml`
>   - プレイリストカードをクリックすることで画面遷移（`ViewType.PlaylistTracks`）して表示される。
>   - 上部に背景ぼかしアート、ヘッダー（4分割サムネイル、タイトル、曲数、閉じるボタン）、下部に収録曲リストが配置された完全なシングルビューとして実装されています。

## 3. 属性（プロパティ）定義

| プロパティ名 | 型 | 説明 | コレクション表示 | シングル表示 | 実装プロパティ ([UserPlaylist.cs](file:///c:/Users/tnish/vs_code_git/Audio_Effector/AudioEffector/Domain/Entities/UserPlaylist.cs)) |
| :--- | :--- | :--- | :---: | :---: | :--- |
| **Id** | `PlaylistId` | プレイリストの一意識別子 | - | - | `UserPlaylist.Id` |
| **Name** | `string` | プレイリストのタイトル | ○ | ○ | `UserPlaylist.Name` |
| **TrackPaths** | `List<string>` | 登録楽曲のファイルパス一覧 | - | ○ (全曲リスト) | `UserPlaylist.TrackPaths` |
| **TrackCount** | `int` | 登録曲数 | ○ | ○ | `UserPlaylist.TrackCount` |
| **TotalDuration** | `TimeSpan` | 登録楽曲の合計再生時間 | - | ○ | `UserPlaylist.TotalDuration` |
| **ThumbnailTrackPaths** | `ObservableCollection<string>` | 4分割サムネイル用楽曲パス | ○ (4分割アート) | ○ (ヘッダーアート) | `UserPlaylist.ThumbnailTrackPaths` |

## 4. アクション（操作）定義

| アクション名 | 動詞 | トリガー / 操作 | 影響・結果 | 関連コマンド / 実装 |
| :--- | :--- | :--- | :--- | :--- |
| **新規作成** | Create | 上部「+ NEW PLAYLIST」ボタン、または右クリック「新規プレイリスト作成」 | 空の新規プレイリストを作成 | `CreatePlaylistCommand` ([PlaylistViewModel.cs](file:///c:/Users/tnish/vs_code_git/Audio_Effector/AudioEffector/Presentation/ViewModels/PlaylistViewModel.cs)) |
| **シングル詳細表示** | Open | プレイリストカードのクリック | `PlaylistTracksView` へ画面遷移 | `ShowPlaylistCommand` |
| **一括再生する** | Play | カード右クリック「再生」 | プレイリスト全曲をキューに読み込み再生開始 | `PlayPlaylistCommand` |
| **シャッフル再生** | Shuffle Play | カード右クリック「シャッフル再生」 | キューをシャッフルして再生開始 | `ShufflePlayPlaylistCommand` |
| **名前を変更する** | Rename | カード右クリック「名前の変更」 | ダイアログ (`InputBox`) で名称入力・更新 | `RenamePlaylistCommand` |
| **削除する** | Delete | カード右クリック「削除」 | プレイリストを削除 | `DeletePlaylistCommand` |
| **楽曲を追加する** | Add Track | 楽曲右クリック「プレイリストに追加...」 | `PlaylistSelectionDialog` で対象選択 | `ShowAddToPlaylistDialogCommand` |
| **楽曲を除外する** | Remove Track | シングルビュー内の楽曲右端「✕」ボタン | プレイリストから該当曲を除外 | `RemoveTrackFromPlaylistCommand` |
| **一覧へ戻る** | Close | シングルビュー右上の閉じるボタン | プレイリスト一覧 (`PlaylistSelectorView`) へ戻る | `CloseCommand` |

## 5. OOUI設計上の課題と今後の指針 (To-Be)
- **直接操作（D&D投入）の実現**: プレイリストへの楽曲追加がモーダルダイアログ経由に限定されているため、サイドバーに常時プレイリスト一覧を表示し、楽曲を直接ドロップして追加できる操作感を導入します。
- **インライン名称変更**: ダイアログではなく、タイトルテキストのダブルクリック等によるインプレース編集を実現します。
