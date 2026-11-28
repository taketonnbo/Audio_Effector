# プレイリスト一覧 UI仕様書

## 1. 概要 (Overview)

ユーザーのプレイリストをカードから開き、新規作成・削除する。対象はUserPlaylist。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+--------------------------------------------------------------------------------------------------------------------+
| [+ 新規プレイリスト作成]   [🔍 プレイリストを検索...                        ]                   全 12 プレイリスト |
+--------------------------------------------------------------------------------------------------------------------+
| +---------------+  +---------------+  +---------------+  +---------------+  +---------------+                      |
| | +-----+-----+ |  | +-----+-----+ |  | +-----+-----+ |  | +-----+-----+ |  |               |                      |
| | | A1  | A2  | |  | | B1  | B2  | |  | | C1  | C2  | |  | | D1  | D2  | |  |      [+]      |                      |
| | +-----+-----+ |  | +-----+-----+ |  | +-----+-----+ |  | +-----+-----+ |  |               |                      |
| | | A3  | A4  | |  | | B3  | B4  | |  | | C3  | C4  | |  | | D3  | D4  | |  | 新規作成      |                      |
| | +-----+-----+ |  | +-----+-----+ |  | +-----+-----+ |  | +-----+-----+ |  |               |                      |
| +---------------+  +---------------+  +---------------+  +---------------+  +---------------+                      |
| Driving Beats      Chillout Lounge    Synthwave 80s      Acoustic Favorites (クリックで作成)                       |
| 32 曲 • 2.1 時間   45 曲 • 3.2 時間   28 曲 • 1.9 時間   18 曲 • 1.2 時間                                          |
|                                                                                                                    |
| (カードホバー時: オーバーレイ再生ボタン [▶] およびメニュー [⋯] が表出)                                             |
+--------------------------------------------------------------------------------------------------------------------+
```

> **構想要素に関する注記**: モックアップ内のプレイリスト検索ボックス・件数/時間サマリー・末尾の新規作成カード・ホバー再生オーバーレイは拡張構想（関連Issue: #233, #242, #252）です。

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`CurrentViewType = Playlists`、`DataContext` は `MainViewModel.Playlist`（`PlaylistViewModel`）。行0が新規作成ボタン（`Auto`）、行1がプレイリストカード一覧（`*`）。`ListBox` の `ItemsPanel` は中央揃えの `WrapPanel` で、ウィンドウ幅に応じてカードを折り返す。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | 作成ボタン・カード背景 |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | ボタン・カードホバー背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | プレイリスト名テキスト |
| `BorderBrush` | <span style="background-color:#343E4E;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #343E4E</span> | `#343E4E` | ボタン・カード境界線 |

## 5. 構成要素と共通コントロール

- 上部に「`+ Create New Playlist`」ボタン、一覧領域に `UserPlaylists` のカード、各カード右上に削除「`-`」ボタンを配置する。
- **2×2分割サムネイル抽出仕様**: 各プレイリストの登録曲から親ディレクトリ（アルバムフォルダ）単位で重複を排除して最大4曲のパスを抽出し、`Thumbnail1`〜`Thumbnail4` プロパティ経由で左上→右上→右下→左下の順に `AlbumArtLoader` で非同期表示する。
- **永続化仕様**: プレイリスト一覧は `%AppData%\AudioEffector\playlists.json` に永続化される。

## 6. アニメーションとインタラクション

- **新規作成**: 「`+ Create New Playlist`」ボタン（`CreatePlaylistCommand`）押下で `InputBox` ダイアログを表示し、名称を入力して新規作成・保存する。また、リスニング統計ビューの「`＋ プレイリスト化`」アクションから `CreatePlaylistWithTracks` 経由で TOP 10 楽曲を順序保持で一括登録した新規プレイリストを作成・一覧へ即時追加できる。
- **詳細画面への遷移**: カード左クリックまたは右クリック「開く」（`SelectPlaylistCommand`）で `PlaylistTracksView`（収録曲画面）へ遷移する。
- **コンテキストメニュー**: カード右クリックで「開く」「再生」（`PlayPlaylistCommand`）「シャッフル再生」（`ShufflePlayPlaylistCommand`）「名前の変更」（`RenamePlaylistCommand`、`InputBox` に現在名をプリセット）「削除」（`DeletePlaylistCommand`）を表示する。登録曲が0件のプレイリストに対する再生・シャッフル再生は何も行わない。
- **削除確認**: カード右上の「`-`」ボタンまたは右クリック「削除」実行時は、確認 `MessageBox` を表示した上で削除・保存する。

## 7. パフォーマンス最適化要件

ListBox内のWrapPanelは仮想化パネルではない。標準の大量コレクション向けRecycling／Pixel条件はこの構成に適用されない。アートはAlbumArtLoaderで取得する。画面切替は親が担当。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [PlaylistSelectorView.xaml](../../../AudioEffector/Presentation/Views/PlaylistSelectorView.xaml)
- [ユースケース](../../ユースケース/03_プレイリスト管理.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [プレイリスト内楽曲UI仕様書](../プレイリスト内楽曲/プレイリスト内楽曲UI仕様書.md)
- [文字入力ダイアログUI仕様書](../文字入力ダイアログ/文字入力ダイアログUI仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/PlaylistSelectorView.xaml.cs)
- [PlaylistViewModel](../../../AudioEffector/Presentation/ViewModels/PlaylistViewModel.cs)
