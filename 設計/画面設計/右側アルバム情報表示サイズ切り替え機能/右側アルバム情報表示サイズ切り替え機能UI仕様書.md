# 右側アルバム情報表示サイズ切り替え機能 UI仕様書

## 1. 概要 (Overview)

右側の曲情報をコンパクト表示とアルバム全体表示に切り替える。対象はPlaybackListのAlbum／UserPlaylistとTrack。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
【コンパクト表示モード (IsAlbumViewMaximized=False)】
+------------------------------------+
|                      [🗖 最大化] [X] |
|       +--------------------+       |
|       |                    |       |
|       |  [Art 160x160]     |       |
|       |                    |       |
|       +--------------------+       |
| Midnight City                      |
| M83 — Hurry Up, We're Dreaming     |
| [FLAC 44.1kHz / 16bit]             |
|                                    |
| 01:23 ========O============ 03:45  |
|   [🔀]   [|<]   [ ▶ ]   [>|]   [🔁] |
| 🔊 =======O=======  [★] [📋+] [≡]  |
|                                    |
| [ミニ歌詞 / タグ情報]              |
| Waiting in a car...                |
| Waiting for the right time...      |
+------------------------------------+

【最大化表示モード (IsAlbumViewMaximized=True)】
+-----------------------------------------------------------------------------------------+
|                                                                           [🗗 縮小] [X] |
| +-----------------------------------+ +-----------------------------------------------+ |
| |       +-------------------+       | | アルバム収録曲 (22曲, 73分)                   | |
| |       |                   |       | |-----------------------------------------------| |
| |       |  [Art 260x260]    |       | |  1. Intro (feat. Zola Jesus)            05:22 | |
| |       |                   |       | | >2. Midnight City                       04:03 | |
| |       +-------------------+       | |  3. Reunion                             03:55 | |
| |                                   | |  4. Where the Boats Go                  01:46 | |
| | Hurry Up, We're Dreaming (2011)   | |  5. Wait                                05:43 | |
| | M83 • Electronic / Synthpop       | |  6. Raconte-Moi Une Histoire            04:04 | |
| |                                   | |  7. Train to Sichuan                    01:30 | |
| | [ ▶ アルバム全曲再生 ]            | |  8. Claudia Lewis                       04:31 | |
| | 01:23 ============O======= 04:03  | |  ...                                          | |
| +-----------------------------------+ +-----------------------------------------------+ |
+-----------------------------------------------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

右側Gridは最大化時 `1* / 0*`、コンパクト時 `Auto / 1*` の2行。最大化表示は左のアート・情報・再生操作、右の収録曲一覧に分ける。右上28×28の切替ボタンは表示領域に重ねる。メイン左列は維持し、OSの全画面表示には切り替えない。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | ペイン背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主テキスト |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 操作アクセント |

アルバム見出し24/Bold、サブタイトル13、品質バッジ9。スペクトラム表示時の左カード幅340・アート260×260、スペクトラムを閉じると幅380・アート330×330。アート角丸10・Padding7、再生カード角丸12。カードのグラデーションはDarkThemeのMetallicCardBackgroundBrush／MetallicCardBorderBrushを使用。

## 5. 構成要素と共通コントロール

コンパクト表示は小型アート、曲名・アーティスト・品質、再生操作・シーク・キュー等。最大化表示はPlaybackListName、PlaybackListSubtitle、品質、曲名、48px再生ボタン、収録曲数とPlaybackListTracks。

再生中の行は色と再生／一時停止アイコンで示す。行のホバーはローカルテンプレート、交互背景はZebraOddBackgroundBrushを使う。左端3px選択バーは含めない。

## 6. アニメーションとインタラクション

`ToggleAlbumViewSizeModeCommand` で `IsAlbumViewMaximized` を反転し、双方のGridをVisibilityで切り替える。スペクトラム開閉時のアート寸法は300ms・CubicEase/EaseInOut。曲クリック・右クリックで再生、キュー追加、お気に入り、プレイリスト追加などを操作する。

## 7. パフォーマンス最適化要件

大小のGridは非選択側をCollapsedにする。最大化側の曲一覧はScrollViewer＋ItemsControlでUI仮想化を行わない。アートにAlbumArtDropShadowEffect、カードに静的な影を使用する。多数行・動的部品の共通要件とは適用範囲を分ける。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [MainWindow.xaml](../../../AudioEffector/Presentation/Views/MainWindow.xaml)
- [ユースケース](../../ユースケース/01_楽曲再生・再生制御.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/MainWindow.xaml.cs)
- [MainViewModel](../../../AudioEffector/Presentation/ViewModels/MainViewModel.cs)
- 関連Issue（設計経緯）: [#121](https://github.com/taketonnbo/Audio_Effector/issues/121)、[#122](https://github.com/taketonnbo/Audio_Effector/issues/122)
