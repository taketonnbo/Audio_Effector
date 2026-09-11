# 再生キューダイアログ UI仕様書

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

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

タイトル「再生リスト」、400×500。見出し `Auto` とスクロール領域 `*` の2行。各曲は左40、曲情報 `*`、時間 `Auto` の3列。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 画面背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主テキスト |
| `AlwaysNeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | テーマ共通アクセント |

見出し18/Bold、アート30×30、曲名14/SemiBold、アーティスト・時間12。

## 5. 構成要素と共通コントロール

「再生キュー (Play Queue)」と `PlayQueue` の行を表示する。再生中フラグとアプリの再生状態でアート上に再生／一時停止アイコンを重ねる。行はButtonで、左端3px選択バーは描画しない。

## 6. アニメーションとインタラクション

行クリックは `PlayFromQueueCommand`。右クリックから再生／停止、キューから削除、お気に入り、プレイリスト追加、プロパティ、ファイル場所。閉じる操作はウィンドウ枠で行う。メインのスライドパネルとは別ウィンドウで、タブを置かない。

## 7. パフォーマンス最適化要件

ScrollViewer内のItemsControlで全件描画する構成。UI仮想化の4条件は付いていない。多数の曲を扱う場合の評価ではこの点を含める。アートはAlbumArtLoaderで取得する。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [PlayQueueDialog.xaml](../../../AudioEffector/Presentation/Views/PlayQueueDialog.xaml)
- [ユースケース](../../ユースケース/01_楽曲再生・再生制御.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/PlayQueueDialog.xaml.cs)
- [PlayerControlViewModel](../../../AudioEffector/Presentation/ViewModels/PlayerControlViewModel.cs)
