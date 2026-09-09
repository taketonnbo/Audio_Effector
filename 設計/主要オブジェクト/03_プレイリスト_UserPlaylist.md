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
| **IsSmartPlaylist** | `bool` | スマートプレイリスト（動的自動抽出）かどうか | ○ (バッジ/アイコン) | ○ | プレイリスト種別 |
| **FilterRule** | `string?` | スマートプレイリスト抽出条件式（JSON/定義） | - | ○ (編集ダイアログ) | 抽出ルール |

## 4. アクション（操作）定義

| アクション名 | 動詞 | トリガー / 操作 | 影響・結果 | 関連コマンド / 実装 |
| :--- | :--- | :--- | :--- | :--- |
| **新規作成** | Create | 上部「+ NEW PLAYLIST」ボタン、または右クリック「新規プレイリスト作成」 | 空の新規プレイリストを作成 | `CreatePlaylistCommand` ([PlaylistViewModel.cs](file:///c:/Users/tnish/vs_code_git/Audio_Effector/AudioEffector/Presentation/ViewModels/PlaylistViewModel.cs)) |
| **シングル詳細表示** | Open | プレイリストカードのクリック | `PlaylistTracksView` へ画面遷移 | `ShowPlaylistCommand` |
| **一括再生する** | Play | カード右クリック「再生」 | プレイリスト全曲をキューに読み込み再生開始 | `PlayPlaylistCommand` |
| **シャッフル再生** | Shuffle Play | カード右クリック「シャッフル再生」 | キューをシャッフルして再生開始 | `ShufflePlayPlaylistCommand` |
| **名前を変更する** | Rename | カード右クリック「名前の変更」、またはインラインクリック | ダイアログまたはインプレース編集で更新 | `RenamePlaylistCommand` |
| **削除する** | Delete | カード右クリック「削除」 | プレイリストを削除（Ctrl+Zで復元可能） | `DeletePlaylistCommand` |
| **楽曲を追加する** | Add Track | 楽曲右クリック「プレイリストに追加...」、または楽曲D&D投入 | プレイリストへ楽曲を追加 | `ShowAddToPlaylistDialogCommand` |
| **楽曲を除外する** | Remove Track | シングルビュー内の楽曲右端「✕」ボタン | プレイリストから該当曲を除外（Undo対応） | `RemoveTrackFromPlaylistCommand` |
| **M3Uエクスポート** | Export M3U | カード/詳細右クリック「M3Uとしてエクスポート」 | 標準プレイリストファイル（.m3u/.m3u8）を保存 | `ExportPlaylistCommand` |
| **M3Uインポート** | Import M3U | ツールバー「M3Uインポート」またはM3UファイルのD&D | 外部プレイリストを読み込み新規作成 | `ImportPlaylistCommand` |
| **クイック検索（絞り込み）**| Filter | サイドバーヘッダー横の虫眼鏡（🔍）アイコン押下しキーワード入力 | サイドバー内のプレイリスト一覧をインクリメンタル絞り込み | `PlaylistFilterText` |
| **セクション開閉** | Toggle Expander | サイドバーの「プレイリスト」ヘッダークリック | プレイリスト一覧の折りたたみ / 展開 | UIトグル状態 |
| **一覧へ戻る** | Close | シングルビュー右上の閉じるボタン、または戻るボタン | 直前の表示画面へ戻る | `CloseCommand` / `GoBack` |

## 5. OOUI設計上の課題と今後の指針 (To-Be)
- **直接操作（D&D投入）とモードレスなサイドバー展開**:
  従来の「全画面を切り替えてプレイリスト詳細を開く」方式を排し、左サイドバーにプレイリスト一覧を常設展開。目的のリストをクリックするだけで右側ワークスペースに該当曲が即座に表示され、他画面から楽曲を直接ドロップして追加できる操作感を導入する。
- **サイドバー縦長化防止（独立スクロール ＆ 折りたたみ Expander）**:
  プレイリスト数が増加しても下部の「設定」や「ツール」項目が押し出されないよう、プレイリスト部分専用の独立スクロールコンテナ（`ScrollViewer` + UI仮想化）を採用。ヘッダークリックによるセクション全体の折りたたみ（Expander）にも対応する。
- **インラインクイック検索（ヘッダー横の虫眼鏡アイコン）**:
  多数のプレイリストを保有している場合でも、サイドバーヘッダー横の虫眼鏡アイコンから即座にキーワード絞り込み（リアルタイムフィルター）を行えるようにし、目的のプレイリストへのクイックアクセスを可能とする。
- **スマートプレイリスト（動的抽出）の導入**: 「最近追加された10アルバム」「再生回数トップ100」等、ルールに基づく自動更新プレイリストを通常プレイリストと統一的に管理・再生可能にする。
- **誤操作の取り消し（Undo）**: 楽曲の除外や並び替え、誤った削除を `Ctrl+Z` で直前の状態へ安全に取り消せるようにする。
