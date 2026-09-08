# オブジェクト抽出テーブル: アルバム (Album)

## 1. 基本情報
- **オブジェクト名**: アルバム
- **英語名**: Album
- **分類**: プライマリオブジェクト / コンテナオブジェクト
- **責務**: 同一アルバム名およびアーティスト名でまとめられた楽曲群を集約し、ジャケット画像とともに音楽作品としてのまとまりを提供する。

## 2. コレクション / シングル構成

> [!IMPORTANT]
> **実装仕様 (As-Is)**:
> - **コレクション表示**: `LibraryView.xaml`
>   - グリッド表示（カード形式: カバー画像、タイトル、アーティスト）とリスト表示（行形式）をツールバーの `ToggleViewCommand` で相互に切り替え可能。
>   - ソート基準（Artist, Title, Year等）および昇順/降順の切り替えが可能。
> - **シングル表示**: 独立した画面（シングルビュー）は **存在しません**。
>   - 代わりに、`LibraryView.xaml` の各アルバムカード/行において、インラインで収録曲リストを展開表示する方式（グリッド時はソケット風オーバーレイ #166/#167、リスト時はExpander）となっています。また、右側タブ領域にアルバム情報が表示されます。

## 3. 属性（プロパティ）定義

| プロパティ名 | 型 | 説明 | コレクション表示 | 展開・詳細表示 | 実装プロパティ ([Album.cs](file:///c:/Users/tnish/vs_code_git/Audio_Effector/AudioEffector/Domain/Entities/Album.cs)) |
| :--- | :--- | :--- | :---: | :---: | :--- |
| **Title / Name** | `string` | アルバムタイトル名 | ○ | ○ | `Album.Title`, `Album.Name` |
| **Artist** | `string` | アルバムアーティスト名 | ○ | ○ | `Album.Artist` |
| **Year** | `uint` | リリース年 | △ (ソート・詳細時) | ○ | `Album.Year` |
| **CoverImage** | `BitmapImage?` | アルバムジャケット画像 | ○ (カードアート) | ○ (右カラム大表示) | `Album.CoverImage` |
| **Tracks** | `List<Track>` | 所属楽曲リスト | - | ○ (インライン展開リスト) | `Album.Tracks` |
| **TrackCount** | `int` | 収録楽曲数 | - | ○ (トレイヘッダー等) | `Album.TrackCount` |
| **TotalDuration** | `TimeSpan` | アルバム総再生時間 | - | - | `Album.TotalDuration` |
| **IsOnDevice** | `bool` | 接続機器に転送済みか | ○ (選択モード時バッジ) | - | `Album.IsOnDevice` |
| **IsSelected** | `bool` | 選択モード時の選択状態 | ○ (CheckBox) | - | `Album.IsSelected` |
| **IsTracksExpanded**| `bool` | トラック展開中かどうか | ○ | - | `Album.IsTracksExpanded` |

## 4. アクション（操作）定義

| アクション名 | 動詞 | トリガー / 操作 | 影響・結果 | 関連コマンド / 実装 |
| :--- | :--- | :--- | :--- | :--- |
| **一括再生する** | Play All | アルバムアートホバー時の再生ボタンクリック、または右クリック「再生」 | アルバム全曲をキューに展開し、1曲目を再生開始 | `PlayAlbumCommand` ([LibraryViewModel.cs](file:///c:/Users/tnish/vs_code_git/Audio_Effector/AudioEffector/Presentation/ViewModels/LibraryViewModel.cs)) |
| **次に再生** | Play Next | アルバム右クリック「次に再生」 | アルバム全曲を現在再生曲の直後に割り込み | `PlayNextAlbumCommand` |
| **最後に再生** | Enqueue | アルバム右クリック「最後に再生」 | アルバム全曲をキューの末尾に追加 | `EnqueueAlbumCommand` |
| **トラック一覧展開**| Expand Tracks | ・グリッド時: ホバー時の下部トグルボタン押下<br>・リスト時: Expanderボタン押下 | 収録曲リスト（SocketTrayまたはExpander）を展開 | `IsTracksExpanded` |
| **プレイリスト追加**| Add to Playlist| アルバム右クリック「プレイリストに追加...」 | ダイアログ (`ShowAddAlbumToPlaylistDialogCommand`) 表示 | `PlaylistViewModel.cs` |
| **削除する** | Delete | アルバムリスト行の右クリック「削除」 | アルバムをライブラリから除外 | `DeleteAlbumCommand` |
| **右側詳細表示** | Show Info | アルバムカードホバー時の三点リーダーボタン押下 (#168) | メイン画面右側タブにアルバム詳細を表示 | 右ペイン連携 |

## 5. OOUI設計上の課題と今後の指針 (To-Be)
- **シングルビューの再定義**: 現在のインライン展開（ソケットオーバーレイ）は省スペースですが、曲数が多いアルバムでの操作性が制限されます。
  OOUIの標準モデルとして、「アルバム一覧（コレクション）」から特定のアルバムを選択した際、中央ワークスペースで「アルバム詳細（大アート・メタ情報・全トラックテーブル）」をシームレスに表示するシングルビューへの移行を検討します。
