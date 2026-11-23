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

正方形デザイン（アルバムアート前面＋下部情報＆シークドック / 220×284px、回転ON時は丸い窓）:
+---------------------------------------+
|                                  [⤢]  | ← ホバー時のみ表示
|        . - - - - - - - - - .          |
|      /     (Album Art)       \        |
|     |   +-------------------+ |       |
|     |   |  [⏮]  [▶/⏸]  [⏭]  | |       | ← ホバー時のみ表示
|     |   +-------------------+ |       |
|      \     (時計回り回転)     /        |
|        ' - - - - - - - - - '          |
+---------------------------------------+
| Midnight City                         | ← 曲名 (Bold / MarqueeTextBlock)
| Hurry Up, We're Dreaming              | ← アルバム名 (Secondary / MarqueeTextBlock)
| 01:23 ━━━━━━━━●──────────── 04:03     | ← 再生シークバー (常時表示・クリック/ドラッグ対応)
+---------------------------------------+
```

> **実装済み拡張に関する注記**: 下部連結の再生キュー展開は Issue #217、最小化時のコンパクトプレイヤー非表示設定は Issue #189、正方形（アルバムアート前面・丸い窓・回転アニメーション）デザイン切替は Issue #261、正方形デザインの下部情報・シークバードック追加は Issue #287 にて実装済みです。

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`WindowStyle="None"`、`AllowsTransparency="True"`、`ResizeMode="NoResize"`。角丸 `8px`・外枠 `1px`（`BorderBrush`、`ClipToBounds="True"`）内に、縦2行構成（行0 `Auto`: 横長／正方形切替ヘッダー `MiniPlayerHeaderHost`、行1 `*`: 下部連結再生キュー領域 `QueueDrawerContainer`、初期 `Visibility="Collapsed"`）で配置する。

- **横長デザイン（`Standard` / 既定）**: 幅 `350px`、通常時高さ `100px`／キュー展開時高さ `350px`。行0の `StandardLayoutGrid`（高さ `98px`）に横3列構成（列0: `78×78px` アルバムアート、列1 `*`: 曲名・アーティスト名、列2 `Auto`: 操作ボタン5個 `⏮` `⏯` `⏭` `☰(キュー開閉)` `⤢` の横1列）を配置。
- **正方形デザイン（`SquareAlbumArt` / Issue #261, #287）**: 幅 `220px`、通常時高さ `284px`／キュー展開時高さ `534px`。行0の `SquareLayoutGrid`（`218×282px`）を縦2行（行0 `218px`: 正方形アルバムアート領域 `SquareArtAreaGrid`、行1 `64px`: 下部情報＆シークドック領域 `SquareInfoDockGrid`）で構成する：
  - **上部アート領域（`218×218px`、`SquareArtAreaGrid`）**:
    - **回転ON時（`MiniPlayerRotateAlbumArt == true`）**: 中央に直径 `184×184px` の丸い窓（`SquareCircularArtContainer`、`EllipseGeometry` 円形クリップ）を配置し、内部のアルバムアート（未設定時は `♫` フォールバック）へ `RotateTransform` を適用。
    - **回転OFF時（`MiniPlayerRotateAlbumArt == false`）**: 正方形前面にアルバムアートを静止表示（`SquareStaticAlbumArtContainer`）。
    - **前面オーバーレイ層（回転しない、`SquareOverlayControlsLayer`）**: 通常時は非表示（`Visibility="Collapsed"`）としてジャケット写真の視認性を維持し、上部アート領域（`SquareArtAreaGrid`）へのマウスオーバー時のみ表示する（下部ドック操作時にジャケットを遮らず、各オーバーレイボタンは `Focusable="False"` としクリック後のフォーカス残留による表示継続も防止）。右上隅に復帰ボタン「`⤢`」（半透明ダーク円形背景）、中央に半透明ダークカプセル（`#B3161920`）で「`⏮`」「`▶` / `⏸`（`AlwaysNeonCyanBrush`）」「`⏭`」を配置。
  - **下部情報＆シークドック（高さ `64px`、`SquareInfoDockGrid`）**: 上部境界線 `1px`（`BorderBrush`）、背景 `WindowBackgroundBrush` 内に縦3行で配置し、ジャケット領域へ重ならないレコードカード風構成とする：
    - 行0: **曲名**（`SquareTrackTitleText`、`MarqueeTextBlock`、`12.5pt / Bold`、`TextForegroundBrush`、未設定時 `No Track`、`SquareInfoDockGrid.IsMouseOver` 連動）。
    - 行1: **アルバム名**（`SquareTrackAlbumText`、`MarqueeTextBlock`、`11pt`、`SecondaryTextForegroundBrush`、`AlbumDisplayFallbackConverter` により `CurrentTrack.Album` → `CurrentTrack.Artist` → `Unknown Album` の順でフォールバック、`SquareInfoDockGrid.IsMouseOver` 連動）。
    - 行2: **再生シークバーと時間表示**（経過時間 `SquareCurrentTimeText` `9.5pt` / `MutedTextForegroundBrush`、再生シークバー `SquareSeekSlider`、`MiniPlayerSeekSliderStyle`、合計時間 `SquareTotalTimeText` `9.5pt` / `MutedTextForegroundBrush`）。
