# ミニプレイヤー UI仕様書

## 1. 概要 (Overview)

メインウィンドウを最小化した状態で、再生操作と曲情報へのアクセスを提供する。対象は再生中のTrack。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+-------------------------------------------------------------------+
| +--------+  Midnight City                                   [⤢]   |
| |  [Art] |  M83 — Hurry Up, We're Dreaming                        |
| |  80x80 |                                                        |
| |        |  [|<]   [  ▶ / ||  ]   [>|]      [≡ キュー] [★]         |
| +--------+  01:23 =================O=============== 04:03         |
+-------------------------------------------------------------------+
  ^ アート    ^ 再生コントロール (前/再生一時停止/次)   ^ 復帰ボタン ([⤢])
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

350×100、WindowStyle=None、AllowsTransparency=True、ResizeMode=NoResize。角丸8の外枠1内に曲情報、操作ボタン、復帰ボタンを置く。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 画面背景 |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | ホバー・選択背景 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 操作アクセント |

曲名・アーティストはMarqueeTextBlock。前／次は30×30、再生切替は36×36。IconButtonStyleは角丸15で、ホバー時にControlBackgroundHighlightBrushを使用する。

## 5. 構成要素と共通コントロール

アート、曲名、アーティスト、前の曲「⏮」、再生切替「⏯」、次の曲「⏭」、通常画面へ戻る「⤢」。再生切替の文字記号は固定。単一の再生対象を扱うため幅3px選択バーは対象外。

## 6. アニメーションとインタラクション

メイン最小化で表示する。ウィンドウをドラッグで移動、復帰ボタンでメイン画面を表示する。右クリックはお気に入り、プレイリスト追加、プロパティ表示、ファイル場所、再生リスト表示、削除。最前面表示は設定の「常に」「表示時のみ」「表示しない」に従う。

## 7. パフォーマンス最適化要件

少数の固定要素。長い曲名はMarqueeTextBlockのホバーとオーバーフロー条件に従う。通常画面復帰後の表示・終了はコードビハインドで管理する。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [MiniPlayerWindow.xaml](../../../AudioEffector/Presentation/Views/MiniPlayerWindow.xaml)
- [ユースケース](../../ユースケース/01_楽曲再生・再生制御.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [再生キューダイアログUI仕様書](../再生キューダイアログ/再生キューダイアログUI仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/MiniPlayerWindow.xaml.cs)
- [PlayerControlViewModel](../../../AudioEffector/Presentation/ViewModels/PlayerControlViewModel.cs)
