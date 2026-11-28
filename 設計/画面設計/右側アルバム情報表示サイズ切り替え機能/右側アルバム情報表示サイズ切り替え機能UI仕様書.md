# 右側アルバム情報表示サイズ切り替え機能 UI仕様書

## 1. 概要 (Overview)

右側の曲情報をコンパクト表示とアルバム全体表示に切り替える。対象はPlaybackListのAlbum／UserPlaylistとTrack。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
【コンパクト表示モード (IsAlbumViewMaximized=False)】
+-----------------------------------------------------------------------------------------+
| [Art 50] Midnight City / M83   [Hi-Res] FLAC 96kHz/24bit                    [🗖 最大化] |
|   [🔀] [|<] [ ▶ ] [>|] [🔁]   01:23 ========O============ 04:03   [+] [★] [≡]           |
+-----------------------------------------------------------------------------------------+
|        歌詞           |        ダッシュボード        |           タグ                   |
| +===================+ |                              |                                  |
+-----------------------------------------------------------------------------------------+
|                                                                                         |
| (下部拡張領域 Grid.Row="1": 3タブバー直下で 歌詞パネル LyricsView [#401/#403]・         |
|  LRC 自動生成 [#98]・ダッシュボード ListeningStatsView [#123]・タグ [#124] を排他表示)  |
|                                                                                         |
+-----------------------------------------------------------------------------------------+

【最大化表示モード (IsAlbumViewMaximized=True / 初期状態)】
+-----------------------------------------------------------------------------------------+
|                                                                             [🗗 縮小]   |
| +-----------------------------------+ +-----------------------------------------------+ |
| |       +-------------------+       | | Playlist: Hurry Up, We're Dreaming  22 Tracks | |
| |       |                   |       | |-----------------------------------------------| |
| |       | [Art 260/330px]   |       | | 01. Intro (feat. Zola Jesus)            05:22 | |
| |       | (7px メタルベゼル)|       | | ▶   Midnight City                       04:03 | |
| |       +-------------------+       | | 03. Reunion                             03:55 | |
| | Hurry Up, We're Dreaming          | | 04. Where the Boats Go                  01:46 | |
| | M83                               | | 05. Wait                                05:43 | |
| | [Hi-Res] FLAC 96kHz/24bit         | | 06. Raconte-Moi Une Histoire            04:04 | |
| | +-------------------------------+ | | ...                                           | |
| | | Midnight City                 | | |                                               | |
| | |                   [+] [★] [≡] | | |                                               | |
| | |  [🔀]  [|<]  [ ▶ ]  [>|]  [🔁]| | |                                               | |
| | |  ==============O============  | | |                                               | |
| | |  01:23                 04:03  | | |                                               | |
| | +-------------------------------+ | +-----------------------------------------------+ |
+-----------------------------------------------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

右側 `Grid` は最大化時（`IsAlbumViewMaximized = True`、初期状態）に `1* / 0*`、コンパクト時（`IsAlbumViewMaximized = False`）に `Auto / 1*` の2行構成。最大化表示は左カラム（アルバムアート・基本情報・Player Podカード）と右カラム（メタリックコンテナ内の収録曲一覧）の2列に分ける。右上端には `28×28px` の拡大/縮小トグルボタン（`ToggleAlbumViewSizeModeCommand`）の 1 つだけを固定配置し（楽曲未再生時は非活性ガード）、コンパクト時の下部拡張領域（`Grid.Row="1"`）上部に 3 タブバー（`歌詞` / `ダッシュボード` / `タグ`）を配置して歌詞パネル（`LyricsView`）・LRC 自動生成パネル（`LrcGeneratorView`）・リスニング統計ダッシュボード（`ListeningStatsView`）・タグパネルを排他表示する。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | ペイン背景 |
| `MetallicCardBackgroundBrush` | <span style="background-color:#202734;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ グラデーション</span> | Dark / Light 両対応 | Player Pod・トラックリスト・アートベゼル背景 |
| `MetallicCardBorderBrush` | <span style="background-color:#384556;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ グラデーション</span> | Dark / Light 両対応 | メタリックカード外枠 |
| `ZebraOddBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #12FFFFFF</span> | Dark `#12FFFFFF` / Light `#0C000000` | トラックリスト奇数行ゼブラ背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主テキスト |
| `NeonCyanBrush` / `AlwaysNeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 再生中曲名・インラインアイコン・シークバー |

アルバム見出し `24pt / Bold`、サブタイトル `13pt`、音質バッジ `9pt`。スペクトラム表示時（`PlayerControl.IsSpectrumVisible = True`）は左カラム幅 `340px`・アート `260×260px`、スペクトラム非表示時は幅 `380px`・アート `330×330px` へ伸縮する。アート枠は外枠角丸 `10px`・内枠角丸 `5px`・ベゼル `Padding="7"`＋`AlbumArtDropShadowEffect`、Player Pod と右トラックリストコンテナは角丸 `12px`＋`CardDropShadowEffect`（すべて `DarkTheme.xaml` / `LightTheme.xaml` 両テーマ対応）。

## 5. 構成要素と共通コントロール

- **コンパクト表示 (`IsAlbumViewMaximized = False`)**: 上部に `50×50px` サムネイルアート、曲名・アーティスト名（`MarqueeTextBlock`、右上ボタン領域［単一ボタン］と干渉しない右マージン `38px` を確保）、音質バッジ・フォーマット詳細、再生制御5ボタン・シークバー・プレイリスト追加/お気に入り/再生キューボタンを配置し、下部拡張領域（`Grid.Row="1"`）上部に 3 タブバー（`歌詞` / `ダッシュボード` / `タグ`）を置いて `LyricsView`・`LrcGeneratorView`・`ListeningStatsView`・タグパネルを排他表示する。
- **最大化表示 (`IsAlbumViewMaximized = True`)**:
  - 左カラム: 多層メタリックベゼルアート、`PlaybackListName` / `PlaybackListSubtitle`（`MarqueeTextBlock`）、音質バッジ、Player Pod（現在曲名 `MarqueeTextBlock`、クイックアクション3種、`48×48px` 再生ボタンを含む5ボタン、ワイドシークバー、経過/総時間）。
  - 右カラム: ヘッダー（`Playlist: {PlaybackListName}` と `{Count} Tracks`）、および `PlaybackListTracks` 一覧（0件時は空状態「プレイリストは空です」を表示）。奇数行には `ZebraOddBackgroundBrush`、左端カラムには2桁トラック番号（`01.`）と再生中 `▶` / 一時停止 `⏸` アイコンをインライン統合し、曲名には `MarqueeTextBlock` を適用する。

## 6. アニメーションとインタラクション

- `ToggleAlbumViewSizeModeCommand` で `IsAlbumViewMaximized` を反転し、最大化GridとコンパクトGridを `Visibility`（`Visible` / `Collapsed`）で排他切り替えする（楽曲未再生時は `CanToggleAlbumViewSizeMode = False` でコンパクト表示へ開けない）。
- **下部 3 タブ切替およびプレイリスト右クリック導線（[リスニング統計ダッシュボードUI仕様書](../リスニング統計ダッシュボード/リスニング統計ダッシュボードUI仕様書.md)）**: コンパクト表示時の下部拡張領域上部にある 3 タブバー（`歌詞` / `ダッシュボード` / `タグ`、`SelectRightExtensionTabCommand`）で表示パネルを排他切り替えする。また、右カラムの収録曲行右クリックメニューから「ダッシュボードを表示」「タグを表示」「歌詞を編集（LRCエディタ）」を実行すると、再生中の曲に関係なく選択曲を対象としてコンパクト表示＋該当タブへ直接遷移する。
- スペクトラム開閉時の左カラム幅およびアート寸法は `300ms`・`CubicEase` (`EaseInOut`) でアニメーション伸縮する。
- 右カラムの収録曲行クリックで再生開始（再生中トラックの再クリックで一時停止/再開トグル）。右クリックコンテキストメニューから「再生」「次に再生」「最後に再生」「お気に入りに追加 / 解除」「プレイリストに追加...」「ダッシュボードを表示」「タグを表示」「歌詞を編集（LRCエディタ）」「プロパティ」「ファイルの場所を開く」「削除」を実行できる。

## 7. パフォーマンス最適化要件

大小のGridは非選択側をCollapsedにする。最大化側の曲一覧はScrollViewer＋ItemsControlでUI仮想化を行わない。アートにAlbumArtDropShadowEffect、カードに静的な影を使用する。多数行・動的部品の共通要件とは適用範囲を分ける。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [MainWindow.xaml](../../../AudioEffector/Presentation/Views/MainWindow.xaml)
- [ユースケース](../../ユースケース/01_楽曲再生・再生制御.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [歌詞表示UI仕様書](../歌詞表示/歌詞表示UI仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/MainWindow.xaml.cs)
- [MainViewModel](../../../AudioEffector/Presentation/ViewModels/MainViewModel.cs)
- 関連Issue（設計経緯）: [#121](https://github.com/taketonnbo/Audio_Effector/issues/121)、[#122](https://github.com/taketonnbo/Audio_Effector/issues/122)、[#401](https://github.com/taketonnbo/Audio_Effector/issues/401)、[#403](https://github.com/taketonnbo/Audio_Effector/issues/403)、[#406](https://github.com/taketonnbo/Audio_Effector/issues/406)
