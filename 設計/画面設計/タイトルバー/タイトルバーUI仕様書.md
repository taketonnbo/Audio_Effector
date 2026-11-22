# タイトルバー UI仕様書

## 1. 概要 (Overview)

アプリ名とウィンドウ操作を表示する、メインウィンドウ上端の領域。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+--------------------------------------------------------------------------------------------------------------------+
| [♪]                                           AudioEffector                                       [_]  [口]  [X]   |
+--------------------------------------------------------------------------------------------------------------------+
  ^ AppIcon (18x18)                             ^ 中央タイトル表示 / ウィンドウドラッグ領域       ^ 最小化 / 最大化 / 閉じる
                                                                                                    (各46x32px)
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

ルート `Grid` の行0、高さ `32px`。左に `18×18px` のアプリアイコン（左余白 `10px`）、中央に全3列をまたぐ `"AudioEffector"` タイトルテキスト（`Window.Title` は `"Audio Effector"`）、右側に拡張アクション予約用の `TitleBarExtensionArea`（`StackPanel`、`Margin="0,0,12,0"`、`IsHitTestVisibleInChrome="True"`）および最小化・最大化／復元・閉じるの3ボタン（各 `46×32px`）を配置する。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `TitleBarBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | Dark `#161920` / Light `#F5F5F5` | タイトルバー背景 |
| `TitleBarForegroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | Dark `#F0F4F8` / Light `#1E1E1E` | タイトル文字・ボタンアイコン色 |
| `TitleBarBorderBrush` | <span style="background-color:#2A323D;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2A323D</span> | Dark `#2A323D` / Light `#E2E8F0` | ウィンドウ外枠（通常時 1px） |
| `TitleBarButtonHoverBrush` | <span style="background-color:#2A323D;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2A323D</span> | Dark `#2A323D` / Light `#E5E5E5` | 最小化・最大化ボタンホバー |
| `TitleBarButtonPressedBrush` | <span style="background-color:#384454;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #384454</span> | Dark `#384454` / Light `#CCCCCC` | 最小化・最大化ボタン押下 |
| `TitleBarCloseButtonHoverBrush` | <span style="background-color:#E81123;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #E81123</span> | `#E81123` | 閉じるボタンホバー |
| `TitleBarCloseButtonPressedBrush` | <span style="background-color:#B2101E;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #B2101E</span> | Dark `#B2101E` / Light `#C4101E` | 閉じるボタン押下 |

アプリ名 `12pt / SemiBold`。タイトルボタン `46×32px`。通常表示時のルート `WindowRootBorder` は枠線 `1px`（`TitleBarBorderBrush`）、最大化時はDWMはみ出し防止のため枠線 `0px`・`Margin="7"` に切り替える。`WindowChrome` は `CaptionHeight="32"`, `ResizeBorderThickness="6"`。

## 5. 構成要素と共通コントロール

`TitleBarButtonStyle` を基に最小化・最大化・閉じるボタンを構成し、ホバーと押下に専用トークンを使用する。最大化中はアイコンと `ToolTip` が「元に戻す」へ切り替わる。タイトル文字列とアプリアイコンは `IsHitTestVisible="False"` とし、`WindowChrome` のドラッグ移動およびダブルクリック最大化を通す。

## 6. アニメーションとインタラクション

各ボタンの `Click` から `SystemCommands` でウィンドウを操作する。最小化時は `MiniPlayerWindow` への移行が連動し、閉じる操作時は機器へのデータ転送中（`DeviceBrowser.IsTransferring`）であれば中断確認 `MessageBox` を表示する。

## 7. パフォーマンス最適化要件

固定要素のみで構成されるためコレクションのUI仮想化は対象外。動的な `DropShadowEffect` / `BlurEffect` を使わず、`SolidColorBrush` とベクター `Path` のみで軽量に描画する。

## 8. 関連Issue・仕様書

- [MainWindow.xaml](../../../AudioEffector/Presentation/Views/MainWindow.xaml)
- [ユースケース](../../ユースケース/06_設定・カスタマイズ.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/MainWindow.xaml.cs)
- [MainViewModel](../../../AudioEffector/Presentation/ViewModels/MainViewModel.cs)
- 関連Issue（設計経緯）: [#51](https://github.com/taketonnbo/Audio_Effector/issues/51)、[#165](https://github.com/taketonnbo/Audio_Effector/issues/165)
