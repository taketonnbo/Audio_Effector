# 左サイドバー UI仕様書

## 1. 概要 (Overview)

ワークスペースの表示先と設定画面を選択する。対象はライブラリの分類、プレイリスト、接続機器。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
【通常状態: 幅50px (縮小時)】     【展開状態: 幅200px (ホバー時オーバーレイ)】
+----+                          +------------------------+
| |  |                          | |                      |
| |[♪|                          | | [♪] すべての曲       |  <- 選択中: 左端4pxシアンバー
| |  |                          | |                      |
| [◎]|                          |   [◎] アルバム         |
|    |                          |                        |
| [👤]|                          |   [👤] アーティスト     |
|    |                          |                        |
| [📁]|                          |   [📁] フォルダ         |
|----+                          +------------------------+
| [★]|                          |   [★] お気に入り       |
|    |                          |                        |
| [📋]|                          |   [📋] プレイリスト     |
|    |                          |                        |
| [🕒]|                          |   [🕒] 最近再生した曲   |
|----+                          +------------------------+
| [🎚]|                          |   [🎚] イコライザー     |
|    |                          |                        |
| [📱]|                          |   [📱] 機器転送         |  <- 機器接続時のみ表示
|    |                          |                        |
| ~  |                          | ~                      |
| [⚙]|                          |   [⚙] 設定             |
+----+                          +------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

クライアント領域の左端に `Panel.ZIndex=10` で重ねる。`RootBorder` は通常幅50、展開時200。内側Gridは幅200、上下余白10、行は `Auto / * / Auto`。メイン本文の左余白は50のままなので、展開した150px分は本文に重なる。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | ペイン背景 |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | ホバー・選択背景 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 操作アクセント |

行高40、アイコン20×20、文字14、文字左余白15。画像はマウス進入後の幅200の状態。

## 5. 構成要素と共通コントロール

9個のナビゲーションRadioButtonと最下部の設定Buttonを置く。順序は「すべての曲」「アルバム」「アーティスト」「フォルダ」「お気に入り」「プレイリスト」「最近再生した曲」「イコライザー」「機器転送」。機器転送は `IsDeviceConnected=True` のとき表示。

`SidebarRadioButtonStyle` は通常透明、Hover/Checkedでハイライト背景。Checkedは文字がシアンになり、左端幅4pxのバーを表示する。共通標準の幅3pxに対し、このスタイルの指定値は4px。`SidebarButtonStyle` はホバー時に背景と文字色を変更する。

## 6. アニメーションとインタラクション

MouseEnterで幅50→200、MouseLeaveで200→50、いずれも200ms・QuadraticEase/EaseOut。`SwitchViewCommand` にViewTypeを渡す。設定は `ShowSettingsCommand`。プレイリスト項目をサイドバー内に列挙する領域やD&D先は設けない。

## 7. パフォーマンス最適化要件

固定数のボタンのためUI仮想化の対象外。機器未接続時は該当RadioButtonをCollapsedにする。ルートのClipToBoundsで格納時のラベルを切り取る。DropShadowEffect/BlurEffectは付与しない。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [SidebarControl.xaml](../../../AudioEffector/Presentation/Views/SidebarControl.xaml)
- [ユースケース](../../ユースケース/06_設定・カスタマイズ.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/SidebarControl.xaml.cs)
- [MainViewModel](../../../AudioEffector/Presentation/ViewModels/MainViewModel.cs)
- 関連Issue（設計経緯）: [#65](https://github.com/taketonnbo/Audio_Effector/issues/65)、[#73](https://github.com/taketonnbo/Audio_Effector/issues/73)
