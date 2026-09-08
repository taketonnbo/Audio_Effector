# オブジェクト抽出テーブル: 楽曲 (Track)

## 1. 基本情報
- **オブジェクト名**: 楽曲
- **英語名**: Track
- **分類**: プライマリオブジェクト（音楽再生・管理の最小単位）
- **責務**: 音声ファイルの実体およびメタデータを保持し、再生・キュー追加・プレイリスト追加・転送の基本単位となる。

## 2. コレクション / シングル構成

> [!IMPORTANT]
> **コレクション / シングル構成**:
> - **コレクション表示**: 全曲データグリッド画面（`AllSongsView.xaml`）をはじめ、以下の主要画面でコレクションとして表示・操作されます：
>   1. `AllSongsView.xaml`: ライブラリ全楽曲のフラットなデータグリッド一覧（ソート、カラムカスタマイズ対応）
>   2. `LibraryView.xaml`: 各アルバムを展開した際のインライン曲リスト（Expander / SocketTray）
>   3. `PlaylistTracksView.xaml`: プレイリスト／お気に入り楽曲リスト
>   4. `PlayQueueSidePanel.xaml`: 再生キューリスト
>   5. `RecentView.xaml`: 再生履歴リスト
> - **シングル表示**: 楽曲単体の詳細画面（シングルビュー）は、メイン画面右カラムの楽曲情報パネル、またはプロパティ表示ダイアログ（`ShowTrackPropertiesCommand`）として提供されます。


## 3. 属性（プロパティ）定義

| プロパティ名 | 型 | 説明 | コレクション表示 | シングル表示 | 実装プロパティ ([Track.cs](file:///c:/Users/tnish/vs_code_git/Audio_Effector/AudioEffector/Domain/Entities/Track.cs)) |
| :--- | :--- | :--- | :---: | :---: | :--- |
| **Id** | `TrackId` | 楽曲の一意識別子 | - | - | `Track.Id` |
| **Title** | `string` | 楽曲タイトル（曲名） | ○ | ○ | `Track.Title` |
| **Artist** | `string` | アーティスト名 | ○ | ○ | `Track.Artist` |
| **Album** | `string` | 所属アルバム名 | △ (リスト時) | ○ | `Track.Album` |
| **Duration** | `TimeSpan` | 再生時間（分:秒） | ○ | ○ | `Track.Duration`, `Track.DurationDisplay` |
| **TrackNumber** | `uint` | アルバム内トラック番号 | ○ (アルバム展開時) | ○ | `Track.TrackNumber` |
| **Year** | `uint` | リリース年 | - | ○ | `Track.Year` |
| **Genre** | `string` | ジャンル | - | ○ | `Track.Genre` |
| **Format** | `string` | 音声フォーマット（MP3, FLAC, WAV等） | - | ○ | `Track.Format` |
| **Bitrate** | `int` | ビットレート (kbps) | - | ○ | `Track.Bitrate` |
| **SampleRate** | `int` | サンプルレート (Hz) | - | ○ | `Track.SampleRate` |
| **BitsPerSample** | `int` | 量子化ビット数 (bit) | - | ○ | `Track.BitsPerSample` |
| **IsLossless** | `bool` | 可逆圧縮音源かどうか | - | ○ | `Track.IsLossless` |
| **IsHiRes** | `bool` | ハイレゾ音源かどうか | - | ○ | `Track.IsHiRes` |
| **IsFavorite** | `bool` | お気に入り状態 | ○ (星アイコン) | ○ | `Track.IsFavorite` |
| **IsPlaying** | `bool` | 現在再生中かどうか | ○ (波形/再生アイコン) | ○ | `Track.IsPlaying` |
| **CoverImage** | `BitmapImage?` | アート画像 | ○ (キュー/プレイリスト) | ○ | `Track.CoverImage` |
| **FilePath** | `string` | ローカルファイルパス | - | ○ | `Track.FilePath` |
| **IsSelected** | `bool` | 一覧表示での選択状態（複数選択対応） | ○ (チェック/反転行) | - | ViewModel層でバインド |
| **Tags** | `string[]` | ユーザー定義タグ / 検索タグ | △ (全曲グリッド) | ○ | タグ情報 |

## 4. アクション（操作）定義

| アクション名 | 動詞 | トリガー / 操作 | 影響・結果 | 関連コマンド / 実装 |
| :--- | :--- | :--- | :--- | :--- |
| **再生する** | Play | 楽曲ボタンクリック、または右クリック「再生」 | 選択曲の再生開始 | `PlayTrackCommand` ([PlayerControlViewModel.cs](file:///c:/Users/tnish/vs_code_git/Audio_Effector/AudioEffector/Presentation/ViewModels/PlayerControlViewModel.cs)) |
| **次に再生** | Play Next | 楽曲右クリック「次に再生」 | 再生中楽曲の直後に割り込み挿入 | `PlayNextCommand` |
| **最後に再生** | Enqueue | 楽曲右クリック「最後に再生」 | 再生キューの末尾へ追加 | `EnqueueTrackCommand` / `AddToQueueCommand` |
| **お気に入り切替** | Toggle Favorite | 右クリック「お気に入りに追加/解除」 | `IsFavorite` の反転とお気に入り反映 | `ToggleFavoriteCommand` |
| **プレイリスト追加** | Add to Playlist | 右クリック「プレイリストに追加...」、またはプレイリストへのD&D | 対象プレイリストへ楽曲を追加 | `ShowAddToPlaylistDialogCommand` |
| **一括操作** | Bulk Action | 複数行選択後の右クリック、またはドラッグ＆ドロップ | 選択した複数曲の一括お気に入り、一括キュー/プレイリスト追加、一括削除、一括タグ編集 | 複数選択コマンド群 |
| **プロパティ表示** | Properties | 楽曲右クリック「プロパティ」 | 楽曲プロパティ情報ダイアログ表示 | `ShowTrackPropertiesCommand` |
| **ファイルの場所** | Open Location | 楽曲右クリック「ファイルの場所を開く」 | エクスプローラーで該当ファイルを選択表示 | `OpenFileLocationCommand` |
| **削除する** | Delete | 楽曲右クリック「削除」 | ライブラリ管理から削除（Ctrl+Zで復元可能） | `DeleteTrackCommand` |

## 5. OOUI設計上の課題と今後の指針 (To-Be)
- **コレクションビューの正式提供**: `AllSongsView.xaml` をソート・フィルタ・カラム選択が自在なデータグリッドとして提供し、大量音源の一覧性とブラウズ性を高める。
- **複数選択と一括操作（ダイレクトマニピュレーション）**: コンテキストメニュー中心の操作に加え、選択楽曲群を再生キュー（`PlayQueueSidePanel`）やサイドバーのプレイリストへ直接ドラッグ＆ドロップ投入できるようにする。
- **安心の取り消し（Undo）**: 楽曲の除外・削除・並び替え等の変更操作を `Ctrl+Z` で直前の状態へ安全に復元できるようにする。

