# 再生操作 UI仕様書

## 1. 概要 (Overview)

現在のTrackの再生、一時停止、曲送り、シーク、音量、お気に入り操作を提供する。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
【上部プレイヤーバー: 高さ60px (右パネル格納時)】
+--------------------------------------------------------------------------------------------------------------------+
| [Art] Current Track Title       [|<]  [ ▶ / || ]  [>|]   01:23 =============O================== 03:45              |
| (40)  Artist Name - Album       [🔀] Shuffle  [🔁] Repeat   🔊 =====O======  [★] [📋+]  [≡ キュー] [🗖 ミニ]          |
+--------------------------------------------------------------------------------------------------------------------+
  ^ アート+曲名+アーティスト        ^ 再生制御・モード切替             ^ シークバー (経過/総時間)  ^ 音量/アクション/展開

【右パネル内プレイヤー (右パネル展開時)】
+------------------------------------+
|          [ 大アルバムアート ]       |
|                                    |
| Current Track Title                |
| Artist Name — Album Name (2024)    |
|                                    |
| 01:23 ========O============ 03:45  |
|                                    |
|   [🔀]   [|<]   [  ▶  ]   [>|]   [🔁]|
|                (48x48)             |
|                                    |
| 🔊 =======O=======  [★] [📋+] [≡]  |
+------------------------------------+
```

> **構想要素に関する注記**: モックアップ内のメイン画面上音量スライダー、および上部バーの `[★]` `[📋+]` `[🗖 ミニ]` ボタンは拡張構想（関連Issue: #233）です。`MainWindow.xaml` では上部プレイヤー右端はミュート切替ボタンのみ（音量スライダーは `EqualizerView` および `SettingsDialog` に配置）、再生キュー開閉ボタンは中央操作列末尾に配置されます。

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

右パネルを閉じた状態（`IsRightPanelOpen = False`）では、本文最上部に高さ `60px` のコンパクトプレイヤーを表示する（左列: 40×40アート＋曲名・アーティスト名、中央列: 再生制御・シークバー・キュー開閉ボタン、右列: ミュートボタン）。右パネルを開くと上部プレイヤーを高さ `0px` へ格納し、右パネル内のPlayer Pod（最大化時）または上部コンパクトプレイヤー（最小化時）を使用する。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | 上部プレイヤーバー背景 |
| `MetallicCardBackgroundBrush` | <span style="background-color:#202734;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2C3545..#161A22</span> | グラデーション | 右パネル Player Pod 背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 曲名テキスト |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | アーティスト名 |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | 経過・総再生時間、非アクティブボタン |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 再生ボタン・シークバー・有効トグル強調 |

上部の曲名 `14pt / SemiBold`、アーティスト `12pt`（いずれも `MarqueeTextBlock` でホバー連動ループスクロールに対応）。シークバーは `NeonSliderStyle`（レール高 `4px`、つまみ `10px`）。最大化右パネルのPlayer Pod再生ボタンは `48×48px`、コンパクト時は `30×30px`。

## 5. 構成要素と共通コントロール

- 曲情報は `CurrentTrack`（`Title`, `Artist`）と `NowPlayingImage` を表示する。
- 操作ボタン群はシャッフル（`IsShuffleEnabled`）、前の曲、再生/一時停止（`IsPlaying` に応じて `▶` / `⏸` アイコン切替）、次の曲、リピート（`IsAlbumRepeat`：全曲リピート ON / OFF の2値トグル）、再生キュー開閉（`TogglePlayQueuePanelCommand`）を備える。
- 右パネル側にはさらにプレイリスト追加（`+`）およびお気に入りトグルボタンを配置し、上部プレイヤーバー右端にはミュート切替ボタン（`ToggleMuteCommand`、ミュート前音量を `_preMuteVolume` に保持）を配置する。

## 6. アニメーションとインタラクション

- `TogglePlayPauseCommand`, `PreviousCommand`, `NextCommand`, `ToggleShuffleCommand`, `ToggleRepeatCommand` を実行する。
- **シーク制御**: `500ms` 周期の進捗タイマー更新中は内部更新フラグ（`_isUpdatingProgressInternally`）で逆シーク発火を防止し、ユーザーのスライダードラッグ中は `_isDraggingProgress` フラグを立てて完了位置へシークする。
- **音量・ショートカット・メディアキー制御**:
  - 音量は `0.0`〜`1.0` の範囲で `AppSettings.Volume` に永続化され、ショートカットキー（`IncreaseVolumeCommand` / `DecreaseVolumeCommand`）により `±5%`（`0.05`）刻みで増減できる。
  - ハードウェアメディアキー（`F4` 再生/一時停止、停止、次/前トラック、ミュート、音量上下）は `Key.Media*` および Win32 `WM_APPCOMMAND` の双方に標準対応し、両方送出される環境では二重発火を抑止する（同一メディアキーを別機能へ明示割り当てしている場合はユーザー設定を優先）。
  - カスタムショートカットは `PreviewKeyDown`（トンネリング）で判定し、ボタン・リスト等へのフォーカス時でも確実に発火させる。編集可能な `TextBox` フォーカス中は修飾キーなし／`Shift` のみの入力・`Space`・矢印キーの発火をスキップして文字入力を保護する（ハードウェアメディアキーはテキスト入力中も有効）。

## 7. パフォーマンス最適化要件

- 固定数のプレイヤーコントロールで構成されるためUI仮想化は対象外。
- 時間・進捗・再生状態は `PlayerControlViewModel`（500msタイマーおよびイベント通知）で更新し、シークバー等の動的更新要素には `DropShadowEffect` / `BlurEffect` を適用しない。

## 8. 関連Issue・仕様書

- [MainWindow.xaml](../../../AudioEffector/Presentation/Views/MainWindow.xaml)
- [ユースケース](../../ユースケース/01_楽曲再生・再生制御.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [右側アルバム情報表示サイズ切り替え機能UI仕様書](../右側アルバム情報表示サイズ切り替え機能/右側アルバム情報表示サイズ切り替え機能UI仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/MainWindow.xaml.cs)
- [MainViewModel](../../../AudioEffector/Presentation/ViewModels/MainViewModel.cs)
