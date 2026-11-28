# 設定 UI仕様書

## 1. 概要 (Overview)

テーマ、起動、オーディオ、再生、ショートカットの設定を変更する。対象はAppSettings。変更は各プロパティのSetterで保存する。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+---------------------------------------------------------------------------------------------------+
| 設定 (Settings)                                                                                   |
+---------------------+-----------------------------------------------------------------------------+
| > [ 一般          ] | 一般                                                                        |
|   [ オーディオデバイス] |-----------------------------------------------------------------------------|
|   [ エフェクト・再生] | テーマ設定:                                                                 |
|   [ ショートカット] |   アプリケーションのテーマ: [ システム設定に合わせる ▼ ]                    |
|   [ データ管理・その他] |                                                                             |
|                     | 起動設定:                                                                   |
|                     |   [✓] Windows 起動時に自動的に起動する                                      |
|                     |   [ ] 起動時に最小化状態で開始する                                          |
|                     |                                                                             |
|                     | ミニプレイヤー設定:                                                         |
|                     |   最前面表示の挙動: [ 音楽を再生中のみ最前面に表示 ▼ ]                      |
|                     |   [ ] 画面最小化時にコンパクトプレイヤーを表示しない                        |
|                     |   ミニプレイヤーのデザイン: [ 横長（標準） ▼ ]                              |
|                     |   [✓] 楽曲再生中にアルバムアートを回転させる                                |
|                     |       ※「正方形（アルバムアート）」デザイン選択時に有効です。               |
+---------------------+-----------------------------------------------------------------------------+
|                                                                                        [ 閉じる ] |
+---------------------------------------------------------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`800×600px`、`WindowStartupLocation="CenterOwner"` で親ウィンドウ中央に表示するモーダルダイアログ。ルートは見出し `Auto`、設定本文 `*`、閉じるボタン行 `Auto` の3行構成。本文は左カテゴリ `ListBox` 幅 `200px`、区切り `20px`、右詳細領域 `*`。右詳細領域全体を単一の `ScrollViewer`（`VerticalScrollBarVisibility="Auto"`, `HorizontalScrollBarVisibility="Disabled"`）でラップし、カテゴリ選択で内部の `StackPanel` を切り替える。カテゴリ切替時はスクロール位置を先頭へリセットする。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | ダイアログ背景 |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | カテゴリリスト・入力・ボタン背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 見出し・設定ラベル |
| `BorderBrush` | <span style="background-color:#343E4E;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #343E4E</span> | `#343E4E` | 境界線 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 選択カテゴリ・スライダーアクセント |

全体見出し `24pt / Bold`、カテゴリ内見出し `20pt`、節見出し `18pt`。テーマ・音声設定 `ComboBox` 幅 `200px`、最前面設定 `ComboBox` 幅 `250px`。閉じるボタン `100×30px`。

## 5. 構成要素と共通コントロール

