# スペクトラムアナライザ UI仕様書

## 1. 概要 (Overview)

再生音声の周波数帯域ごとの強度を下部に表示する。対象はPlayerControl.SpectrumValues。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
【格納時 (高さ22px)】
+--------------------------------------------------------------------------------------------------------------------+
| [▲] SPECTRUM ANALYZER                                                                          [ 64-Band FFT ]     |
+--------------------------------------------------------------------------------------------------------------------+

【展開時 (高さ140px)】
+--------------------------------------------------------------------------------------------------------------------+
| [▼] SPECTRUM ANALYZER                                                                          [ 64-Band FFT ]     |
| +----------------------------------------------------------------------------------------------------------------+ |
| |                                     ▄█                                                                         | |
| |                         ▄█         ███▄                                                                        | |
| |                  ▄█    ████       █████▄       ▄█                                                              | |
| |                 ████  ██████     ████████     ████  ▄█                                                         | |
| |          ▄█    ██████████████   ██████████▄  ██████████  ▄█                                                    | |
| | ▄█      ████  ████████████████▄█████████████████████████████▄      ▄█                                          | |
| |████▄▄▄▄███████████████████████████████████████████████████████▄▄▄▄████                                         | |
| | 60Hz         250Hz         500Hz         1kHz          4kHz          16kHz                                     | |
| +----------------------------------------------------------------------------------------------------------------+ |
+--------------------------------------------------------------------------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

本文の最下行。開閉バーは高22、下のSpectrumSlidePanelは0／140。内部Grid幅768、ヘッダー高22、バーItemsControl幅768・高90、下余白22。64個をUniformGridの1行に均等配置する。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | ペイン背景 |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | 補助表示 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 操作アクセント |

開閉ラベルSPECTRUM ANALYZERは9/Bold、周波数ラベル10/SemiBold。バー幅6、最小高3、最大高80、角丸3、枠線1。ラベルは60Hz／250Hz／500Hz／1kHz／4kHz／16kHz。

## 5. 構成要素と共通コントロール

`PlayerControl.IsSpectrumVisible` でパネルを開閉し、`IsPlaying=True` のときバーを表示する。各バーのValueが高さ、不透明度はZeroToTransparentで制御される。バー色は `SpectrumBarBrush`、枠は `SpectrumBorderBrush` の動的ブラシ。画像は固定した再生中データを使用する。選択行バーは対象外。

## 6. アニメーションとインタラクション

ハンドルクリックで `PlayerControl.ToggleSpectrumCommand`。開300ms、閉250msの高さアニメーション。閉時は開閉バーを残す。アルバム情報を最大化している場合はアートの大きさも追従する。

## 7. パフォーマンス最適化要件

64本の固定個数なのでUI仮想化対象外。更新はPlayerControlViewModelで間引き、値の通知は有意差に限る。バーにDropShadowEffect／BlurEffectは付与しない。停止時はItemsControlをCollapsed。パネル格納は高さ0で行う。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [MainWindow.xaml](../../../AudioEffector/Presentation/Views/MainWindow.xaml)
- [ユースケース](../../ユースケース/01_楽曲再生・再生制御.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/MainWindow.xaml.cs)
- [MainViewModel](../../../AudioEffector/Presentation/ViewModels/MainViewModel.cs)
- 関連Issue（設計経緯）: [#51](https://github.com/taketonnbo/Audio_Effector/issues/51)、[#87](https://github.com/taketonnbo/Audio_Effector/issues/87)
