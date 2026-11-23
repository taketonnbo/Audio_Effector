# アルバム一覧 UI仕様書

## 1. 概要 (Overview)

アルバムと収録曲を閲覧し、再生・キュー追加・プレイリスト追加を行う。主要対象はAlbumとTrack。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
【グリッド表示モード (Grid View)】
+--------------------------------------------------------------------------------------------------------------------+
| [ソート: アーティスト名 ▼] [▲昇順]   [田 グリッド | ≡ リスト]   [🔍 アルバムを検索...              ]     全 86 作品 |
+--------------------------------------------------------------------------------------------------------------------+
| +---------------+  +---------------+  +---------------+  +---------------+  +---------------+                      |
| |               |  |  [▶ 再生]     |  |               |  |               |  |               |                      |
| |  [Art 160px]  |  |  (Hover時)    |  |  [Art 160px]  |  |  [Art 160px]  |  |  [Art 160px]  |                      |
| |               |  |  [≡ 展開]     |  |               |  |               |  |               |                      |
| +---------------+  +---------------+  +---------------+  +---------------+  +---------------+                      |
| Random Access M... Hurry Up, We're... Starboy            Currents           Discovery                              |
| Daft Punk          M83                The Weeknd         Tame Impala        Daft Punk                              |
| 2013 • 13曲        2011 • 22曲        2016 • 18曲        2015 • 13曲        2001 • 14曲                            |
|                                                                                                                    |
| (トレイ展開時: カード直下に収録曲がインライン展開)                                                                 |
| +-----------------------------------------------------------------------------------------+                        |
| | [▼] Random Access Memories - 収録曲 (13曲, 74分)                       [▶ 全曲再生] [✕] |                        |
| |   1. Give Life Back to Music (04:34)    2. The Game of Love (05:22)                     |                        |
| |  >3. Giorgio by Moroder (09:04)         4. Within (03:48)                               |                        |
| |   5. Instant Crush (05:37)              6. Lose Yourself to Dance (05:53)               |                        |
| +-----------------------------------------------------------------------------------------+                        |
+--------------------------------------------------------------------------------------------------------------------+

【リスト表示モード (List View)】
+--------------------------------------------------------------------------------------------------------------------+
| [ソート: アルバム名 ▼]     [▲昇順]   [田 グリッド | ≡ リスト]                                                       |
+--------------------------------------------------------------------------------------------------------------------+
| [v] [40x40] Random Access Memories / Daft Punk                                                                     |
|   +--------------------------------------------------------------------------------------------------------------+ |
|   |   |  #  | ★ | タイトル                       | アーティスト        | 形式            |  時間 |              | |
|   |---+-----+---+--------------------------------+---------------------+-----------------+-------+--------------| |
|   |   |  1  | ☆ | Give Life Back to Music        | Daft Punk           | [FLAC][Hi-Res]  | 04:34 |     [+]      | |
|   | | |  ▶  | ★ | Instant Crush                  | Daft Punk feat. ... | [FLAC][Hi-Res]  | 05:37 |     [+]      | |
|   |   |  -  | ☆ | Bonus Track                    | Daft Punk           | [MP3]           | 03:12 |     [+]      | |
|   +--------------------------------------------------------------------------------------------------------------+ |
| [>] [40x40] Hurry Up, We're Dreaming / M83                                                                         |
+--------------------------------------------------------------------------------------------------------------------+
```

> **構想要素に関する注記**: モックアップ内のアルバム検索ボックス・作品数サマリーは拡張構想（関連Issue: #200）です。リスト表示の展開楽曲リスト詳細列（トラック番号、再生中インジケーター、お気に入りトグル、アーティスト、形式/音質バッジ、再生時間）は Issue #264 にて実装済みです。

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`CurrentViewType = Albums`（起動時初期画面）、`DataContext` は `MainViewModel.Library`（`LibraryViewModel`）。2行 `Grid` の上部がソート基準 `ComboBox`（`Artist` / `Album`）、昇降順トグルボタン、表示切替ボタン（グリッド / リスト）、下部がアルバム一覧。`IsListView` では縦 `ListBox`、`IsGridView` では `WrapPanel` のカード配置を使用する。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 画面背景 |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | カード・トレイ・コントロール背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | アルバム名・曲名テキスト |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | アーティスト名 |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | 補助テキスト・トラック番号 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | テーマ連動アクセント |
| `AlwaysNeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | アート暗幕上の再生・操作アクセント |

## 5. 構成要素と共通コントロール

