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
| [ソート: アルバム名 ▼]     [▲昇順]   [田 グリッド | ≡ リスト]   [🔍 アルバムを検索...              ]     全 86 作品 |
+--------------------------------------------------------------------------------------------------------------------+
|    | アート | アルバム名                    | アーティスト          | 年     | 収録曲数 | 総再生時間 | 操作      |
|----+--------+-------------------------------+-----------------------+--------+----------+------------+-----------|
| || | [40x40]| Random Access Memories        | Daft Punk             | 2013   | 13 曲    | 74:24      | [▶] [📋+] |
|    | [40x40]| Hurry Up, We're Dreaming      | M83                   | 2011   | 22 曲    | 73:22      | [▶] [📋+] |
|    | [40x40]| Starboy                       | The Weeknd            | 2016   | 18 曲    | 68:40      | [▶] [📋+] |
|    | [40x40]| Currents                      | Tame Impala           | 2015   | 13 曲    | 51:12      | [▶] [📋+] |
+--------------------------------------------------------------------------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`CurrentViewType=Albums`、DataContextは `MainViewModel.Library`。2行Gridの上部が並べ替えのComboBox、昇降順ボタンと表示切替、下部が一覧。`IsListView` では縦ListBox、`IsGridView` ではWrapPanelのカード配置を使用。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 画面背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主テキスト |
| `AlwaysNeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | テーマ共通アクセント |

リストのアート40×40、アルバム名14、アーティスト11。グリッドのExpander幅160、カード余白5。収録曲トレイは140×140、ヘッダー24、曲領域110（22×5行）。

## 5. 構成要素と共通コントロール

一覧は `Albums`、収録曲は `Tracks`。アルバムExpanderで曲を展開し、アート上の再生ボタン、収録曲切替ボタン、情報ボタンを操作する。グリッドのトレイはカード配置を押し広げずに下へ重なる。

一覧のListBoxItemはContentPresenterのみのテンプレートで、標準選択背景と共通の幅3px選択バーは描画しない。転送選択時のCheckBoxは `IsSelected` に連動し、機器に存在するアルバムは選択不可。ホバーは各ボタン・カードのローカルStyleに従う。

## 6. アニメーションとインタラクション

並べ替え項目はArtist／Album、昇降順は `ToggleSortDirectionCommand`。表示切替は `ToggleViewCommand`。アートの再生操作は `PlayAlbumCommand`、曲のボタンは `PlayTrackCommand`。右クリックで再生／次に再生／最後に再生／プレイリスト追加／削除、曲にはお気に入り・プロパティ・ファイル場所も表示する。収録曲トレイは開220ms BackEase、Opacity150ms。閉は150ms CubicEase。三点ボタンはアルバムの情報表示を開く。

## 7. パフォーマンス最適化要件

リスト側は `VirtualizingStackPanel.IsVirtualizing=True` と `Recycling` を指定。ScrollUnitの明示はない。グリッド側はScrollViewer＋ItemsControl＋WrapPanel、収録曲もItemsControlで、UI仮想化を行わない。共通の大規模一覧要件との差を評価する際はこの構成を使用する。非選択表示はBoolToVisでCollapsed。アートのボタン・トレイにDropShadowEffectが存在する。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [LibraryView.xaml](../../../AudioEffector/Presentation/Views/LibraryView.xaml)
- [ユースケース](../../ユースケース/02_楽曲ライブラリ管理.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/LibraryView.xaml.cs)
- [LibraryViewModel](../../../AudioEffector/Presentation/ViewModels/LibraryViewModel.cs)
- 関連Issue（設計経緯）: [#166](https://github.com/taketonnbo/Audio_Effector/issues/166)、[#167](https://github.com/taketonnbo/Audio_Effector/issues/167)、[#168](https://github.com/taketonnbo/Audio_Effector/issues/168)
