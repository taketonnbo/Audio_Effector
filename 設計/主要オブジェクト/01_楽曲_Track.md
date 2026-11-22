# オブジェクト抽出テーブル: 楽曲 (Track)

## 1. 基本情報
- **オブジェクト名**: 楽曲
- **英語名**: Track（関連エンティティ: `FavoriteTrackEntry`, `RecentTrackEntry`）
- **分類**: プライマリオブジェクト（音楽再生・管理の最小単位）
- **責務**: 音声ファイルの実体およびメタデータを保持し、再生・キュー追加・プレイリスト追加・お気に入り・再生履歴・機器転送の基本単位となる。

## 2. コレクション / シングル構成

> [!IMPORTANT]
> **コレクション / シングル構成**:
> - **コレクション表示**: 以下の主要画面でコレクションとして表示・操作されます：
>   1. `AllSongsView.xaml`: ライブラリ全楽曲のフラットな一覧（4項目横断検索・5項目ソート対応、UIラッパー `AllSongsTrackItem` 使用）
>   2. `ArtistsView.xaml`: 選択したアーティスト（`ArtistGroup`）の所属楽曲一覧
>   3. `LibraryView.xaml`: 各アルバムを展開した際のインライン曲リスト（グリッド時ソケットトレイ / リスト時Expander）
>   4. `PlaylistTracksView.xaml`: プレイリスト内楽曲リスト、およびお気に入り楽曲リスト
>   5. `PlayQueueSidePanel.xaml` / `PlayQueueDialog.xaml`: 再生キュー（予約キュー・アルバムの残り曲）および再生履歴タブ
>   6. `RecentView.xaml`: 最近再生した曲一覧（`RecentTrackEntry` として再生日時 `PlayedAt` とともに永続化・表示）
> - **シングル表示**: メイン画面右側パネルの楽曲情報・Player Pod、上部プレイヤーバー、ミニプレイヤー（`MiniPlayerWindow.xaml`）、およびプロパティ表示ダイアログ（`ShowTrackPropertiesCommand`）として提供されます。

## 3. 属性（プロパティ）定義

| プロパティ名 | 型 | 説明 | コレクション表示 | シングル表示 | 実装プロパティ ([Track.cs](../../AudioEffector/Domain/Entities/Track.cs)) |
| :--- | :--- | :--- | :---: | :---: | :--- |
| **Id** | `TrackId` | 楽曲の一意識別子 | - | - | `Track.Id` |
| **Title** | `string` | 楽曲タイトル（曲名） | ○ | ○ | `Track.Title` |
| **Artist** | `string` | アーティスト名 | ○ | ○ | `Track.Artist` |
| **Album** | `string` | 所属アルバム名 | ○ (全曲/アーティスト/最近再生) | ○ | `Track.Album` |
| **Duration** | `TimeSpan` | 再生時間（分:秒） | ○ | ○ | `Track.Duration`, `Track.DurationString` |
| **TrackNumber** | `uint` | アルバム内トラック番号 | ○ (アルバム展開/アーティスト) | ○ | `Track.TrackNumber` |
| **Year** | `uint` | リリース年 | - | ○ | `Track.Year` |
| **Genre** | `string` | ジャンル | ○ (全曲一覧) | ○ | `Track.Genre` |
| **Format** | `string` | 音声フォーマット（MP3, FLAC, WAV等） | - | ○ | `Track.Format` |
| **Bitrate** | `int` | ビットレート (kbps) | - | ○ | `Track.Bitrate` |
| **SampleRate** | `int` | サンプルレート (Hz) | - | ○ | `Track.SampleRate` |
| **BitsPerSample** | `int` | 量子化ビット数 (bit) | - | ○ | `Track.BitsPerSample` |
| **IsLossless** | `bool` | 可逆圧縮音源かどうか | - | ○ | `Track.IsLossless` |
| **IsHiRes** | `bool` | ハイレゾ音源かどうか | - | ○ | `Track.IsHiRes` |
| **QualityLabel / QualityInfo** | `string` | 音質バッジ表記（`Hi-Res` / `Lossless`）と詳細文字列 | - | ○ (右側パネル) | `Track.QualityLabel`, `Track.QualityInfo` |
| **IsFavorite** | `bool` | お気に入り状態（`FavoriteTrackEntry` によりメタデータベース永続化） | ○ (ハート/星アイコン) | ○ | `Track.IsFavorite` |
| **IsPlaying** | `bool` | 現在再生対象かどうか | ○ (再生アイコン/シアン強調) | ○ | `Track.IsPlaying` |
| **CoverImage / AlbumArt** | `BitmapImage?` | アルバムアート画像（遅延読込対応） | ○ (サムネイル) | ○ | `Track.CoverImage`, `Track.AlbumArt` |
| **FilePath** | `string` | ローカルファイルパス | - | ○ | `Track.FilePath` |
| **IsSelected** | `bool` | 機器転送等の選択状態 | ○ (CheckBox) | - | `Track.IsSelected` |
| **Tags** | `string[]` | ユーザー定義タグ / 検索タグ | - | - | 拡張構想（#233） |

