# 再生キューダイアログ UI仕様書（廃止）

> [!WARNING]
> **廃止済み（Issue #217）**: 独立ウィンドウダイアログ `PlayQueueDialog`（`PlayQueueDialog.xaml` / `PlayQueueDialog.xaml.cs`）は Issue #217 にて完全撤廃され、[ミニプレイヤーUI仕様書](../ミニプレイヤー/ミニプレイヤーUI仕様書.md) の下部連結再生キュー領域へ統合されました。本書は履歴参照用として残しています。

## 1. 概要 (Overview)

ミニプレイヤー等から開く独立した再生キュー表示。対象はPlayQueue内のTrack。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+---------------------------------------------------+
| 再生リスト (Play Queue)                      [X]  |
+---------------------------------------------------+
| 予約中の楽曲: 12 曲 (48 分)          [🗑 全クリア] |
+---------------------------------------------------+
| > [Art 30] Midnight City                    04:03 |
|            M83                                    |
|   [Art 30] Starboy                          03:50 |
|            The Weeknd                             |
|   [Art 30] Get Lucky (feat. Pharrell)       06:09 |
|            Daft Punk                              |
|   [Art 30] Instant Crush                    05:37 |
|            Daft Punk                              |
|   [Art 30] Lose Yourself to Dance           05:53 |
|            Daft Punk                              |
|   [Art 30] The Less I Know The Better       03:36 |
|            Tame Impala                            |
|   ...                                             |
+---------------------------------------------------+
|                                 [ 閉じる (Close) ]|
+---------------------------------------------------+
```

> **構想要素・統合履歴に関する注記**: モックアップ内の件数・全クリアバーおよび下部閉じるボタンは構想要素でした。本独立ダイアログ（`PlayQueueDialog`）は Issue #217 にて廃止され、ミニプレイヤー下部の連結パネルへ統合・置き換え済みです。

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

ウィンドウタイトル「再生リスト」、`400×500px`、`WindowStartupLocation="CenterOwner"`。見出し `Auto`（「`再生キュー (Play Queue)`」）とスクロール領域 `*` の2行構成。各曲は左列 `40px`（`30×30px` アート）、曲情報 `*`、再生時間 `Auto` の3列。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 画面・行背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主テキスト |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | アーティスト名 |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | 再生時間 |
| `AlwaysNeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 見出しおよび再生状態アイコン |

見出し `18pt / Bold`、アート `30×30px`、曲名 `14pt / SemiBold`、アーティスト・時間 `12pt`。

## 5. 構成要素と共通コントロール

- 見出し「`再生キュー (Play Queue)`」と `PlayQueue` の楽曲行を表示する。
- アート画像は `Track.AlbumArt` プロパティへ直接バインドし、`IsPlaying` フラグに応じて半透明黒オーバーレイと再生/一時停止アイコンを重ねる。
- `PlayerControlViewModel.ShowQueueDialog()` によって単一インスタンス管理され、既に開いている場合は新規生成せず既存ウィンドウをアクティブ化する。

## 6. アニメーションとインタラクション

- 行クリックで `PlayFromQueueCommand` を実行。
- 右クリックコンテキストメニューから「再生 / 停止」「キューから削除」「お気に入りに追加 / 解除」「プレイリストに追加...」「プロパティ」「ファイルの場所を開く」を実行。
- ウィンドウ内の左ボタンドラッグで `DragMove()` によるウィンドウ移動が可能。

## 7. パフォーマンス最適化要件

`ScrollViewer` 内の `ItemsControl` で描画する構成であり、UI仮想化の4属性は指定していない（メイン画面の `PlayQueueSidePanel` または Issue #217 の連結キュー移行対象）。

## 8. 関連Issue・仕様書

- [ミニプレイヤーUI仕様書](../ミニプレイヤー/ミニプレイヤーUI仕様書.md)（統合先）
- [再生キュースライドパネルUI仕様書](../再生キュースライドパネル/再生キュースライドパネルUI仕様書.md)
- [ユースケース](../../ユースケース/01_楽曲再生・再生制御.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [PlayerControlViewModel](../../../AudioEffector/Presentation/ViewModels/PlayerControlViewModel.cs)