- **行1（下部連結キュー領域）**: サブヘッダー（タイトル「`再生キュー (件数)`」、全クリアボタン、格納ボタン）とコンテンツ領域（空状態メッセージまたはUI仮想化 `ListBox` `MiniQueueListBox`）。各行は再生ボタン・曲名/アーティスト（クリックで再生）・削除ボタンの3列構成。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | ウィンドウ背景 |
| `BorderBrush` | <span style="background-color:#343E4E;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #343E4E</span> | `#343E4E` | ウィンドウ外枠 |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | ボタンホバー背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 曲名・ボタンアイコン |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | アーティスト名・アルバム名 |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | 経過時間・合計時間・補足ラベル |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | シークバー進捗・つまみ・強調アイコン |

曲名（`14pt / Bold`）・アーティスト名（`12pt`）は `MarqueeTextBlock`（情報エリア `MiniPlayerInfoArea` へのホバー連動）。前／次ボタンは `30×30px`、再生切替ボタンは `36×36px`、復帰ボタン（`ToolTip="元に戻す"`）は `24×24px`。

## 5. 構成要素と共通コントロール

アルバムアート（高解像度再生中実ジャケット `MiniPlayerAlbumArtImage`、`DecodePixelWidth = 500`・`Freeze()` 済み、未再生・未設定時は `null` で背面の `♫` フォールバックを維持、各 `<Image>` へ `RenderOptions.BitmapScalingMode="HighQuality"` を適用）、曲名（`CurrentTrack.Title`）、アーティスト名（`CurrentTrack.Artist`）、前の曲「`⏮`」、再生切替「`⏯`」、次の曲「`⏭`」、キュー開閉トグル「`☰`」（`ToggleMiniPlayerQueueCommand`、展開時は `NeonCyanBrush` で強調）、メイン画面へ戻る「`⤢`」、および下部連結キュー領域（`PlayQueue` バインドの `MiniQueueListBox`、`PlayFromQueueCommand` / `RemoveFromQueueCommand` / `ClearQueueCommand` / `CloseMiniPlayerQueueCommand`）で構成する。

## 6. アニメーションとインタラクション

