# ミニプレイヤー UI仕様書

## 1. 概要 (Overview)

メインウィンドウを最小化した状態で、再生操作と曲情報へのアクセスを提供する。対象は再生中のTrack。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
通常時 (350×100px):
+-------------------------------------------------------------------+
| +-------+  Midnight City            [⏮] [ ⏯ ] [⏭] [☰] [⤢]        |
| | [Art] |  M83                      (28) (34)  (28)(26)(24)       |
| | 78x78 |                                                         |
| +-------+                                                         |
+-------------------------------------------------------------------+

キュー展開時 (350×350px・下部連結延長):
+-------------------------------------------------------------------+
| +-------+  Midnight City            [⏮] [ ⏯ ] [⏭] [☰] [⤢]        |
| | [Art] |  M83                                                    |
| | 78x78 |                                                         |
| +-------+                                                         |
+-------------------------------------------------------------------+
| ☰ 再生キュー (12)                                        [🗑] [▲] |
+-------------------------------------------------------------------+
| [▶] Starboy                                                   [×] |
|     The Weeknd                                                    |
| [▶] Get Lucky (feat. Pharrell)                                [×] |
|     Daft Punk                                                     |
|  ... (UI仮想化 ListBox・ピクセルスクロール)                        |
+-------------------------------------------------------------------+
```

> **構想要素に関する注記**: ミニプレイヤーの正方形（アルバムアート前面・回転アニメーション）デザイン切替は Issue #261 にて計画されています。下部連結の再生キュー展開は Issue #217、最小化時のコンパクトプレイヤー非表示設定は Issue #189 にて実装済みです。

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

幅 `350px`、通常時高さ `100px`／キュー展開時高さ `350px`、`WindowStyle="None"`、`AllowsTransparency="True"`、`ResizeMode="NoResize"`。角丸 `8px`・外枠 `1px`（`BorderBrush`、`ClipToBounds="True"`）内に、縦2行構成（行0: 高さ `98px` のコンパクトミニプレイヤーヘッダー、行1 `*`: 下部連結再生キュー領域 `QueueDrawerContainer`、初期 `Visibility="Collapsed"`）で配置する。

- **行0（コンパクトヘッダー）**: 横3列構成（列0: `78×78px` アルバムアート、列1 `*`: 曲名・アーティスト名、列2 `Auto`: 操作ボタン5個 `⏮` `⏯` `⏭` `☰(キュー開閉)` `⤢` の横1列）。
- **行1（下部連結キュー領域）**: サブヘッダー（タイトル「`再生キュー (件数)`」、全クリアボタン、格納ボタン）とコンテンツ領域（空状態メッセージまたはUI仮想化 `ListBox` `MiniQueueListBox`）。各行は再生ボタン・曲名/アーティスト（クリックで再生）・削除ボタンの3列構成。

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

アルバムアート（`CurrentTrack.AlbumArt`）、曲名（`CurrentTrack.Title`）、アーティスト名（`CurrentTrack.Artist`）、前の曲「`⏮`」、再生切替「`⏯`」、次の曲「`⏭`」、キュー開閉トグル「`☰`」（`ToggleMiniPlayerQueueCommand`、展開時は `NeonCyanBrush` で強調）、メイン画面へ戻る「`⤢`」、および下部連結キュー領域（`PlayQueue` バインドの `MiniQueueListBox`、`PlayFromQueueCommand` / `RemoveFromQueueCommand` / `ClearQueueCommand` / `CloseMiniPlayerQueueCommand`）で構成する。

## 6. アニメーションとインタラクション

- **起動と座標永続化**: `AppSettings.DisableMiniPlayerOnMinimize` が `false`（既定）の場合、メインウィンドウ最小化時に表示する（`true` の場合は表示せず OS 標準のタスクバー最小化を維持）。`Loaded` 時に `AppSettings.MiniPlayerLeft` / `MiniPlayerTop` から表示座標を復元（仮想スクリーン領域外の場合は画面内へ補正）し、`OnClosed` 時に現在座標を `settings.json` へ保存すると共に `IsMiniPlayerQueueOpen` を `false` にリセットする。
- **下部連結キューの展開・格納アニメーション (Issue #217)**: ヘッダーのキュー開閉ボタンまたは右クリックメニューから `ToggleMiniPlayerQueueCommand` で開閉する。展開時は即座に `QueueDrawerContainer.Visibility = Visibility.Visible` としてウィンドウ高さを `100px → 350px`（`220ms`, `CubicEase EaseOut`）へ延長し、格納時は `350px → 100px`（`200ms`, `CubicEase EaseIn`）のアニメーション完了後に `Visibility = Visibility.Collapsed` へ戻す。
- **移動と復帰**: ウィンドウのドラッグで一体型（連結キュー含む）のまま自由移動し、復帰ボタン「`⤢`」でメイン画面へ復帰する。
- **最大化ショートカット時の安全復帰 (Issue #72)**: OSの最大化ショートカット（`Win + ↑` / Aero Snap の `WM_WINDOWPOSCHANGING`・`WM_SIZE(SIZE_MAXIMIZED)`・`WM_SYSCOMMAND(SC_MAXIMIZE)`）またはメインウィンドウ側への復元要求を受けた場合は、想定高さ（通常 `100px`／キュー展開時 `350px`）を超える拡大を抑止して閉じ、メイン画面を正常な可視・描画状態でシームレスに復帰する。
- **ショートカット・ハードウェアメディアキー操作**: メイン画面と同様に、設定済みカスタムショートカット（`PreviewKeyDown` ベース、設定変更時は `SettingsUpdated` で追従）およびハードウェアメディアキー（`Key.Media*` / Win32 `WM_APPCOMMAND`、二重発火抑止付き）による再生・停止・曲送り操作に対応する（ハードウェアのミュート・音量上げ/下げキーは OS のシステムオーディオへ委譲）。`OnClosed` 時にフックおよびイベント購読を確実に解除する。
- **右クリックメニュー**: 「お気に入りに追加 / 解除」「プレイリストに追加...」「プロパティ表示」「ファイルの場所を開く」「再生キューを表示 / 再生キューを閉じる」（`ToggleMiniPlayerQueueCommand`）「削除」。
- **最前面表示制御**: `AppSettings.MiniPlayerTopmostBehavior`（「常に最前面に表示」「音楽を再生中のみ最前面に表示」「最前面に表示しない」）に従い、再生状態変更時および設定変更時にリアルタイム反映する。

## 7. パフォーマンス最適化要件

- **非表示時の完全描画抑止**: 下部連結キュー領域（`QueueDrawerContainer`）は初期状態および格納完了時に `Visibility="Collapsed"` / `IsHitTestVisible="False"` とし、非展開時のレイアウト計算・コンテナ生成オーバーヘッドをゼロに抑える。
- **UI仮想化**: `MiniQueueListBox` には `VirtualizingPanel.IsVirtualizing="True"`、`VirtualizingPanel.VirtualizationMode="Recycling"`、`VirtualizingPanel.ScrollUnit="Pixel"`、`ScrollViewer.CanContentScroll="True"` を適用し、大量のキュー項目でも軽量にスクロール描画する。
- **MarqueeTextBlock**: 長い曲名・アーティスト名の `MarqueeTextBlock` は `MiniPlayerInfoArea` にマウスが乗っており、かつ文字列が表示幅を超えている場合のみアニメーションを実行する。

## 8. 関連Issue・仕様書

- [MiniPlayerWindow.xaml](../../../AudioEffector/Presentation/Views/MiniPlayerWindow.xaml)
- [ユースケース](../../ユースケース/01_楽曲再生・再生制御.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [再生キューダイアログUI仕様書](../再生キューダイアログ/再生キューダイアログUI仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/MiniPlayerWindow.xaml.cs)
- [PlayerControlViewModel](../../../AudioEffector/Presentation/ViewModels/PlayerControlViewModel.cs)
