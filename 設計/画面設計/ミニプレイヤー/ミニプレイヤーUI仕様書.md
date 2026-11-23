# ミニプレイヤー UI仕様書

## 1. 概要 (Overview)

メインウィンドウを最小化した状態で、再生操作と曲情報へのアクセスを提供する。対象は再生中のTrack。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+-------------------------------------------------------------------+
| +--------+  Midnight City            [⏮] [ ⏯ ] [⏭]  [⤢]          |
| |  [Art] |  M83                      (30) (36)  (30) (24)         |
| |  80x80 |                                                        |
| +--------+                                                        |
+-------------------------------------------------------------------+
  ^ アート    ^ 曲名・アーティスト      ^ 前 / 再生切替 / 次 / 復帰ボタン ([⤢])
```

> **構想要素に関する注記**: ミニプレイヤーの正方形（アルバムアート前面・回転アニメーション）デザイン切替は Issue #261、下部への再生キュー連結展開は Issue #217、最小化時のミニプレイヤー非表示設定は Issue #189 にて計画されています。

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`350×100px`、`WindowStyle="None"`、`AllowsTransparency="True"`、`ResizeMode="NoResize"`。角丸 `8px`・外枠 `1px`（`BorderBrush`）内に、横3列構成（列0: `80×80px` アルバムアート、列1 `*`: 曲名・アーティスト名、列2 `Auto`: 操作ボタン4個 `⏮` `⏯` `⏭` `⤢` の横1列）で配置する。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | ウィンドウ背景 |
| `BorderBrush` | <span style="background-color:#343E4E;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #343E4E</span> | `#343E4E` | ウィンドウ外枠 |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | ボタンホバー背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 曲名・ボタンアイコン |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | アーティスト名 |

曲名（`14pt / Bold`）・アーティスト名（`12pt`）は `MarqueeTextBlock`（情報エリア `MiniPlayerInfoArea` へのホバー連動）。前／次ボタンは `30×30px`、再生切替ボタンは `36×36px`、復帰ボタン（`ToolTip="元に戻す"`）は `24×24px`。

## 5. 構成要素と共通コントロール

アルバムアート（`NowPlayingImage`）、曲名（`CurrentTrack.Title`）、アーティスト名（`CurrentTrack.Artist`）、前の曲「`⏮`」、再生切替「`⏯`」、次の曲「`⏭`」、メイン画面へ戻る「`⤢`」で構成する。

## 6. アニメーションとインタラクション

- **起動と座標永続化**: メインウィンドウ最小化時に表示する。`Loaded` 時に `AppSettings.MiniPlayerLeft` / `MiniPlayerTop` から表示座標を復元（仮想スクリーン領域外の場合は画面内へ補正）し、`OnClosed` 時に現在座標を `settings.json` へ保存する。
- **移動と復帰**: ウィンドウのドラッグで自由移動し、復帰ボタン「`⤢`」でメイン画面へ復帰する。
- **最大化ショートカット時の安全復帰 (Issue #72)**: OSの最大化ショートカット（`Win + ↑` / Aero Snap の `WM_WINDOWPOSCHANGING`・`WM_SIZE(SIZE_MAXIMIZED)`・`WM_SYSCOMMAND(SC_MAXIMIZE)`）またはメインウィンドウ側への復元要求を受けた場合は、ミニプレイヤーの拡大を抑止して閉じ、メイン画面を正常な可視・描画状態でシームレスに復帰する。
- **ショートカット・ハードウェアメディアキー操作**: メイン画面と同様に、設定済みカスタムショートカット（`PreviewKeyDown` ベース、設定変更時は `SettingsUpdated` で追従）およびハードウェアメディアキー（`Key.Media*` / Win32 `WM_APPCOMMAND`、二重発火抑止付き）による再生・停止・曲送り・ミュート・音量操作に対応する。`OnClosed` 時にフックおよびイベント購読を確実に解除する。
- **右クリックメニュー**: 「お気に入りに追加 / 解除」「プレイリストに追加...」「プロパティ」「ファイルの場所を開く」「再生リストを表示」（`ShowQueueDialogCommand`）「削除」。
- **最前面表示制御**: `AppSettings.MiniPlayerTopmostBehavior`（「常に最前面に表示」「音楽を再生中のみ最前面に表示」「最前面に表示しない」）に従い、再生状態変更時および設定変更時にリアルタイム反映する。

## 7. パフォーマンス最適化要件

少数の固定要素のみで構成されるためコレクションのUI仮想化は対象外。長い曲名・アーティスト名の `MarqueeTextBlock` は `MiniPlayerInfoArea` にマウスが乗っており、かつ文字列が表示幅を超えている場合のみアニメーションを実行する。

## 8. 関連Issue・仕様書

- [MiniPlayerWindow.xaml](../../../AudioEffector/Presentation/Views/MiniPlayerWindow.xaml)
- [ユースケース](../../ユースケース/01_楽曲再生・再生制御.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [再生キューダイアログUI仕様書](../再生キューダイアログ/再生キューダイアログUI仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/MiniPlayerWindow.xaml.cs)
- [PlayerControlViewModel](../../../AudioEffector/Presentation/ViewModels/PlayerControlViewModel.cs)
