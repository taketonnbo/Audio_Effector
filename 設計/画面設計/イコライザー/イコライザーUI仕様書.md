# イコライザー UI仕様書

## 1. 概要 (Overview)

周波数帯域ごとのゲイン、音量、エフェクトプリセットを操作する。対象はEqualizerPreset。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+--------------------------------------------------------------------------------------------------------------------+
| イコライザー (Graphic Equalizer - 10 Band)                                              [EQ有効: [ON / OFF] ]      |
+--------------------------------------------------------------------------------------------------------------------+
|  +10dB |                                                [●]                                         |  Master Vol  |
|        |                  [●]                                 [●]                                   |  [+]         |
|   +5dB |            [●]               [●]                                                           |  |           |
|        |                                                                      [●]                   |  |           |
|    0dB |--[●]-----------------------------------------------------------------------[●]-------------|  |[●]        |
|        |                                                                                            |  |           |
|   -5dB |                                                                                            |  |           |
|        |                                                                                            |  |           |
|  -10dB |                                                                                            |  [-]         |
|--------+--------------------------------------------------------------------------------------------+--------------|
|  Gain  |   0.0     +4.5    +7.0      +4.0    +10.0     +8.5    +6.0      +2.0    0.0     -2.0   |     80%      |
|  Freq  |  31Hz    62Hz    125Hz     250Hz    500Hz     1kHz    2kHz      4kHz    8kHz    16kHz  |              |
+--------------------------------------------------------------------------------------------------------------------+
| プリセット: [ Rock (Classic)            ▼ ]     [💾 名前を付けて保存]   [↺ フラットにリセット]   [🗑 削除]           |
+--------------------------------------------------------------------------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`CurrentViewType=Equalizer`、DataContextは `MainViewModel.Equalizer`。上部 `*` にBandsと右側音量、下部 `Auto` にプリセット・SAVE・RESET・DELETEを置く。周波数帯域の本数とラベルは `Bands` のデータに従う。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | ホバー・選択背景 |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | 補助表示 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 操作アクセント |

帯域ラベル12、縦Sliderの最小高さ250、つまみ10×10、レール幅4。プリセットComboBoxは200×30、操作Buttonは80×30。音量の＋／－は30×30。

## 5. 構成要素と共通コントロール

`NeonVerticalSliderStyle` のGainは-10～+10 dB。音量は `NeonVolumeSliderStyle` で0～1。プリセット項目はName表示。SAVEは保存、RESETはフラットへ戻す、DELETEはユーザープリセット削除。コレクションの選択はComboBoxで扱い、行の幅3pxバーは対象外。

## 6. アニメーションとインタラクション

Slider変更を即時反映。＋／－は音量変更コマンド。SAVEからInputBoxへプリセット名を入力し保存。RESETはフラットプリセットを選択する。DELETEは組込プリセットを削除しない。任意位置への操作とキーボード増減はWPF Sliderに従う。

## 7. パフォーマンス最適化要件

帯域数が限定されたItemsControlなのでUI仮想化対象外。ビュー自身の縦SliderテンプレートにはDropShadowEffect/BlurEffectなし。設定値変更時の通知と音声処理はEqualizerViewModelが担当。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [EqualizerView.xaml](../../../AudioEffector/Presentation/Views/EqualizerView.xaml)
- [ユースケース](../../ユースケース/04_イコライザー・エフェクト.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/EqualizerView.xaml.cs)
- [EqualizerViewModel](../../../AudioEffector/Presentation/ViewModels/EqualizerViewModel.cs)
