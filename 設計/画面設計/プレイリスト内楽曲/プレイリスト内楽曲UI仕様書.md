# プレイリスト内楽曲 UI仕様書

## 1. 概要 (Overview)

選択したプレイリストの楽曲を表示し、再生やプレイリストからの除外を行う。対象はUserPlaylistとTrack。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+--------------------------------------------------------------------------------------------------------------------+
| [< プレイリスト一覧へ]                                                                                             |
| +--------+  Driving Beats                                                                                          |
| | [Cover]|  32 曲 • 2 時間 14 分 • ユーザー作成プレイリスト                                                        |
| | 100px  |                                                                                                         |
| +--------+  [▶ すべて再生]  [🔀 シャッフル]  [✏ 名前を変更]  [🗑 プレイリスト削除]                                 |
+--------------------------------------------------------------------------------------------------------------------+
|    | #  | アート | タイトル                     | アーティスト          | アルバム              | 時間  | 操作      |
|----+----+--------+------------------------------+-----------------------+-----------------------+-------+-----------|
| || | 1  | [40x40]| Midnight City                | M83                   | Hurry Up, We're D...  | 04:03 | [✕] [⋯]   |
|    | 2  | [40x40]| Starboy                      | The Weeknd            | Starboy               | 03:50 | [✕] [⋯]   |
| >  | 3  | [40x40]| Get Lucky (feat. Pharrell)   | Daft Punk             | Random Access Memo... | 06:09 | [✕] [⋯]   |
|    | 4  | [40x40]| Instant Crush                | Daft Punk             | Random Access Memo... | 05:37 | [✕] [⋯]   |
|    | 5  | [40x40]| Lose Yourself to Dance       | Daft Punk             | Random Access Memo... | 05:53 | [✕] [⋯]   |
|    | ...| ...    | ...                          | ...                   | ...                   | ...   | ...       |
+--------------------------------------------------------------------------------------------------------------------+
  ^ ドラッグ＆ドロップで曲順並び替え可能 (≡)                                                 ^ 除外ボタン [✕]
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`CurrentViewType=PlaylistTracks`。ヘッダー `Auto`、楽曲ListBox `*` の2行。背景画像を両行にまたがって敷く。ヘッダーは40×40のサムネイル、名前、右端のX。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | 入力・カード背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主テキスト |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | 補足テキスト |

ヘッダー名16/Bold、曲名12、アーティスト10、時間12。行のアート40×40/角丸4、行の上下余白2。除外ボタンは40×40、文字24。

## 5. 構成要素と共通コントロール

ヘッダーの `CurrentPlaylistName` と `PlaylistTracks` の行を表示。各行は転送用CheckBox、アート、曲名・アーティスト、mm:ssの時間、除外「-」。選択用CheckBoxはMainWindowの `IsSelectionMode` に連動し、転送中は無効。

ListBoxItemはContentPresenterのみ。行に共通の幅3px選択バーは描かない。背景画像にはBlurEffect半径30と半透明Rectangleを重ねる。

## 6. アニメーションとインタラクション

行のButtonは単一クリックで `PlayTrackCommand`。右クリックに再生／停止、次に再生、最後に再生、お気に入り、プレイリスト追加、プロパティ、ファイル場所、削除を表示。行末「-」は `RemoveFromPlaylistCommand` で、確認後にプレイリストから曲を除外する。右上Xは `ShowPlaylistSelectorCommand`。

## 7. パフォーマンス最適化要件

楽曲ListBoxはIsVirtualizing=True、Recycling、Pixel、CanContentScroll=Trueの4条件を指定し、親ScrollViewerは置かない。静的背景にBlurEffectを使用する。アートは非同期取得、表示切替は親のContentTemplateで行う。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [PlaylistTracksView.xaml](../../../AudioEffector/Presentation/Views/PlaylistTracksView.xaml)
- [ユースケース](../../ユースケース/03_プレイリスト管理.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [お気に入りUI仕様書](../お気に入り/お気に入りUI仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/PlaylistTracksView.xaml.cs)
- [PlaylistViewModel](../../../AudioEffector/Presentation/ViewModels/PlaylistViewModel.cs)
