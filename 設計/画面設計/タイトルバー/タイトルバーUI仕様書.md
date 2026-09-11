# タイトルバー UI仕様書

## 1. 概要 (Overview)

アプリ名とウィンドウ操作を表示する、メインウィンドウ上端の領域。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+--------------------------------------------------------------------------------------------------------------------+
| [♪] Audio Effector                           Now Playing: Song Title - Artist                     [_]  [口]  [X]   |
+--------------------------------------------------------------------------------------------------------------------+
  ^ AppIcon (18x18)                             ^ 中央タイトル表示 / ウィンドウドラッグ領域       ^ 最小化 / 最大化 / 閉じる
                                                                                                    (各46x32px)
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

ルートGridの行0、高32。左に18×18のアプリアイコン（左余白10）、中央に全3列をまたぐAudioEffector、右端に最小化・最大化／復元・閉じるの順で3ボタンを配置する。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `TitleBarBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | タイトルバー背景 |
| `TitleBarButtonHoverBrush` | <span style="background-color:#2A323D;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2A323D</span> | `#2A323D` | タイトルボタンホバー |
| `TitleBarCloseButtonHoverBrush` | <span style="background-color:#E81123;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #E81123</span> | `#E81123` | 閉じるボタンホバー |

アプリ名12/SemiBold。タイトルボタン46×32。通常のルート枠線1、最大化時は枠線0と余白7。WindowChromeはCaptionHeight=32、ResizeBorderThickness=6。

## 5. 構成要素と共通コントロール

`TitleBarButtonStyle` を基に最小化・最大化・閉じるを派生する。ホバーと押下に専用トークンを使用。最大化中はアイコンとToolTipが「元に戻す」へ変わる。タイトルとアプリアイコンはヒットテストを無効化しドラッグを通す。行選択バーは対象外。

## 6. アニメーションとインタラクション

各ボタンのClickからSystemCommandsでウィンドウを操作する。タイトル部分の移動・最大化はWindowChromeの標準操作に従う。最小化はミニプレイヤー表示につながる。

## 7. パフォーマンス最適化要件

固定要素のみ。動的DropShadowEffect／BlurEffectを使わず、線とPathでアイコンを描く。UI仮想化対象なし。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [MainWindow.xaml](../../../AudioEffector/Presentation/Views/MainWindow.xaml)
- [ユースケース](../../ユースケース/06_設定・カスタマイズ.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/MainWindow.xaml.cs)
- [MainViewModel](../../../AudioEffector/Presentation/ViewModels/MainViewModel.cs)
- 関連Issue（設計経緯）: [#51](https://github.com/taketonnbo/Audio_Effector/issues/51)、[#165](https://github.com/taketonnbo/Audio_Effector/issues/165)
