# イコライザー UI仕様書

## 1. 概要 (Overview)

周波数帯域ごとのゲイン、音量、エフェクトプリセットを操作する。対象はEqualizerPreset。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+--------------------------------------------------------------------------------------------------------------------+
|   31      62     125     250     500      1k      2k      4k      8k     16k                  |        VOL         |
|  [＋]    [＋]    [＋]    [＋]    [＋]    [＋]    [＋]    [＋]    [＋]    [＋]                 |        [＋]        |
|   |       |       |       |      [●]      |       |       |       |       |                   |         |          |
|   |       |      [●]      |       |      [●]      |       |       |       |                   |        [●]         |
|   |      [●]      |      [●]      |       |      [●]      |       |       |                   |         |          |
|  [●]      |       |       |       |       |       |      [●]     [●]      |                   |         |          |
|   |       |       |       |       |       |       |       |       |      [●]                  |         |          |
|  [－]    [－]    [－]    [－]    [－]    [－]    [－]    [－]    [－]    [－]                 |        [－]        |
+--------------------------------------------------------------------------------------------------------------------+
|                      [ Rock            ▼ ]   [ SAVE ]   [ RESET ]   [ DELETE ]                                     |
+--------------------------------------------------------------------------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`CurrentViewType = Equalizer`、`DataContext` は `MainViewModel.Equalizer`（`EqualizerViewModel`）。上部 `*` に10バンドの周波数スライダー列（`Bands`）と右側のマスター音量列（`"VOL"`）、下部 `Auto` に中央寄せでプリセット選択 `ComboBox`、`"SAVE"`、`"RESET"`、`"DELETE"` ボタンを配置する。各バンド列は上から周波数ラベル（`31`, `62`, `125`, `250`, `500`, `1k`, `2k`, `4k`, `8k`, `16k`）、`＋` ボタン、縦スライダー、`－` ボタンの順で構成する。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | ボタン・ComboBox背景 |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | ホバー背景・スライダー未充填レール |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | ボタン・ComboBoxテキスト |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | 周波数ラベル・VOL見出し |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | バンドスライダー充填・Thumb |

帯域ラベル `12pt`、縦 `Slider` の最小高さ `250px`、つまみ `10×10px`、レール幅 `4px`。プリセット `ComboBox` は `200×30px`、下部操作 `Button` は `80×30px`、上下の `＋`／`－` ボタンは `30×30px`。

## 5. 構成要素と共通コントロール

- 各バンドの縦スライダー（`NeonVerticalSliderStyle`）の `Gain` 範囲は `-10`〜`+10 dB`。
- 右側のマスター音量スライダー（`NeonVolumeSliderStyle`、充填色 `#008B8B`）の範囲は `0.0`〜`1.0` で、`AppSettings.Volume` と双方向同期・永続化される。
- プリセット `ComboBox` は組み込みプリセットおよび `presets.json` に保存されたカスタムプリセットを表示し、選択中のプリセット名は `AppSettings.LastUsedEffectPreset` に保存・起動時復元される。

## 6. アニメーションとインタラクション

- **ゲイン・音量変更**: スライダー操作または `＋`／`－` ボタン押下（増減幅 `±0.05`）によりリアルタイムでDSPへ反映。
- **保存 (`SAVE`)**: `SavePresetCommand` により `InputBox` ダイアログ（初期値 `"User Preset MM-dd HH:mm"`）を表示し、カスタムプリセットとして `presets.json` に保存する（既存の同名カスタムプリセットがある場合は上書き更新）。
- **リセット (`RESET`)**: `ResetCommand` により全バンドを `0 dB`（Flat）に戻す。
- **削除 (`DELETE`)**: `DeletePresetCommand` を実行。組み込みプリセットが選択されている場合は削除不可の警告 `MessageBox` を表示し、カスタムプリセットの場合は削除確認 `MessageBox` を経て削除・保存する。

## 7. パフォーマンス最適化要件

10バンド＋音量1本の固定要素で構成されるためコレクションのUI仮想化は対象外。スライダーテンプレートに `DropShadowEffect` / `BlurEffect` は使用しない。

## 8. 関連Issue・仕様書

- [EqualizerView.xaml](../../../AudioEffector/Presentation/Views/EqualizerView.xaml)
- [ユースケース](../../ユースケース/04_イコライザー・エフェクト.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/EqualizerView.xaml.cs)
- [EqualizerViewModel](../../../AudioEffector/Presentation/ViewModels/EqualizerViewModel.cs)
