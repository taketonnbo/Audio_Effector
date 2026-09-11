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

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`Window` の初期サイズは1000×650 DIP。ルートはタイトルバー `Auto` とクライアント領域 `*` の2行。タイトルバー高32、サイドバーは幅50でクライアント左端へ重ねる。本文は左余白50を確保し、上部プレイヤー `Auto`、中央 `*`、スペクトラム `Auto` の3行。

中央は左ワークスペース `1*`、開閉ハンドル `Auto`、右パネル `1* / 0*` の3列。固定340pxの右ペインやGridSplitterによる横幅変更は定義しない。右端には幅360のキュー用ホストが重なり、幅360のキューパネルを収める（スライド移動距離380）。 左ワークスペース列のMinWidthは200。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 画面背景 |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | ペイン背景 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 操作アクセント |

XAMLの寸法単位はDIP（96dpiの画像では1 DIP＝1px）。ウィンドウのMinWidth/MinHeight指定、幅別のブレークポイント、自動オーバーレイ化はない。 上部プレイヤーの格納条件は `IsRightPanelOpen=True` で、アルバム表示の大小には依存しない。

## 5. 構成要素と共通コントロール

| 領域 | 表示条件・構成 |
| :--- | :--- |
| ワークスペース | `CurrentViewType` の10値を `ContentControl.ContentTemplate` で切り替える |
| 上部プレイヤー | 通常高60。右パネルの開状態に連動して高0へ縮む |
| 右側情報 | `IsRightPanelOpen` と `IsAlbumViewMaximized` で開閉・大小を切替 |
| 読込中 | `IsLoading=True` でワークスペースに半透明オーバーレイとプログレス表示 |
| 下部 | 高22のスペクトラム開閉バーと高0／140の描画領域 |

選択行のスタイルは各子画面に委譲する。サイドバーの選択バーは4px。

## 6. アニメーションとインタラクション

サイドバーの選択でワークスペースを即時切替。右ハンドルは600ms、スペクトラムは開300ms・閉250msで高さを変更する。最小化するとミニプレイヤーを表示し、メインを隠す。転送中の終了操作は確認ダイアログを表示する。

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
