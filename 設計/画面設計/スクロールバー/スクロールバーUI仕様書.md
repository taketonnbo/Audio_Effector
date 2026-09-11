# スクロールバー UI仕様書

## 1. 概要 (Overview)

一覧や本文のスクロール位置を表示し、つまみ操作で表示範囲を変える共通コントロール。画像はプレイリスト内楽曲の縦スクロールバー適用例。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
【通常状態 (Normal)】         【ホバー状態 (Hover)】        【ドラッグ中 (Dragging)】
+------------------------+    +------------------------+    +------------------------+
| コンテンツ領域       | |    | コンテンツ領域      |  |    | コンテンツ領域      |  |
| Track Title 01       | |    | Track Title 01      |  |    | Track Title 01      |  |
| Track Title 02       |█|    | Track Title 02      |██|    | Track Title 02      |██| <- ネオンシアン
| Track Title 03       |█|    | Track Title 03      |██|    | Track Title 03      |██|    (ドラッグ操作中)
| Track Title 04       |█|    | Track Title 04      |██|    | Track Title 04      |██|
| Track Title 05       | |    | Track Title 05      |  |    | Track Title 05      |  |
| Track Title 06       | |    | Track Title 06      |  |    | Track Title 06      |  |
+------------------------+    +------------------------+    +------------------------+
  ^ 幅: 6px (スリム表示)        ^ 幅: 8px (拡大ハイライト)    ^ 幅: 8px (シアンアクセント)
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

ScrollViewer内のトラックに置く。縦は幅8、横は高8。つまみの標準厚6、ホバー・ドラッグ時8、移動方向の最小長24。矢印ボタンは置かず、トラックの余白はページ送り用RepeatButton。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `ScrollBarThumbColor` | <span style="background-color:#424C5E;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #424C5E</span> | `#424C5E` | スクロールつまみ |
| `ScrollBarThumbHoverColor` | <span style="background-color:#607088;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #607088</span> | `#607088` | スクロールつまみホバー |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 操作アクセント |

角丸3。通常／ホバー／ドラッグの色はScrollBarThumbBrush、ScrollBarThumbHoverBrush、ScrollBarThumbDraggingBrush。参照元Colorは下表に示す。

## 5. 構成要素と共通コントロール

VerticalScrollBarThumbStyle／HorizontalScrollBarThumbStyleと各ControlTemplateを使用。通常はトラックに対して中央揃え、バー全体ホバーで厚み8、つまみホバーで色変更、ドラッグ中はシアン。選択行の幅3pxバーとは別のコントロール。

## 6. アニメーションとインタラクション

つまみドラッグでスクロール位置を変更。余白クリックは縦PageUp／PageDown、横PageLeft／PageRight。厚みと色はTriggerで即時変更し、Storyboardによる補間は行わない。

## 7. パフォーマンス最適化要件

ソリッドブラシとBorderで描画し、DropShadowEffect／BlurEffectは使用しない。一覧の仮想化は利用側のItemsPanelとScrollViewer.CanContentScroll等に依存する。このStyleだけで仮想化を保証しない。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [PlaylistTracksView.xaml](../../../AudioEffector/Presentation/Views/PlaylistTracksView.xaml)
- [ユースケース](../../ユースケース/02_楽曲ライブラリ管理.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [ScrollBarResources.xaml](../../../AudioEffector/Presentation/Themes/ScrollBarResources.xaml)
- [コードビハインド](../../../AudioEffector/Presentation/Views/PlaylistTracksView.xaml.cs)
- [PlaylistViewModel](../../../AudioEffector/Presentation/ViewModels/PlaylistViewModel.cs)