## 4. アクション（操作）定義

| アクション名 | 動詞 | トリガー / 操作 | 影響・結果 | 状態 | 関連コマンド / 実装 |
| :--- | :--- | :--- | :--- | :---: | :--- |
| **再生する** | Play | 楽曲ダブルクリック/ボタンクリック、または右クリック「再生」 | 選択曲の再生開始 | 実装済み | `PlayTrackCommand` ([PlayerControlViewModel.cs](../../AudioEffector/Presentation/ViewModels/PlayerControlViewModel.cs)) |
| **次に再生** | Play Next | 楽曲右クリック「次に再生」 | 予約キューの先頭に割り込み挿入 | 実装済み | `PlayNextCommand` |
| **最後に再生** | Enqueue | 楽曲右クリック「最後に再生」 | 再生キューの末尾へ追加 | 実装済み | `EnqueueTrackCommand` |
| **お気に入り切替** | Toggle Favorite | お気に入りボタン、または右クリック「お気に入りに追加/解除」 | `IsFavorite` の反転と `favorites.json` へのメタデータ保存 | 実装済み | `ToggleFavoriteCommand` ([LibraryViewModel.cs](../../AudioEffector/Presentation/ViewModels/LibraryViewModel.cs)) |
| **プレイリスト追加** | Add to Playlist | `+` ボタン、または右クリック「プレイリストに追加...」 | プレイリスト選択ダイアログから対象リストへ楽曲を追加 | 実装済み | `ShowAddToPlaylistDialogCommand` ([PlaylistViewModel.cs](../../AudioEffector/Presentation/ViewModels/PlaylistViewModel.cs)) |
| **プロパティ表示** | Properties | 楽曲右クリック「プロパティ」 | 楽曲プロパティ情報ダイアログ表示 | 実装済み | `ShowTrackPropertiesCommand` ([MainViewModel.cs](../../AudioEffector/Presentation/ViewModels/MainViewModel.cs)) |
| **ファイルの場所** | Open Location | 楽曲右クリック「ファイルの場所を開く」 | エクスプローラーで該当ファイルを選択表示 | 実装済み | `OpenFileLocationCommand` |
| **削除する** | Delete | 楽曲右クリック「削除」 | 確認ダイアログのうえライブラリおよびお気に入りから除外（実ファイルは非削除） | 実装済み | `DeleteTrackCommand` |
| **複数選択一括操作 / D&D / Undo** | Bulk / D&D / Undo | 複数行選択後のD&D投入、一括タグ編集、`Ctrl+Z` 取り消し | キュー・プレイリストへの直接D&D投入および操作復元 | 拡張構想 (#233) | — |

## 5. 拡張設計指針
- **直接操作（ダイレクトマニピュレーション）**: コンテキストメニューに加え、選択楽曲群を再生キュー（`PlayQueueSidePanel`）やサイドバーのプレイリストへ直接ドラッグ＆ドロップ投入できる操作系への拡張。
- **操作の取り消し（Undo）**: 楽曲の除外・削除・並び替え等の変更操作を `Ctrl+Z` で復元できる履歴管理機構の導入。
