# オブジェクト抽出テーブル: エフェクトプリセット (EqualizerPreset)

## 1. 基本情報
- **オブジェクト名**: エフェクトプリセット（イコライザープリセット）
- **英語名**: EqualizerPreset
- **分類**: セカンダリオブジェクト / 設定オブジェクト
- **責務**: 10バンド周波数帯域（31Hz〜16kHz）のゲイン設定群を保持し、ユーザーが好みの音質に切り替え・カスタム保存できる実体。

## 2. コレクション / シングル構成

> [!IMPORTANT]
> **コレクション / シングル構成**:
> - **コレクション表示**: `EqualizerViewModel.Presets`
>   - プリセット選択用ドロップダウン（`ComboBox`）として一覧表示される（組み込みプリセット＋`presets.json` に永続化されたカスタムプリセット）。
> - **シングル表示**: `EqualizerView.xaml`
>   - メインコンテンツ領域（`ViewType.Equalizer`）として表示される。
>   - 選択中のプリセットに対応する10バンドの縦型ゲインスライダー（`-10dB`〜`+10dB`）、およびマスターボリューム調整スライダー（`VOL`）が表示される。

## 3. 属性（プロパティ）定義

| プロパティ名 | 型 | 説明 | 表示箇所 | 実装プロパティ ([EqualizerPreset.cs](../../AudioEffector.Domain/Entities/EqualizerPreset.cs)) |
| :--- | :--- | :--- | :---: | :--- |
| **Name** | `string` | プリセット名称（例：Flat, Rock, Pop, ユーザー保存名） | プリセット選択コンボボックス | `EqualizerPreset.Name` |
| **Bands** | `IReadOnlyList<FrequencyBand>` | 10バンドの周波数とGain値オブジェクトのリスト | 内部ドメインモデル | `EqualizerPreset.Bands` |
| **Gains** | `List<float>` | 各周波数バンドのゲイン配列（JSONシリアライズ互換用） | 10バンドスライダー (`BandViewModel.Gain`) | `EqualizerPreset.Gains` |
| **IsCustom** | `bool` | ユーザーが作成・保存したカスタムプリセットかどうか | 削除可否判定等 | `EqualizerPreset.IsCustom` |

## 4. アクション（操作）定義

| アクション名 | 動詞 | トリガー / 操作 | 影響・結果 | 状態 | 関連コマンド / 実装 |
| :--- | :--- | :--- | :--- | :---: | :--- |
| **プリセット選択** | Apply | プリセットコンボボックスから項目選択 | DSPエンジンへの10バンドゲイン即時適用と `AppSettings.LastUsedEffectPreset` への保存 | 実装済み | `SelectedPreset` セッター ([EqualizerViewModel.cs](../../AudioEffector/Presentation/ViewModels/EqualizerViewModel.cs)) |
| **ゲイン微調整** | Adjust | 各バンドの縦型スライダー操作、または `＋` / `－` ボタン（`±0.05`）押下 | 指定周波数のゲインをリアルタイム変更 | 実装済み | `BandViewModel.Gain`, `IncreaseGainCommand`, `DecreaseGainCommand` |
| **プリセット保存** | Save | `"SAVE"` ボタン押下 | `InputBox`（既定名 `"User Preset MM-dd HH:mm"`）で名称を入力し `presets.json` へ保存（同名カスタムは上書き） | 実装済み | `SavePresetCommand` |
| **プリセット削除** | Delete | `"DELETE"` ボタン押下 | 組み込みプリセットは削除不可警告を表示、カスタムプリセットは確認 `MessageBox` のうえ削除 | 実装済み | `DeletePresetCommand` |
| **フラットリセット** | Reset | `"RESET"` ボタン押下 | 全バンドのゲインを `0dB` に初期化 | 実装済み | `ResetCommand` |

## 5. 拡張設計指針
- **モードレスなイコライザーアクセス**: メイン画面の切り替えなしで再生中に素早くプリセットやゲインを調整できるポップアップ/ドロワー導線の検討。
