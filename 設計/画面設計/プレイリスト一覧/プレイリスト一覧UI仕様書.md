# プレイリスト一覧 UI仕様書

## 1. 概要 (Overview)

ユーザーのプレイリストをカードから開き、新規作成・削除する。対象はUserPlaylist。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+--------------------------------------------------------------------------------------------------------------------+
| [+ 新規プレイリスト作成]   [🔍 プレイリストを検索...                        ]                   全 12 プレイリスト |
+--------------------------------------------------------------------------------------------------------------------+
| +---------------+  +---------------+  +---------------+  +---------------+  +---------------+                      |
| | +-----+-----+ |  | +-----+-----+ |  | +-----+-----+ |  | +-----+-----+ |  |               |                      |
| | | A1  | A2  | |  | | B1  | B2  | |  | | C1  | C2  | |  | | D1  | D2  | |  |      [+]      |                      |
| | +-----+-----+ |  | +-----+-----+ |  | +-----+-----+ |  | +-----+-----+ |  |               |                      |
| | | A3  | A4  | |  | | B3  | B4  | |  | | C3  | C4  | |  | | D3  | D4  | |  | 新規作成      |                      |
| | +-----+-----+ |  | +-----+-----+ |  | +-----+-----+ |  | +-----+-----+ |  |               |                      |
| +---------------+  +---------------+  +---------------+  +---------------+  +---------------+                      |
| Driving Beats      Chillout Lounge    Synthwave 80s      Acoustic Favorites (クリックで作成)                       |
| 32 曲 • 2.1 時間   45 曲 • 3.2 時間   28 曲 • 1.9 時間   18 曲 • 1.2 時間                                          |
|                                                                                                                    |
| (カードホバー時: オーバーレイ再生ボタン [▶] およびメニュー [⋯] が表出)                                             |
+--------------------------------------------------------------------------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`CurrentViewType=Playlists`、DataContextは `MainViewModel.Playlist`。行は作成ボタン `Auto` と一覧 `*`。ListBoxのItemsPanelは中央揃えのWrapPanelで、幅に応じてカードを折り返す。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | 入力・カード背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主テキスト |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 操作アクセント |

作成ボタン高30、左右余白10、下余白10。カードは160pxのアート領域に2×2のサムネイルを並べる。カード角丸8、コンテナ余白10、名称14。

## 5. 構成要素と共通コントロール

「+ NEW PLAYLIST」、`UserPlaylists` のカード、カード内の削除「-」を配置。カードのサムネイル順は左上→右上→右下→左下。名称は省略表示。ホバーでカード背景を変更する。

ListBoxItemはContentPresenterのみで、選択行の幅3pxバーを描画しない。共通PrimaryButton等の名前付きStyleではなく各テンプレートとMainWindowの暗黙Buttonを使用する。

## 6. アニメーションとインタラクション

作成ボタンと一覧余白の右クリックから新規作成。カード選択は `ShowPlaylistCommand` で収録曲画面へ遷移する。カード右クリックには再生／シャッフル再生／名前の変更／削除を置く。削除「-」は `DeletePlaylistCommand`。作成時の名前はInputBoxで入力する。

## 7. パフォーマンス最適化要件

ListBox内のWrapPanelは仮想化パネルではない。標準の大量コレクション向けRecycling／Pixel条件はこの構成に適用されない。アートはAlbumArtLoaderで取得する。画面切替は親が担当。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [PlaylistSelectorView.xaml](../../../AudioEffector/Presentation/Views/PlaylistSelectorView.xaml)
- [ユースケース](../../ユースケース/03_プレイリスト管理.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [プレイリスト内楽曲UI仕様書](../プレイリスト内楽曲/プレイリスト内楽曲UI仕様書.md)
- [文字入力ダイアログUI仕様書](../文字入力ダイアログ/文字入力ダイアログUI仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/PlaylistSelectorView.xaml.cs)
- [PlaylistViewModel](../../../AudioEffector/Presentation/ViewModels/PlaylistViewModel.cs)
