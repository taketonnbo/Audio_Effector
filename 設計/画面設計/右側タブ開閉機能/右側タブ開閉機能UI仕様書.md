# 右側タブ開閉機能 UI仕様書

## 1. 概要 (Overview)

右側の再生情報ペインを開閉し、本文の表示領域と再生コントロールの配置を切り替える。対象は再生中のTrackとAlbum。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
【開状態: IsRightPanelOpen=True (右パネル展開中)】
+----+-----------------------------------------------------+--+------------------------------------+
|    | (上部プレイヤーは高さ0pxへ格納)                     |  | [大アート+Player Pod] | [収録曲一覧]|
| [=]|                                                     |[>| Midnight City - M83                |
|    | 中央ワークスペース (ライブラリ一覧等)               |  | 01:23 ========O===== 04:03         |
| [♪]|                                                     |  |   [🔀]   [|<]   [ ▶ ]   [>|]   [🔁] |
|    | 幅: 1* (左半分)                                     |  | 幅: 1* (右半分)                    |
+----+-----------------------------------------------------+--+------------------------------------+
                                                             ^ 開閉ハンドル [>] (クリックで閉じる)

【閉状態: IsRightPanelOpen=False (右パネル格納・ワークスペース全幅 / 初期状態)】
+----+-----------------------------------------------------------------------------------------+--+
|    | [Art] Title / Artist   [🔀] [|<] [ ▶ ] [>|] [🔁] [≡]   01:23 ======O======= 03:45   [🔊]|  |
|    +-----------------------------------------------------------------------------------------+  |
| [=]|                                                                                         |  |
|    | 中央ワークスペース (全幅表示: 画面を広く使って楽曲・アルバムをブラウズ可能)             |[<|
| [♪]|                                                                                         |  |
|    | 幅: 1* (画面いっぱいに拡張)                                                             |  |
+----+-----------------------------------------------------------------------------------------+--+
                                                                                                 ^ 開閉ハンドル [<] (クリックで開く)
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

中央 `Grid` の左列は `MinWidth="200"` で `1*`、中間列は幅 `1px` の縦区切り線（閉状態では `Visibility="Collapsed"`）と開閉 `ToggleButton`（`16×48px`）を収める `Auto`、右列は開時 `1*`／閉時 `0*`（初期値は `IsRightPanelOpen = false` の閉状態）。`GridSplitter` による手動ドラッグリサイズは設けない。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | ペイン・ハンドル背景 |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | カード背景 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | ハンドル矢印アクセント |

ハンドル角丸 `8px`、枠線 `1px`、シアンの矢印パス。

## 5. 構成要素と共通コントロール

`ToggleButton.IsChecked` が `IsRightPanelOpen` に双方向バインドされる。
- **閉状態 (`IsChecked = False`)**: 左向き矢印 `<`（`M10,6l-4,4l4,4`）を表示し、縦区切り線を `Collapsed` にする。
- **開状態 (`IsChecked = True`)**: 右向き矢印 `>`（`M6,6l4,4l-4,4`）を表示し、縦区切り線を `Visible` にする。
右側パネル内は `IsAlbumViewMaximized` に応じて最大化2カラムレイアウトまたはコンパクトプレイヤー＋下部拡張予約領域（関連Issue: #121, #123, #124, #130）を表示する。

## 6. アニメーションとインタラクション

クリックで右列の `GridLength` を `0*` ↔ `1*` へ `600ms`・`CubicEase` (`EaseInOut`) でアニメーション変更し、同時に上部コンパクトプレイヤーの高さを `60px` ↔ `0px` へ切り替える。右パネルを閉じても再生状態や `PlaybackListTracks` はそのまま保持される。

## 7. パフォーマンス最適化要件

右列格納時は幅 `0` と `ClipToBounds="True"` で描画領域を閉じ、縦区切り線を `Visibility="Collapsed"` にする。ハンドル自体には高負荷エフェクトを使用しない。

## 8. 関連Issue・仕様書

- [MainWindow.xaml](../../../AudioEffector/Presentation/Views/MainWindow.xaml)
- [ユースケース](../../ユースケース/01_楽曲再生・再生制御.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/MainWindow.xaml.cs)
- [MainViewModel](../../../AudioEffector/Presentation/ViewModels/MainViewModel.cs)
- 関連Issue（設計経緯）: [#107](https://github.com/taketonnbo/Audio_Effector/issues/107)、[#116](https://github.com/taketonnbo/Audio_Effector/issues/116)
