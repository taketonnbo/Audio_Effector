# 右側タブ開閉機能 UI仕様書

## 1. 概要 (Overview)

右側の再生情報ペインを開閉し、本文の表示領域と再生コントロールの配置を切り替える。対象は再生中のTrackとAlbum。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
【開状態: IsRightPanelOpen=True (右パネル展開中)】
+----+-----------------------------------------------------+--+------------------------------------+
|    | (上部プレイヤーは高さ0pxへ格納)                     |  | [Art 160] Midnight City            |
| [=]|                                                     |[<| M83 - 01:23 ========O===== 04:03   |
|    | 中央ワークスペース (ライブラリ一覧等)               |  |   [🔀]   [|<]   [ ▶ ]   [>|]   [🔁] |
| [♪]|                                                     |  | 🔊 =======O=======  [★] [📋+] [≡]  |
|    | 幅: 1* (残りのスペース)                             |  |                                    |
| [O]|                                                     |  | 幅: 340px (または 1*)              |
+----+-----------------------------------------------------+--+------------------------------------+
                                                             ^ 開閉ハンドル [<]

【閉状態: IsRightPanelOpen=False (右パネル格納・ワークスペース全幅)】
+----+-----------------------------------------------------------------------------------------+--+
|    | [Play/Pause] [Prev] [Next]  01:23 ======O============= 03:45   Vol: =====O===   [≡ キュー]|  |
|    +-----------------------------------------------------------------------------------------+  |
| [=]|                                                                                         |  |
|    | 中央ワークスペース (全幅表示: 画面を広く使って楽曲・アルバムをブラウズ可能)             |[>|
| [♪]|                                                                                         |  |
|    | 幅: 1* (画面いっぱいに拡張)                                                             |  |
| [O]|                                                                                         |  |
+----+-----------------------------------------------------------------------------------------+--+
                                                                                                 ^ 開閉ハンドル [>]
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

中央Gridの左列はMinWidth=200で `1*`、中間列は開閉ボタンを収める `Auto`、右列は開時 `1*`／閉時 `0*`。ボタンは16×48。画面幅別のモード切替やGridSplitterは使用しない。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | ペイン背景 |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | 入力・カード背景 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 操作アクセント |

ハンドル角丸8、枠線1、シアンの矢印。ペイン背景はPanelBackgroundBrush。

## 5. 構成要素と共通コントロール

ToggleButtonのIsCheckedが `IsRightPanelOpen` に連動する。開閉に合わせて矢印方向を変える。右側には再生情報と拡張領域を置くが、EQ・歌詞・転送などを選ぶTabControlは構成しない。行選択の幅3pxバーは対象外。

## 6. アニメーションとインタラクション

クリックで右列のGridLengthを0*↔1*へ600ms・CubicEase/EaseInOutで変更する。同時に上部プレイヤーを0↔60へ切り替える。右パネルを閉じてもキューや再生データは保持する。

## 7. パフォーマンス最適化要件

右列は幅0で格納しClipToBoundsで切り取る。列自体にVisibility=Collapsedの連動はない。共通の非表示パネル要件ではアニメーション完了後のCollapsedが基準となるが、この画面の描画は幅変更の構成に従う。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [MainWindow.xaml](../../../AudioEffector/Presentation/Views/MainWindow.xaml)
- [ユースケース](../../ユースケース/01_楽曲再生・再生制御.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/MainWindow.xaml.cs)
- [MainViewModel](../../../AudioEffector/Presentation/ViewModels/MainViewModel.cs)
- 関連Issue（設計経緯）: [#107](https://github.com/taketonnbo/Audio_Effector/issues/107)、[#116](https://github.com/taketonnbo/Audio_Effector/issues/116)
