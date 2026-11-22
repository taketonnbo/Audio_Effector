# 全曲一覧 UI仕様書

## 1. 概要 (Overview)

ライブラリ内のすべての楽曲（`Track` オブジェクト）をフラットな仮想化リスト形式で一覧表示し、ブラウズ・横断検索・ソート・再生を行うメイン画面。

- **主要操作対象オブジェクト**: [楽曲 (`Track`)](../../主要オブジェクト/01_楽曲_Track.md)（UI行ラッパー: `AllSongsTrackItem`）
- **ユーザー体験**: ライブラリ全体からの高速な楽曲絞り込み（200msデバウンス）、5項目ソート、ダブルクリックによる即時再生、コンテキストメニューからのキュー・プレイリスト連携。

---

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+--------------------------------------------------------------------------------------------------------------------+
| [▶ すべて再生] [シャッフル]   [🔍 楽曲を検索...                 ✕]   [ソート: タイトル ▼] [▲]   全 1,248 曲 (78.5 時間) |
+--------------------------------------------------------------------------------------------------------------------+
|    | [Art] | #   | ★ | タイトル                     | アーティスト          | アルバム              | ジャンル | 時間  |
|----+-------+-----+---+------------------------------+-----------------------+-----------------------+----------+-------|
| || | [24]  | 1   | ★ | Midnight City                | M83                   | Hurry Up, We're D...  | Synthpop | 04:03 |
|    | [24]  | 2   | ☆ | Starboy                      | The Weeknd            | Starboy               | R&B      | 03:50 |
| || | [▶24] | 3   | ★ | Get Lucky (feat. Pharrell)   | Daft Punk             | Random Access Memo... | Disco    | 06:09 |
|    | [24]  | 4   | ☆ | Instant Crush                | Daft Punk             | Random Access Memo... | Synthpop | 05:37 |
|    | ...   | ... |   | ...                          | ...                   | ...                   | ...      | ...   |
+--------------------------------------------------------------------------------------------------------------------+
  ^ 選択行または再生中行: 左端3pxシアンバー (||) / 再生中サムネイルオーバーレイ ([▶24])
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

---

## 3. 画面配置とペインレイアウト

- **位置付け**: `MainWindow` の中央ワークスペース列（`CurrentViewType = AllSongs`、`DataContext = MainViewModel.AllSongs`）。
- **グリッド構造（3行構成）**:
  - 行0 (`Auto`): ツールバー（「▶ すべて再生」「シャッフル」ボタン、検索ボックス＋クリア `✕` ボタン、ソート基準 `ComboBox`＋昇降順トグルボタン、楽曲総数・総再生時間サマリー）。
  - 行1 (`Auto`, 高さ `30px`): 固定カラムヘッダー行（`#`, `★`, `タイトル`, `アーティスト`, `アルバム`, `ジャンル`, `時間`）。縦スクロールバーの表示幅に連動して右パディングを自動同期し、行データとの列ズレを防止する。
  - 行2 (`*`): UI仮想化対応の楽曲リスト（`ListBox`）および0件時の Empty State オーバーレイ（「表示する楽曲がありません」）。
- **サイズ要件**: ワークスペースの幅・高さに100%追従。

---

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 画面背景 |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | ツールバーおよび列ヘッダー背景 |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | セカンダリボタン・検索ボックス・ソートComboBox背景 |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | 行選択・ホバー背景 |
| `BorderBrush` | <span style="background-color:#343E4E;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #343E4E</span> | `#343E4E` | ツールバー・列ヘッダー下部境界線、入力枠線 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | プライマリボタン背景、左端3pxアクセントバー、再生中曲名強調 |
| `AlwaysNeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | アルバムアート暗幕上の再生中 `▶` アイコン（両テーマ不変） |
| `InverseTextForegroundBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | プライマリボタン（「▶ すべて再生」）文字色 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主要テキスト（曲名） |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | アーティスト名、アルバム名 |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | 列ヘッダー、連番、ジャンル、再生時間、プレースホルダー |

- **タイポグラフィ**: 曲名: `13pt`（再生中は `SemiBold`＋`NeonCyanBrush`）、アーティスト・アルバム: `12pt`、列ヘッダー・ジャンル・時間: `11pt`。
- **行高**: `36px`。

---

## 5. 構成要素と共通コントロール

