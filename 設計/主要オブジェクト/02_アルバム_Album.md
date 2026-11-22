# オブジェクト抽出テーブル: アルバム (Album)

## 1. 基本情報
- **オブジェクト名**: アルバム
- **英語名**: Album
- **分類**: プライマリオブジェクト / コンテナオブジェクト
- **責務**: 同一アルバム名でまとめられた楽曲群を集約し、ジャケット画像・音質フラグとともに音楽作品としてのまとまりを提供する。

## 2. コレクション / シングル構成

> [!IMPORTANT]
> **コレクション / シングル構成**:
> - **コレクション表示**:
>   - `LibraryView.xaml`: グリッド表示（カード形式）とリスト表示（行形式）をツールバーの `ToggleViewCommand` で切り替え可能。ソート基準（`Artist`, `Album`）および昇順/降順の切り替えに対応。
>   - `ArtistsView.xaml`: 選択アーティストの右ペイン上部にて所属アルバムカードを横スクロール一覧表示。
> - **展開・詳細表示**:
>   - `LibraryView.xaml` の各アルバムカード/行において、インラインで収録曲リストを展開表示（グリッド時はソケット風オーバーレイトレイ、リスト時はExpander）します。また、メイン画面右側パネルに選択・再生中アルバムのアートワークと収録曲リストが表示されます（独立した `AlbumDetailView` 画面への遷移は Issue #188 にて計画）。

## 3. 属性（プロパティ）定義

| プロパティ名 | 型 | 説明 | コレクション表示 | 展開・詳細表示 | 実装プロパティ ([Album.cs](../../AudioEffector/Domain/Entities/Album.cs)) |
| :--- | :--- | :--- | :---: | :---: | :--- |
| **Title / Name** | `string` | アルバムタイトル名 | ○ | ○ | `Album.Title`, `Album.Name` |
| **Artist** | `string` | アルバムアーティスト名 | ○ | ○ | `Album.Artist` |
| **Year** | `uint` | リリース年 | - | ○ | `Album.Year` |
| **CoverImage** | `BitmapImage?` | アルバムジャケット画像 | ○ | ○ | `Album.CoverImage` |
| **RepresentativeTrackFilePath** | `string` | アート遅延読込用の代表トラックファイルパス | ○ (`AlbumArtLoader`) | ○ | `Album.RepresentativeTrackFilePath` |
| **Tracks** | `List<Track>` | 所属楽曲リスト | - | ○ (インライン展開リスト) | `Album.Tracks` |
| **TrackCount** | `int` | 収録楽曲数 | ○ (アーティスト画面等) | ○ (トレイヘッダー等) | `Album.TrackCount` |
| **TotalDuration** | `TimeSpan` | アルバム総再生時間 | - | ○ | `Album.TotalDuration` |
| **ContainsHiRes / ContainsLossless** | `bool` | ハイレゾ / ロスレス楽曲を含むか | ○ (音質バッジ) | ○ | `Album.ContainsHiRes`, `Album.ContainsLossless` |
| **IsOnDevice** | `bool` | 接続機器に全収録曲が転送済みか | ○ (機器転送選択時グレーアウト) | - | `Album.IsOnDevice` |
| **IsSelected** | `bool` | 機器転送選択モード時の選択状態（配下トラックと連動） | ○ (CheckBox) | - | `Album.IsSelected` |
| **IsTracksExpanded** | `bool` | トラック展開中かどうか（グリッド時は単一排他展開） | ○ | - | `Album.IsTracksExpanded` |
| **TotalSize / BitrateDisplay / FormatDisplay / DateAdded** | 各種 | 総データサイズ・代表ビットレート・追加日時 | - | - | 拡張構想 (#188 / #233) |

## 4. アクション（操作）定義

| アクション名 | 動詞 | トリガー / 操作 | 影響・結果 | 状態 | 関連コマンド / 実装 |
| :--- | :--- | :--- | :--- | :---: | :--- |
| **一括再生する** | Play All | アルバムの再生ボタンクリック、または右クリック「再生」 | アルバム全曲をキューに展開し再生開始（シャッフルON時はランダム開始） | 実装済み | `PlayAlbumCommand` ([LibraryViewModel.cs](../../AudioEffector/Presentation/ViewModels/LibraryViewModel.cs)) |
| **次に再生** | Play Next | アルバム右クリック「次に再生」 | アルバム全曲を予約キューの先頭に一括挿入 | 実装済み | `PlayNextAlbumCommand` |
| **最後に再生** | Enqueue | アルバム右クリック「最後に再生」 | アルバム全曲を再生キューの末尾に追加 | 実装済み | `EnqueueAlbumCommand` |
| **トラック一覧展開** | Expand Tracks | グリッド時: 下部トグルボタン押下 / リスト時: Expander押下 | 収録曲リスト（ソケットトレイまたはExpander）を展開 | 実装済み | `IsTracksExpanded` |
| **プレイリスト追加** | Add to Playlist | アルバム内 `+` ボタン、または右クリック「プレイリストに追加...」 | プレイリスト選択ダイアログからアルバム収録曲を一括追加 | 実装済み | `ShowAddToPlaylistDialogCommand` ([PlaylistViewModel.cs](../../AudioEffector/Presentation/ViewModels/PlaylistViewModel.cs)) |
| **削除する** | Delete | アルバム右クリック「削除」 | 確認ダイアログのうえアルバムと収録曲をライブラリ・お気に入りから除外 | 実装済み | `DeleteAlbumCommand` ([LibraryViewModel.cs](../../AudioEffector/Presentation/ViewModels/LibraryViewModel.cs)) |
| **フォルダスキャン** | Scan Folder | フォルダ画面の「SCAN FOLDER」ボタン | 指定フォルダを走査しアルバム・楽曲を構築 | 実装済み | `ScanFolderAsync` ([LibraryApplicationService.cs](../../AudioEffector/Application/ApplicationServices/LibraryApplicationService.cs)) |
| **詳細情報画面の表示** | Show Details | アルバムの三点リーダーボタン等 | アルバム詳細シングルビューの表示 | 拡張構想 (#188) | `ShowAlbumInfoCommand` ([LibraryViewModel.cs](../../AudioEffector/Presentation/ViewModels/LibraryViewModel.cs)) |

## 5. 拡張設計指針
- **アルバム詳細シングルビュー（Issue #188）**: インライン展開に加え、選択アルバムの大アート・メタ情報・全トラックテーブルを中央ワークスペースで閲覧・操作できる詳細ビューへの遷移。
- **リスト表示の展開トラック情報拡充（Issue #264）**: リスト表示で展開したトラック行への再生時間・アーティスト・お気に入り・トラック番号・音質バッジの表示拡充。
