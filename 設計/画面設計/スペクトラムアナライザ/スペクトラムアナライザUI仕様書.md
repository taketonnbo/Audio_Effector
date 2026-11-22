# スペクトラムアナライザ UI仕様書

## 1. 概要 (Overview)

再生音声の周波数帯域ごとの強度を下部に表示する。対象はPlayerControl.SpectrumValues。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
【格納時 (高さ22px)】
+--------------------------------------------------------------------------------------------------------------------+
|                                              [▲] SPECTRUM ANALYZER                                                 |
+--------------------------------------------------------------------------------------------------------------------+

【展開時 (ハンドル22px + パネル140px)】
+--------------------------------------------------------------------------------------------------------------------+
|                                              [▼] SPECTRUM ANALYZER                                                 |
| +----------------------------------------------------------------------------------------------------------------+ |
| | 60Hz         250Hz         500Hz         1kHz          4kHz          16kHz                                     | |
| |                                     ▄█                                                                         | |
| |                         ▄█         ███▄                                                                        | |
| |                  ▄█    ████       █████▄       ▄█                                                              | |
| |                 ████  ██████     ████████     ████  ▄█                                                         | |
| |          ▄█    ██████████████   ██████████▄  ██████████  ▄█                                                    | |
| | ▄█      ████  ████████████████▄█████████████████████████████▄      ▄█                                          | |
| |████▄▄▄▄███████████████████████████████████████████████████████▄▄▄▄████                                         | |
| +----------------------------------------------------------------------------------------------------------------+ |
+--------------------------------------------------------------------------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

本文の最下行。上部の開閉ハンドルバーは高さ `22px`（中央寄せで矢印アイコンと `"SPECTRUM ANALYZER"` ラベル）、その下の `SpectrumSlidePanel` は高さ `0px`（格納時）／`140px`（展開時）。内部描画領域は中央固定幅 `768px` で、行0（高さ `22px`）に6分割周波数ラベル（`60Hz`, `250Hz`, `500Hz`, `1kHz`, `4kHz`, `16kHz`）、行1（`*`、高さ `90px`、下余白 `22px`）に下端基準線（高さ `1px`）と64本のバー（`UniformGrid Rows="1"`）を配置する。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | 開閉ハンドルバー背景 |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | ハンドル文字・周波数ラベル |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | ハンドルホバー時の文字・矢印アクセント |

開閉ラベル `"SPECTRUM ANALYZER"` は `9pt / Bold`、周波数ラベルは `10pt / SemiBold`。バーは幅 `6px`、最小高 `3px`、最大高 `80px`（クランプ上限 `78.0`）、角丸 `3,3,1,1`、枠線 `1px`。

## 5. 構成要素と共通コントロール

- `PlayerControl.IsSpectrumVisible` でパネルを開閉し、`IsPlaying = True` のときバーを表示する。
- 背景には現在再生中アートワーク（`SpectrumBackgroundImage` に `BlurEffect Radius="30"`、グレースケール画像 `SpectrumBackgroundImageGray`、および暗色グラデーション `#E60A0D14`）を配置する。
- 各バーの高さは `SpectrumBarItem.Value`、不透明度は `ZeroToTransparent` コンバータで制御し、塗りはアルバムアートのドミナントカラーから動的生成された `SpectrumBarBrush`、枠線は `SpectrumBorderBrush` を使用する。

## 6. アニメーションとインタラクション

- ハンドルクリックで `PlayerControl.ToggleSpectrumCommand` を実行。開く際は `300ms`、閉じる際は `250ms` の高さアニメーションを行う。
- 右側パネルが最大化表示されている場合は、スペクトラムの開閉に連動して右側アルバムアートの寸法（`260px` ⇔ `330px`）および左カラム幅（`340px` ⇔ `380px`）もアニメーション追従する。
- 機器転送画面（`ViewType.DeviceSync`）へ遷移した際は、自動的に `IsSpectrumVisible = false` となって格納される。

## 7. パフォーマンス最適化要件

64本の固定要素であるためコレクションのUI仮想化は対象外。FFT更新は `PlayerControlViewModel` で約30fps（`1000 / 30 ms`）にスロットリングし、`SpectrumBarItem.Value` の変更通知は変化量が `0.5` を超える場合のみ発火する。64本のバー要素には `DropShadowEffect` や `BlurEffect`、`VisualBrush`（水面反射）を一切使用しない。

## 8. 関連Issue・仕様書

- [MainWindow.xaml](../../../AudioEffector/Presentation/Views/MainWindow.xaml)
- [ユースケース](../../ユースケース/01_楽曲再生・再生制御.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/MainWindow.xaml.cs)
- [MainViewModel](../../../AudioEffector/Presentation/ViewModels/MainViewModel.cs)
- 関連Issue（設計経緯）: [#51](https://github.com/taketonnbo/Audio_Effector/issues/51)、[#87](https://github.com/taketonnbo/Audio_Effector/issues/87)