| カテゴリ（全5件） | 設定項目・既定値・表示仕様 |
| :--- | :--- |
| **一般** | ・アプリケーションのテーマ: `Light` / `Dark` / `System`（既定: `ThemeType.System`）<br>・Windows起動時に自動起動: `AutoStart`（既定: `false`）<br>・起動時に最小化状態で開始: `StartMinimized`（既定: `false`）<br>・ミニプレイヤー最前面表示の挙動: `None`（最前面に表示しない、既定） / `Always`（常に最前面に表示） / `WhenPlaying`（音楽を再生中のみ最前面に表示）<br>・画面最小化時にコンパクトプレイヤーを表示しない: `DisableMiniPlayerOnMinimize`（既定: `false`。有効時は最小化時に `MiniPlayerWindow` を表示せず OS 標準のタスクバー最小化を維持）<br>・ミニプレイヤーのデザイン: `Standard`（横長（標準）、既定） / `SquareAlbumArt`（正方形（アルバムアート））<br>・楽曲再生中にアルバムアートを回転させる: `MiniPlayerRotateAlbumArt`（既定: `true`。「正方形（アルバムアート）」選択時のみ `IsEnabled` 有効） |
| **オーディオデバイス** | ・サンプルレート: `44100`（既定） / `48000` / `88200` / `96000` / `192000` Hz<br>・オーディオバッファサイズ: `50` / `100`（既定） / `200` / `300` / `500` ms |
| **エフェクト・再生** | ・マスター音量: `0.0`〜`1.0`（既定: `1.0`、右横にパーセント `P0` 表示）<br>・ピーク・ノーマライズを有効にする: `EnableNormalize`（既定: `false`） |
| **ショートカット** | `ShortcutInputBox` 7項目（再生/一時停止、停止、次の曲、前の曲、ミュート、音量を上げる、音量を下げる） |
| **データ管理・その他** | ・再スキャン時に更新されたファイルのメタデータを再読み込みする: `DetectMetadataChangesOnRescan`（既定: `false`。有効時は既存ファイルの最終更新日時変更を検知してタグ情報を再解析）<br>・転送時に MP4 を M4A に変換する: `ConvertMp4ToM4a`（既定: `false`。有効時は機器転送時に `.mp4` を `.m4a`（AAC）形式へ自動変換して転送し、一時ファイルを自動クリーンアップ）<br>・転送時に FLAC を MP3 に変換してデータ量を削減する: `ConvertFlacToMp3`（既定: `false`。有効時は機器転送時に `.flac` を `.mp3`（320 kbps・メタデータおよびカバーアート引継ぎ）へ自動変換して転送し、一時ファイルを自動クリーンアップ）（他項目は関連Issue: #44, #45, #46 にて拡張計画） |

保存・キャンセルボタンは設けず、各項目変更時に即時保存して下端の「閉じる」ボタンで終了する（追加設定項目は Issue #190, #223 等にて拡張計画）。

## 6. アニメーションとインタラクション

各入力は `TwoWay` バインディングで `SettingsViewModel` のプロパティSetterへ即時反映され、`settings.json` へ保存されると同時に以下の副作用がリアルタイム実行される：
- **テーマ変更**: `ThemeManager.ApplyTheme()` によりアプリ全体のテーマリソースを即時差し替え。
- **自動起動変更**: `StartupManager.SetAutoStart()` により Windows レジストリ（`Run` キー）を更新。
- **最前面挙動変更**: 表示中の `MiniPlayerWindow` に対して `UpdateTopmostBehavior()` を即時反映。
- **ミニプレイヤーデザイン・回転設定変更 (Issue #261)**: 表示中の `MiniPlayerWindow` に対して `UpdateDesignSettings()` を即時反映し、レイアウト・ウィンドウサイズ・回転アニメーションを切り替える。
- **サンプルレート / バッファサイズ変更**: `IAudioService.UpdateAudioProperties()` へ即時反映。
- **ショートカット変更**: ダイアログを閉じた後に `MainViewModel.SettingsUpdated` イベント経由で `MainWindow` および `MiniPlayerWindow` の `WindowShortcutHandler` へ最新設定を反映。

## 7. パフォーマンス最適化要件

カテゴリは5件の固定リスト。詳細はDataTriggerで選択カテゴリだけVisible、それ以外Collapsed。右側コンテンツ領域全体を単一の `ScrollViewer` でスクロールする構成とし、個別カテゴリ内の二重スクロールは設けない。DropShadowEffect/BlurEffectなし。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [SettingsDialog.xaml](../../../AudioEffector/Presentation/Views/SettingsDialog.xaml)
- [ユースケース](../../ユースケース/06_設定・カスタマイズ.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [ショートカット入力UI仕様書](../ショートカット入力/ショートカット入力UI仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/SettingsDialog.xaml.cs)
- [SettingsViewModel](../../../AudioEffector/Presentation/ViewModels/SettingsViewModel.cs)
