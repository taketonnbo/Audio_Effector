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

> **構想要素に関する注記**: モックアップ内のヘッダー一括操作ボタン群・曲数/総時間サマリー・D&D曲順並び替え（Domain/Application層 `ReorderTrackAsync` は実装済み）は拡張構想（関連Issue: #233, #242, #252）です。

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`CurrentViewType = PlaylistTracks`、`DataContext` は `MainViewModel.Playlist`（`PlaylistViewModel`）。行0がヘッダー（`Auto`：2×2分割サムネイル、`CurrentPlaylistName`、右端の閉じるボタン `X`）、行1が仮想化楽曲 `ListBox`（`*`）の2行構成。背景には再生中アルバムアート（`BlurEffect`＋半透明暗幕）を両行にまたがって透過配置する。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | 行・カード背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主テキスト（プレイリスト名・曲名） |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | アーティスト名 |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | 再生時間 |
| `AlwaysNeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | サムネイル上の再生状態アイコン |

## 5. 構成要素と共通コントロール

- ヘッダーの `CurrentPlaylistName`（2×2サムネイル `Thumbnail1`〜`4` 付き）と `PlaylistTracks` の楽曲行を表示する。
- 各楽曲行は、転送選択用 `CheckBox`（`IsSelectionMode` 連動、転送中 `DeviceBrowser.IsTransferring` 時は非活性）、アルバムアート（再生中は暗幕＋再生/一時停止アイコン）、曲名・アーティスト名、再生時間、および右端のプレイリスト除外ボタン「`-`」で構成する。
- プレイリスト切替時の非同期トラック読み込み（`PlaylistViewModel`）では、`CancellationTokenSource` と世代番号（`_trackLoadVersion`）を併用し、古い読み込み結果による表示上書き（レースコンディション）を防止する。

## 6. アニメーションとインタラクション

- **楽曲再生**: 行のクリックで親ウィンドウ（`MainViewModel`）の `PlayTrackCommand` を実行し、再生を開始する。
- **プレイリストからの除外 vs ライブラリからの削除の区別**:
  - **行右端の「`-`」ボタン (`PlaylistViewModel.RemoveFromPlaylistCommand`)**: 確認 `MessageBox` を表示した上で、**現在のプレイリストからのみ該当曲を除外**し、2×2サムネイルを再計算して `playlists.json` を即時保存する（ライブラリ本体からは削除されない）。
  - **右クリックメニューの「削除」 (`MainViewModel.DeleteTrackCommand`)**: ライブラリ全体（およびお気に入り）から該当曲を除外する。
- **その他の右クリックメニュー**: 「再生 / 停止」「次に再生」「最後に再生」「お気に入りに追加 / 解除」「プレイリストに追加...」「プロパティ」「ファイルの場所を開く」。
- **一覧へ戻る**: ヘッダー右上の `X` ボタンは `ShowPlaylistSelectorCommand` を実行し、`PlaylistSelectorView`（プレイリスト一覧）へ戻る。

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
