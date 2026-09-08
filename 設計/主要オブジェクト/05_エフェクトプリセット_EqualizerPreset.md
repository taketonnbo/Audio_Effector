# オブジェクト抽出テーブル: エフェクトプリセット (EqualizerPreset)

## 1. 基本情報
- **オブジェクト名**: エフェクトプリセット（イコライザープリセット）
- **英語名**: EqualizerPreset
- **分類**: セカンダリオブジェクト / 設定オブジェクト
- **責務**: 10バンド周波数帯域のゲイン設定群を保持し、ユーザーが好みの音質に切り替え・保存できる実体。

## 2. 現状の実装におけるコレクション / シングル構成

> [!IMPORTANT]
> **現状の実装実態 (As-Is)**:
> - **コレクション表示**: `EqualizerViewModel.Presets`
>   - プリセット選択用ドロップダウン（ComboBox）として一覧表示される。
> - **シングル表示**: `EqualizerView.xaml`
>   - メインコンテンツ領域（`ViewType.Equalizer`）として全画面表示される。
>   - 選択中のプリセットに対応する10バンドの縦型ゲインスライダー（-10dB〜+10dB）、およびボリューム調整スライダーが表示される。

## 3. 属性（プロパティ）定義

| プロパティ名 | 型 | 説明 | 現状の表示箇所 | 実装プロパティ ([EqualizerPreset.cs](file:///c:/Users/tnish/vs_code_git/Audio_Effector/AudioEffector/Domain/Entities/EqualizerPreset.cs)) |
| :--- | :--- | :--- | :---: | :--- |
| **Name** | `string` | プリセット名称（例：Rock, Pop, Flat） | プリセット選択コンボボックス | `EqualizerPreset.Name` |
| **Gains** | `float[]` | 各周波数バンドのゲイン配列 | 10バンドスライダー (`BandViewModel.Gain`) | `EqualizerPreset.Gains` |
| **IsDefault** | `bool` | システム既定プリセットかどうか | 削除ボタンの有効/無効判定等 | `EqualizerPreset.IsDefault` |
| **IsCustom** | `bool` | ユーザーが編集したカスタム設定か | `EqualizerViewModel.IsCustom` | `EqualizerViewModel.IsCustom` |

## 4. 現状のアクション（操作）定義

| アクション名 | 動詞 | 現状のトリガー / 操作 | 影響・結果 | 関連コマンド / 実装 |
| :--- | :--- | :--- | :--- | :--- |
| **プリセット選択** | Apply | プリセットコンボボックスから項目選択 | DSPエンジンへのゲイン即時適用 | `SelectedPreset` セッター |
| **ゲイン微調整** | Adjust | 各バンドの縦型スライダーをドラッグ操作 | 指定周波数のゲインをリアルタイム変更 | `BandViewModel.Gain` |
| **プリセット保存** | Save As | 「プリセット保存」ボタン押下 | カスタムプリセットとして新規保存 | `SavePresetCommand` |
| **プリセット削除** | Delete | 「プリセット削除」ボタン押下 | 選択中のカスタムプリセットを削除 | `DeletePresetCommand` |
| **フラットリセット** | Reset | 「フラット」「リセット」ボタン押下 | 全バンドのゲインを0dBに初期化 | `ResetPresetCommand` |

## 5. OOUI設計上の課題と今後の指針 (To-Be)
- **モードレスなアクセス**: 現在の全画面切り替え表示から、下部プレイヤーのポップアップや右側ユーティリティペインへの統合を検討し、楽曲再生や探索を妨げずにEQを微調整できる設計を目指します。