- 一覧は `Albums`、各アルバムの収録曲は `Tracks`。アルバムタイトル・アーティスト名およびグリッド展開トレイ内のトラックタイトルには `MarqueeTextBlock`（ホバー連動シームレスループ）を適用する。
- グリッド表示の収録曲トレイはカード配置を押し広げずにカード直下へオーバーレイ展開され、**単一排他展開制御**（別アルバム展開時、トレイ外クリック時、ソート変更時、グリッド/リスト表示切替時に自動で閉じる）を行う。
- リスト表示の展開領域は、`AllSongsView` と統一したコンパクトな列ヘッダー行（`#`、`★`、`タイトル`、`アーティスト`、`形式`、`時間`）と各楽曲行で構成する。
  - 各楽曲行は `Border` コンテナ（ホバー時 `ControlBackgroundHighlightBrush`）上に、左端3px再生中アクセントバー、選択 `CheckBox`（転送選択モード時のみ表示）、トラック番号（`TrackNumber`、`0` は `-`、再生中は `▶` + `NeonCyanBrush`）、お気に入りトグルボタン（`★`/`☆`、`ToggleFavoriteCommand`）、曲名（`Title`、省略＋`ToolTip`、再生中 `NeonCyanBrush` ハイライト）、アーティスト（`Artist`、省略＋`ToolTip`）、フォーマット/音質バッジ（`Format` + `QualityLabel`、`ToolTip` に `QualityInfo`）、再生時間（`DurationString`、右揃え）、プレイリスト追加 `+` ボタン（`Playlist.ShowAddToPlaylistDialogCommand`）を配置する。
  - 行コンテナを非ネストの `Border` とし、子ボタン（お気に入り・`+`）および `CheckBox` 押下時は行クリック再生（`PlayTrackCommand`）を発火させない。
- 機器転送選択モード時（`IsSelectionMode = True`）の `CheckBox` は `Album.IsSelected` / `Track.IsSelected` に連動し、接続機器上に全曲存在するアルバム（`IsOnDevice = True`）は非活性となる。

## 6. アニメーションとインタラクション

- **ソート・表示切替**: ソート項目は `Artist` / `Album`、昇降順は `ToggleSortDirectionCommand`、グリッド/リスト切替は `ToggleViewCommand`。
- **再生**: アルバムの再生ボタンまたは右クリック「再生」は `PlayAlbumCommand`（シャッフルON時はアルバム内のランダム曲から再生開始）、展開トラックのクリックは `PlayTrackCommand`。
- **コンテキストメニュー**:
  - アルバム: 「再生」「次に再生」「最後に再生」「お気に入りに追加/解除」「プレイリストに追加...」「削除」
  - トラック: 「再生」「次に再生」「最後に再生」「お気に入りに追加/解除」「プレイリストに追加...」「プロパティ」「ファイルの場所を開く」「削除」
- **削除確認**: `DeleteAlbumCommand` / `DeleteTrackCommand` は確認 `MessageBox` を表示し、ライブラリおよびお気に入り（`favorites.json`）から除外する（ディスク上の実ファイルは削除しない）。
- **詳細情報ボタン**: 三点ボタン（`ShowAlbumInfoCommand`）によるアルバム詳細シングルビューへの遷移は Issue #188 にて計画。

## 7. パフォーマンス最適化要件

リスト側は `VirtualizingStackPanel.IsVirtualizing=True`、`VirtualizationMode=Recycling`、`ScrollUnit=Pixel`、`ScrollViewer.CanContentScroll=True` を指定。グリッド側はScrollViewer＋ItemsControl＋WrapPanel、収録曲もItemsControlで、UI仮想化を行わない。共通の大規模一覧要件との差を評価する際はこの構成を使用する。非選択表示はBoolToVisでCollapsed。アートのボタン・トレイにDropShadowEffectが存在するが、リスト表示の展開楽曲行内には高負荷エフェクトを使用しない。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [LibraryView.xaml](../../../AudioEffector/Presentation/Views/LibraryView.xaml)
- [ユースケース](../../ユースケース/02_楽曲ライブラリ管理.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/LibraryView.xaml.cs)
- [LibraryViewModel](../../../AudioEffector/Presentation/ViewModels/LibraryViewModel.cs)
- 関連Issue（設計経緯）: [#166](https://github.com/taketonnbo/Audio_Effector/issues/166)、[#167](https://github.com/taketonnbo/Audio_Effector/issues/167)、[#168](https://github.com/taketonnbo/Audio_Effector/issues/168)、[#264](https://github.com/taketonnbo/Audio_Effector/issues/264)
