# お気に入り UI仕様書

## 1. 概要 (Overview)

お気に入りとして登録したTrackをまとめて再生・管理する。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+--------------------------------------------------------------------------------------------------------------------+
| +--------+  ★ お気に入り (Favorites)                                                                               |
| |  [★]   |  84 曲 • 5 時間 42 分 • お気に入りに登録された楽曲                                                      |
| | 100px  |                                                                                                         |
| +--------+  [▶ すべて再生]  [🔀 シャッフル]    [🔍 お気に入りを検索...                     ]                       |
+--------------------------------------------------------------------------------------------------------------------+
|    | #  | ★ | タイトル                     | アーティスト          | アルバム              | 時間  | 操作          |
|----+----+---+------------------------------+-----------------------+-----------------------+-------+---------------|
| || | 1  | ★ | Midnight City                | M83                   | Hurry Up, We're D...  | 04:03 | [★解除] [⋯]   |
| >  | 2  | ★ | Get Lucky (feat. Pharrell)   | Daft Punk             | Random Access Memo... | 06:09 | [★解除] [⋯]   |
|    | 3  | ★ | The Less I Know The Better   | Tame Impala           | Currents              | 03:36 | [★解除] [⋯]   |
|    | 4  | ★ | Instant Crush                | Daft Punk             | Random Access Memo... | 05:37 | [★解除] [⋯]   |
|    | 5  | ★ | Starboy                      | The Weeknd            | Starboy               | 03:50 | [★解除] [⋯]   |
|    | ...| ★ | ...                          | ...                   | ...                   | ...   | ...           |
+--------------------------------------------------------------------------------------------------------------------+
  ^ 選択行: 左端3pxシアンバー (||)      ^ 再生中 (>)                                     ^ クリックでお気に入り解除
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`CurrentViewType=Favorites` はPlaylistTracksViewを使用する。DataContextはPlaylistViewModel。ヘッダーと楽曲リストの2行、行の配置はプレイリスト内楽曲と共通。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | 入力・カード背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主テキスト |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 操作アクセント |

ヘッダー16/Bold、アート40×40、曲名12、アーティスト10。`IsFavoritesView=True` でプレイリストの4分割サムネイルを隠し、お気に入り用Imageを表示する。

## 5. 構成要素と共通コントロール

見出しは `CurrentPlaylistName`、楽曲は `PlaylistTracks`。リストが空の場合は行を生成しない。お気に入り用ImageのSourceはXAMLに記載されたpack URIをそのまま使用する。選択行の3pxバーはこの行テンプレートに含めない。

## 6. アニメーションとインタラクション

再生・右クリックの曲操作はプレイリスト内楽曲と共通。「お気に入りから解除」または行末の除外操作でお気に入り登録を解除する。ヘッダーXはプレイリスト一覧へ遷移する。

## 7. パフォーマンス最適化要件

楽曲ListBoxはIsVirtualizing=True、Recycling、Pixel、CanContentScroll=True。4分割サムネイルはCollapsed。静的背景のBlurEffectを使用する。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [PlaylistTracksView.xaml](../../../AudioEffector/Presentation/Views/PlaylistTracksView.xaml)
- [ユースケース](../../ユースケース/03_プレイリスト管理.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [プレイリスト内楽曲UI仕様書](../プレイリスト内楽曲/プレイリスト内楽曲UI仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/PlaylistTracksView.xaml.cs)
- [PlaylistViewModel](../../../AudioEffector/Presentation/ViewModels/PlaylistViewModel.cs)
