# スクロールバー UI仕様書

## 1. 概要 (Overview)

一覧や本文のスクロール位置を表示し、つまみ（Thumb）操作で表示範囲を変えるアプリケーション共通のカスタム `ScrollBar` コントロール（`App.xaml` から暗黙スタイルとして全画面へグローバル適用）。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
【通常状態 (Normal)】         【ホバー状態 (Hover)】        【ドラッグ中 (Dragging)】
+------------------------+    +------------------------+    +------------------------+
| コンテンツ領域       | |    | コンテンツ領域      |  |    | コンテンツ領域      |  |
| Track Title 01       | |    | Track Title 01      |  |    | Track Title 01      |  |
| Track Title 02       |█|    | Track Title 02      |██|    | Track Title 02      |██| <- ScrollBarThumbDraggingBrush
| Track Title 03       |█|    | Track Title 03      |██|    | Track Title 03      |██|    (Dark: #00FFFF / Light: #0078D7)
| Track Title 04       |█|    | Track Title 04      |██|    | Track Title 04      |██|
| Track Title 05       | |    | Track Title 05      |  |    | Track Title 05      |  |
| Track Title 06       | |    | Track Title 06      |  |    | Track Title 06      |  |
+------------------------+    +------------------------+    +------------------------+
  ^ 幅: 6px (スリム表示)        ^ 幅: 8px (拡大ハイライト)    ^ 幅: 8px (シアンアクセント)
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`ScrollViewer` 内のトラック（`PART_Track`）に配置する。縦スクロールバーはトラック幅 `8px`、横スクロールバーはトラック高 `8px`。つまみ（`Thumb`）の標準厚は `6px`、ホバー・ドラッグ時は `8px`、移動方向の最小長（`MinHeight` / `MinWidth`）は `24px`、角丸は `3px`。上下・左右の矢印ボタンは置かず、トラックの余白はページ送り用の透明 `RepeatButton` とする。

## 4. デザイントークンの適用

| トークン名 (Brush) | Dark 見本 (Preview) | Dark Hex | Light 見本 (Preview) | Light Hex | 用途 |
| :--- | :---: | :--- | :---: | :--- | :--- |
| `ScrollBarTrackBrush` | `Transparent` | `#00000000` | `Transparent` | `#00000000` | スクロールバートラック背景（完全透明） |
| `ScrollBarThumbBrush` | <span style="background-color:#424C5E;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #424C5E</span> | `#424C5E` | <span style="background-color:#CBD5E1;color:#1E1E1E;padding:2px 8px;border-radius:3px;">■ #CBD5E1</span> | `#CBD5E1` | 通常時スクロールつまみ |
| `ScrollBarThumbHoverBrush` | <span style="background-color:#607088;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #607088</span> | `#607088` | <span style="background-color:#94A3B8;color:#1E1E1E;padding:2px 8px;border-radius:3px;">■ #94A3B8</span> | `#94A3B8` | ホバー時スクロールつまみ |
| `ScrollBarThumbDraggingBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | <span style="background-color:#0078D7;color:#FFFFFF;padding:2px 8px;border-radius:3px;">■ #0078D7</span> | `#0078D7` | ドラッグ操作中スクロールつまみ |

## 5. 構成要素と共通コントロール

`ScrollBarResources.xaml` にて `VerticalScrollBarThumbStyle` / `HorizontalScrollBarThumbStyle` と各 `ControlTemplate`、および `TargetType="{x:Type ScrollBar}"` の暗黙スタイルを定義し、`App.xaml` の `MergedDictionaries` でアプリ全体へ適用する。通常時はトラックに対して中央揃え（`6px`）、`ScrollBar` 全体のホバーまたはドラッグで厚み `8px` に拡張する。

## 6. アニメーションとインタラクション

つまみのドラッグでスクロール位置を変更し、トラック余白のクリックで縦 `PageUp`／`PageDown`、横 `PageLeft`／`PageRight` を実行する。厚みと色は `Trigger` / `DataTrigger` で即時変更する。

## 7. パフォーマンス最適化要件

`SolidColorBrush` と `Border`（`CornerRadius="3"`）のみで描画し、`DropShadowEffect` / `BlurEffect` は使用しない。また、親 `ScrollBar` の外枠幅（`8px`）を固定したまま内部 `Thumb` の太さのみ（`6px` ⇔ `8px`）を切り替えることで、周囲のリストやレイアウトの再計算（Measure / Arrange）を発生させない。

## 8. 関連Issue・仕様書

- [ScrollBarResources.xaml](../../../AudioEffector/Presentation/Themes/ScrollBarResources.xaml)
- [DarkTheme.xaml](../../../AudioEffector/Presentation/Themes/DarkTheme.xaml) / [LightTheme.xaml](../../../AudioEffector/Presentation/Themes/LightTheme.xaml) / [App.xaml](../../../AudioEffector/App.xaml)
- [スクロールバー詳細設計書](../../詳細設計/スクロールバー.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- 関連Issue: [#180](https://github.com/taketonnbo/Audio_Effector/issues/180)