1. **ツールバー領域**:
   - `ToolbarPrimaryButtonStyle`: 「▶ すべて再生」（フィルタ適用後の表示リスト先頭から連続再生）。
   - `ToolbarSecondaryButtonStyle`: 「シャッフル」（表示リストをシャッフルして即時再生し、`AlbumQueue` と同期）。
   - 検索ボックス: 曲名・アーティスト・アルバム・ジャンル（`Genre`）の4項目横断インクリメンタル検索（200msデバウンス、クリア `✕` 押下時は即時解除）。
   - ソートコントロール: `AllSongsSortOption`（タイトル / アーティスト / アルバム / 時間 / ジャンル）の `ComboBox`（`NeonComboBoxStyle`）および昇順（`▲`）/ 降順（`▼`）トグルボタン（同一値時はタイブレーカーで順序安定化）。
   - サマリー表示: 合計時間が1時間未満の場合は `全 N 曲 (M 分)`、1時間以上の場合は `全 N 曲 (H.H 時間)`。
2. **リスト列構成 (`AllSongsTrackItem`)**:
   - 左端アクセントバー: 行選択時（`IsSelected`）または再生中（`Track.IsPlaying`）に幅 `3px` の `NeonCyanBrush` 縦バーを表示。
   - サムネイル列: `24×24px`（角丸 `3px`）のアルバムアートサムネイル（`AlbumArtLoader` 非同期読込）＋再生中（`Track.IsPlaying=True`）は半透明黒オーバーレイ上に `AlwaysNeonCyanBrush` の `▶` アイコンを表示。
   - `#`: `AllSongsTrackItem.Index` による1始まりの連番（仮想化リサイクリング時も連番崩れを起こさない事前採番）。
   - `★`: お気に入りトグルボタン（`★` / `☆`）。
   - `タイトル` / `アーティスト` / `アルバム` / `ジャンル` / `時間`（`DurationString`）。

---

## 6. アニメーションとインタラクション

- **ダブルクリック**: 対象楽曲から表示リスト全体をコンテキストとして即時再生（`PlayTrackCommand`）。ただし `e.OriginalSource` を検証し、お気に入りボタン・スクロールバー・アイテム外余白のダブルクリックでは発火させない。
- **シングルクリック**: 行の選択（`SelectedItem` 更新）。
- **右クリック（コンテキストメニュー）**:
  - 「再生」「次に再生」「最後に再生」
  - 「お気に入りに追加 / お気に入りから解除」
  - 「プレイリストに追加...」
  - 「プロパティ」「ファイルの場所を開く」「削除」
- **拡張構想（関連Issue #188, #233）**:
  - アーティスト/アルバム名クリックによる詳細遷移（#188）、複数選択一括操作・再生キュー/サイドバーへのドラッグ＆ドロップ投入（#233）。

---

## 7. パフォーマンス最適化要件

- 大規模ライブラリにおいても滑らかなスクロールを維持するため、親 `ScrollViewer` の内包を禁止し、以下のUI仮想化4属性を適用：
  ```xml
  VirtualizingPanel.IsVirtualizing="True"
  VirtualizingPanel.VirtualizationMode="Recycling"
  VirtualizingPanel.ScrollUnit="Pixel"
  ScrollViewer.CanContentScroll="True"
  ```
- `LibraryViewModel.Tracks.CollectionChanged` の連続発火時は `DispatcherPriority.Background` で1回の再構築へコアレッシングし、$O(N^2)$ のUIフリーズを防止する。
- 検索キーワード入力時は `DispatcherTimer`（200ms）によるデバウンスを適用する。
- 各行に `DropShadowEffect` や `BlurEffect` 等の高負荷エフェクトを使用しない。

---

## 8. 関連Issue・仕様書

- [AllSongsView.xaml](../../../AudioEffector/Presentation/Views/AllSongsView.xaml) / [AllSongsViewModel.cs](../../../AudioEffector/Presentation/ViewModels/AllSongsViewModel.cs)
- [主要オブジェクト仕様: 楽曲 (`Track`)](../../主要オブジェクト/01_楽曲_Track.md)
- [インタラクション標準化ガイドライン](../../主要オブジェクト/インタラクション標準化ガイドライン.md)
- [ユースケース: 楽曲ライブラリ管理](../../ユースケース/02_楽曲ライブラリ管理.md)
- [デザイントークン仕様書](../../デザインシステム/01_デザイントークン仕様書.md)
- [共通コントロールスタイル仕様書](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [パフォーマンス最適化詳細設計書](../../詳細設計/パフォーマンス最適化.md)