- **起動と座標永続化**: `AppSettings.DisableMiniPlayerOnMinimize` が `false`（既定）の場合、メインウィンドウ最小化時に表示する（`true` の場合は表示せず OS 標準のタスクバー最小化を維持）。`Loaded` 時に `AppSettings.MiniPlayerLeft` / `MiniPlayerTop` から表示座標を復元（仮想スクリーン領域外の場合は画面内へ補正）し、`OnClosed` 時に現在座標を `settings.json` へ保存すると共に `IsMiniPlayerQueueOpen` を `false` にリセットする。
- **下部連結キューの展開・格納アニメーション (Issue #217)**: ヘッダーのキュー開閉ボタンまたは右クリックメニューから `ToggleMiniPlayerQueueCommand` で開閉する。展開時は即座に `QueueDrawerContainer.Visibility = Visibility.Visible` としてウィンドウ高さを `100px → 350px`（`220ms`, `CubicEase EaseOut`）へ延長し、格納時は `350px → 100px`（`200ms`, `CubicEase EaseIn`）のアニメーション完了後に `Visibility = Visibility.Collapsed` へ戻す。
- **移動と復帰**: ウィンドウのドラッグで一体型（連結キュー含む）のまま自由移動し、復帰ボタン「`⤢`」でメイン画面へ復帰する。シークバー（`SquareSeekSlider` およびそのビジュアルツリー子要素）上での左クリック・ドラッグ操作時は `DragMove()` を抑止し、シーク操作との競合を防止する。
- **正方形デザインのシークバー操作 (Issue #287)**: `SquareSeekSlider`（`Minimum="0"`, `Maximum="100"`, `Value="{Binding Progress, Mode=TwoWay}"`, `IsMoveToPointEnabled="True"`, `Focusable="False"`）により、つまみドラッグおよびトラック任意位置クリックで再生位置を変更できる。ドラッグ開始（`Thumb.DragStarted`）で一時停止（`StartDragging` / `PauseForSeek`）、ドラッグ完了（`Thumb.DragCompleted`）で最終位置へシークして再開（`StopDragging` / `ResumeAfterSeek`）し、ドラッグを経由しないトラック直接クリックでも `PreviewMouseLeftButtonUp` にて `Seek()` を呼び出してシーク位置を確実に反映する。また、ドラッグ中にウィンドウが閉じられた場合（`OnClosed`）や `DataContext` 変更時にはドラッグ状態を安全に解除し、`IsDraggingProgress` の残留を防ぐ。
- **最大化ショートカット時の安全復帰 (Issue #72)**: OSの最大化ショートカット（`Win + ↑` / Aero Snap の `WM_WINDOWPOSCHANGING`・`WM_SIZE(SIZE_MAXIMIZED)`・`WM_SYSCOMMAND(SC_MAXIMIZE)`）またはメインウィンドウ側への復元要求を受けた場合は、想定高さ（横長: 通常 `100px`／キュー展開時 `350px`、正方形: 通常 `284px`／キュー展開時 `534px`）を超える拡大を抑止して閉じ、メイン画面を正常な可視・描画状態でシームレスに復帰する。
- **ショートカット・ハードウェアメディアキー操作**: メイン画面と同様に、設定済みカスタムショートカット（`PreviewKeyDown` ベース、設定変更時は `SettingsUpdated` で追従）およびハードウェアメディアキー（`Key.Media*` / Win32 `WM_APPCOMMAND`、二重発火抑止付き）による再生・停止・曲送り操作に対応する（ハードウェアのミュート・音量上げ/下げキーは OS のシステムオーディオへ委譲）。`OnClosed` 時にフックおよびイベント購読を確実に解除する。
- **右クリックメニュー**: 「お気に入りに追加 / 解除」「プレイリストに追加...」「プロパティ表示」「ファイルの場所を開く」「再生キューを表示 / 再生キューを閉じる」（`ToggleMiniPlayerQueueCommand`）「削除」。横長・正方形どちらのデザインでも共通提供する。
- **正方形デザインの回転アニメーションとリアルタイム反映 (Issue #261)**: `AppSettings.MiniPlayerDesignStyle == SquareAlbumArt` かつ `AppSettings.MiniPlayerRotateAlbumArt == true` の場合、`IsPlaying == true` の間、丸い窓内のアルバムアートを時計回り（`0° → 360°`、周期20秒、`RepeatBehavior.Forever`）に連続回転させる。一時停止・停止時はアニメーションを一時停止（`Pause`）し、再開時に元の角度から滑らかに再開（`Resume`）する。回転OFFまたは横長切替・ウィンドウ終了時（`OnClosed`）にはアニメーションクロックを確実に停止・解放する。設定画面からの変更は `UpdateDesignSettings()` により表示中のウィンドウへ即時反映する。
- **最前面表示制御**: `AppSettings.MiniPlayerTopmostBehavior`（「常に最前面に表示」「音楽を再生中のみ最前面に表示」「最前面に表示しない」）に従い、再生状態変更時および設定変更時にリアルタイム反映する。

## 7. パフォーマンス最適化要件

- **非表示時の完全描画抑止**: 下部連結キュー領域（`QueueDrawerContainer`）は初期状態および格納完了時に `Visibility="Collapsed"` / `IsHitTestVisible="False"` とし、横長レイアウト（`StandardLayoutGrid`）と正方形レイアウト（`SquareLayoutGrid`）、および丸い窓コンテナと静止全面コンテナも排他的に `Visibility="Collapsed"` とすることで非アクティブ側のレイアウト計算・描画オーバーヘッドをゼロに抑える。
- **高負荷エフェクトの排除**: 正方形オーバーレイ・回転ターゲット・シークバー（`MiniPlayerSeekSliderStyle`）には `DropShadowEffect` / `BlurEffect` を使用せず、半透明ダーク背景・軽量ベクター描画・`RotateTransform` により軽量に描画する。
- **高解像度アートワークの再利用と世代管理 (Issue #283)**: 一覧サムネイル用の `Track.AlbumArt`（`DecodePixelWidth = 100`）は省メモリ維持のまま変更せず、ミニプレイヤーでは再生中 1 曲分の実ジャケットのみを保持する `MiniPlayerAlbumArtImage`（`DecodePixelWidth = 500`・`Freeze()` 済み、未設定時 `null`）を参照し、`RenderOptions.BitmapScalingMode="HighQuality"` により高DPI・回転時の画質と省メモリを両立する。また、非同期画像読み込みは曲変更ごとの世代番号で保護し、高速曲送りや再生停止時に古い曲の読み込み結果で上書きされる競合を防止する。
- **UI仮想化**: `MiniQueueListBox` には `VirtualizingPanel.IsVirtualizing="True"`、`VirtualizingPanel.VirtualizationMode="Recycling"`、`VirtualizingPanel.ScrollUnit="Pixel"`、`ScrollViewer.CanContentScroll="True"` を適用し、大量のキュー項目でも軽量にスクロール描画する。
- **MarqueeTextBlock**: 長い曲名・アーティスト名・アルバム名の `MarqueeTextBlock` は `MiniPlayerInfoArea`（横長）または `SquareInfoDockGrid`（正方形下部ドック）にマウスが乗っており、かつ文字列が表示幅を超えている場合のみアニメーションを実行する。

## 8. 関連Issue・仕様書

- [MiniPlayerWindow.xaml](../../../AudioEffector/Presentation/Views/MiniPlayerWindow.xaml)
- [ユースケース](../../ユースケース/01_楽曲再生・再生制御.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [再生キューダイアログUI仕様書](../再生キューダイアログ/再生キューダイアログUI仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/MiniPlayerWindow.xaml.cs)
- [PlayerControlViewModel](../../../AudioEffector/Presentation/ViewModels/PlayerControlViewModel.cs)
