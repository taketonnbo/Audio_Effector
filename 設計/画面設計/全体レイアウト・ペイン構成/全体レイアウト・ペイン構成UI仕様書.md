# 全体レイアウト・ペイン構成 UI仕様書

## 1. 概要 (Overview)

ライブラリの画面切替、楽曲再生、右側の再生情報、再生キューを統合するメインウィンドウ。主要対象は Track、Album、UserPlaylist、Device。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+--------------------------------------------------------------------------------------------------------------------+
| [Icon] Audio Effector                           Now Playing: Song Title - Artist                     [_] [口] [X]  | <- タイトルバー (32px)
+----+---------------------------------------------------------------------------------+-----------------------------+
|    | [Play/Pause] [Prev] [Next]  01:23 ======O============= 03:45   Vol: =====O===   |                             | <- 上部プレイヤー (60px)
|    +---------------------------------------------------------------------------------+-----------------------------+
| [=]| [検索: 楽曲・アルバム・アーティスト...                     ]                    | [右側情報パネル] (340px)    |
|    |                                                                                 | +-------------------------+ |
| [M]| +-----------------------------------------------------------------------------+ | |                         | |
|    | | #  | ★ | タイトル             | アーティスト      | アルバム            | 時間  | | |    [ アルバムアート ]    | |
| [A]| |----+---+----------------------+-------------------+---------------------+-------| | |                         | |
|    | | 1  | ★ | Track Title 01       | Artist Name       | Album Title         | 03:45 | | +-------------------------+ |
| [P]| |>2  | ☆ | Current Playing Song | Artist Name       | Album Title         | 04:12 | | Track: Current Playing    |
|    | | 3  | ☆ | Another Track        | Various Artists   | Compilation Vol.1   | 02:58 | | Artist: Artist Name       |
| [E]| | 4  | ★ | Favorite Melody      | Solo Pianist      | Piano Works         | 05:20 | | Album: Album Title (2024) |
|    | | 5  | ☆ | Acoustic Evening     | Guitar Duo        | Unplugged Sessions  | 03:33 | | [Lyrics / Tag Info]       |
| [D]| +-----------------------------------------------------------------------------+ | +-------------------------+ |
|    |                                                                                 | [<<] スライドキュー (360px) |
| [S]| 中央ワークスペース (ContentControl: 10画面切替)                                 | (オーバーレイ開閉)          |
+----+---------------------------------------------------------------------------------+-----------------------------+
| [▲] スペクトラムアナライザ (Toggle: 22px / 展開時: 140px) [ ▂▃▅▆▇█▇▆▅▃  ▂▃▅▆▇█▇▆▅▃ ]                               |
+--------------------------------------------------------------------------------------------------------------------+
  ^ 左サイドバー (50px)                                                      ^ 開閉ハンドル (< / >)
```

> **構想要素に関する注記**: モックアップ内のタイトルバー再生曲表示、上部プレイヤー内音量スライダー、右パネル内歌詞/タグ表示は拡張構想（関連Issue: #121, #233）です。`MainWindow.xaml` ではタイトルバー中央は `"AudioEffector"` 固定、上部プレイヤー右端はミュートボタンのみ、右パネルは `1* : 1*` 分割のアルバム情報・Player Pod・収録曲リスト構成となります。

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`Window` の初期サイズは `1000×650` DIP。ルートはタイトルバー `Auto`（高さ `32px`）とクライアント領域 `*` の2行。左サイドバーは幅 `50px`（ホバー展開時 `200px`）でクライアント左端に `Panel.ZIndex="10"` で重ねる。本文は左余白 `50px` を確保し、上部プレイヤー `Auto`（通常 `60px` / 右パネル開時 `0px`）、中央 `*`、スペクトラム `Auto`（ハンドル `22px`＋展開時 `140px`）の3行。

中央は左ワークスペース `1*`（`MinWidth="200"`）、開閉ハンドル `Auto`、右パネル `1* / 0*` の3列（初期値は `IsRightPanelOpen = false` の閉状態、`IsAlbumViewMaximized = true` の最大化モード）。右端には幅 `360px` の再生キュー用ホスト（`Panel.ZIndex="30"`）が重なり、スライド移動距離 `380px` で開閉する。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 画面背景 |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | ペイン背景 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 操作アクセント |

上部プレイヤーの格納条件は `IsRightPanelOpen = True` で、右側パネル内の最大化/コンパクト表示モードには依存しない。

## 5. 構成要素と共通コントロール

| 領域 | 表示条件・構成 |
| :--- | :--- |
| ワークスペース | `CurrentViewType`（初期値: `ViewType.Albums`）の10値を `ContentControl.ContentTemplate` の `DataTrigger` で切り替える |
| 上部プレイヤー | 通常高 `60px`。右パネルの開状態（`IsRightPanelOpen = True`）に連動して高 `0px` へ格納 |
| 右側情報パネル | `IsRightPanelOpen`（初期 `false`）で開閉、`IsAlbumViewMaximized`（初期 `true`）で最大化/コンパクトを切替 |
| 読込中オーバーレイ | `IsMainLoadingOverlayVisible = True`（`IsLoading = True` かつ `CurrentViewType != ViewType.Folders`）でワークスペース上に半透明オーバーレイと `"LOADING..."` テキストを表示（フォルダタブ表示中はフォルダタブ内の専用プログレスインジケーターへ表示を委譲） |
| 下部スペクトラム | 高 `22px` の開閉バーと高 `0 / 140px` の64バンド描画領域（`ViewType.DeviceSync` 遷移時は自動で `IsSpectrumVisible = false` に格納） |

## 6. アニメーションとインタラクション

- 左サイドバーの項目選択で中央ワークスペースを即時切り替え。
- 右側パネル開閉ハンドルは `600ms`、スペクトラムアナライザは開 `300ms`・閉 `250ms` でサイズをアニメーション変更する。
- `AppSettings.StartMinimized = true` の場合は起動時に最小化状態（`DisableMiniPlayerOnMinimize == false` 時はミニプレイヤー、`true` 時はタスクバー最小化）で開始する。また、7種のキーボードショートカットおよびハードウェアメディアキーを `PreviewKeyDown` / `WM_APPCOMMAND`（`WindowShortcutHandler`）で処理し、設定ダイアログ終了時の `SettingsUpdated` イベントで最新設定を反映する。
- メインウィンドウ最小化時は、`AppSettings.DisableMiniPlayerOnMinimize == false` の場合に `MiniPlayerWindow` を表示してメインウィンドウを非表示とし、`true` の場合は OS 標準のタスクバー最小化状態を維持する。データ転送中（`DeviceBrowser.IsTransferring`）の終了操作には確認 `MessageBox` を表示する。

## 7. パフォーマンス最適化要件

`ContentTemplate` の切替で中央ビューを交換する。`IsLoading` のオーバーレイはBoolToVisでCollapsed。右列の開閉は幅の変更であり、列全体をCollapsedにする指定はない。キューパネル本体はコードビハインドで閉動作完了後にCollapsedとなる。動的描画と大規模リストの個別条件は各仕様書に記載する。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [MainWindow.xaml](../../../AudioEffector/Presentation/Views/MainWindow.xaml)
- [ユースケース](../../ユースケース/01_楽曲再生・再生制御.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [左サイドバーUI仕様書](../左サイドバー/左サイドバーUI仕様書.md)
- [再生操作UI仕様書](../再生操作/再生操作UI仕様書.md)
- [右側タブ開閉機能UI仕様書](../右側タブ開閉機能/右側タブ開閉機能UI仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/MainWindow.xaml.cs)
- [MainViewModel](../../../AudioEffector/Presentation/ViewModels/MainViewModel.cs)
- 関連Issue（設計経緯）: [#233](https://github.com/taketonnbo/Audio_Effector/issues/233)、[#239](https://github.com/taketonnbo/Audio_Effector/issues/239)、[#240](https://github.com/taketonnbo/Audio_Effector/issues/240)、[#241](https://github.com/taketonnbo/Audio_Effector/issues/241)、[#242](https://github.com/taketonnbo/Audio_Effector/issues/242)
