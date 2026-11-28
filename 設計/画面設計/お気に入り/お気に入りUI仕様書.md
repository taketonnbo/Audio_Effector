# お気に入り UI仕様書

## 1. 概要 (Overview)

お気に入りとして登録した楽曲（`Track.IsFavorite == true` / `FavoriteTrackEntry`）を一覧表示し、再生・管理する画面。

- **主要操作対象オブジェクト**: [楽曲 (`Track`)](../../主要オブジェクト/01_楽曲_Track.md)、`FavoriteTrackEntry`
- **ユーザー体験**: メタデータベースの楽曲識別により、ファイルの移動や音源フォーマット（MP3↔FLAC等）の差し替え後も失われないお気に入りコレクションの閲覧・再生・解除。

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
|    | ...| ★ | ...                          | ...                   | ...                   | ...   | ...           |
+--------------------------------------------------------------------------------------------------------------------+
  ^ 選択行: 左端3pxシアンバー (||)      ^ 再生中 (>)                                     ^ クリックでお気に入り解除
```

> **構想要素に関する注記**: 上記ASCIIモックアップのお気に入り専用ヘッダー（一括再生・シャッフル・検索・件数サマリー）は拡張構想（関連Issue: #233, #242, #252）です。`PlaylistTracksView.xaml` ではお気に入りモード（`IsFavoritesView = True`、見出し `"Favorites"`）をプレイリスト画面と共用します。

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

- **位置付け**: `CurrentViewType = Favorites` は `PlaylistTracksView.xaml` を共用し、`DataContext` は `MainViewModel.Playlist`（`PlaylistViewModel`）。
- **構造**: 背景アートワーク（`favorites_bg.png` + `BlurEffect` + 暗幕オーバーレイ）の上に、行0（ヘッダー `Auto`）と行1（仮想化楽曲 `ListBox` `*`）の2行で構成する。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | 行・カード背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主テキスト（見出し・曲名） |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | アーティスト名 |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | 再生時間 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 操作アクセント |

## 5. 構成要素と永続化・メタデータ照合仕様

1. **画面表示要素 (`PlaylistTracksView` / `PlaylistViewModel`)**:
   - `IsFavoritesView = True` の場合、見出し `CurrentPlaylistName` に `"Favorites"` を表示し、背景画像に `Assets/Images/favorites_bg.png` を適用して `PlaylistTracks` に現在のお気に入り楽曲一覧を表示する。
   - 他画面（全曲一覧、アルバム一覧、アーティスト一覧、最近再生した曲、再生キュー等）でお気に入りトグルが実行された場合も、お気に入り画面表示中であればリアルタイムにリストへ反映される。
2. **メタデータベース永続化と照合仕様 (`FavoriteTrackEntry` / `FavoriteTrackMatcher` / `JsonFavoriteRepository`)**:
   - **保存先とスキーマ**: `IDataDirectoryProvider` が解決するデータ保存先ディレクトリ（既定: `%AppData%\AudioEffector`、設定画面「データ管理・その他」から変更可能）配下の `favorites.json` に `Title`, `Artist`, `Album`, `TrackNumber`, `DurationSeconds`, `LastKnownFilePath`, `AddedAt` を持つ `FavoriteTrackEntry` のJSON配列としてアトミック保存（`.tmp` 経由）する。保存先変更時は `LibraryViewModel.ReloadFavoritesFromRepository()` によりお気に入り一覧と表示状態を即時再読込する。旧形式（ファイルパス文字列配列 `List<string>`）からは起動時に自動マイグレーションを行い、一時的なI/O読み取りエラー時には既存ファイルを空配列で上書き破壊しない保護機構を備える。
   - **3段階照合アルゴリズム (`FavoriteTrackMatcher`)**:
     1. 未タグ曲や旧形式エントリに対するファイルパス優先照合
     2. 正規化した `Title` + `Artist` + `Album`（および `TrackNumber`・再生時間 `±5.0秒` 許容）によるメタデータ照合（未タグ曲同士の誤マッチは防止）
     3. `LastKnownFilePath` によるパスフォールバック照合
   - **自動修復**: ファイルの移動や拡張子変更（MP3→FLAC差し替え等）によってパスやメタデータが変化していた場合、照合成功時に `favorites.json` 側の登録情報を最新のトラック情報へ自動更新する（詳細は [ドメイン層詳細設計書](../../詳細設計/アーキテクチャ/ドメイン層詳細設計書.md) および [インフラストラクチャ層詳細設計書](../../詳細設計/アーキテクチャ/インフラストラクチャ層詳細設計書.md) を参照）。

## 6. アニメーションとインタラクション

- **楽曲クリック**: 単一クリックで `PlayTrackCommand` により再生を開始する。
- **右クリック（コンテキストメニュー）**: 「再生 / 停止」「次に再生」「最後に再生」「お気に入りから解除」「プレイリストに追加...」「プロパティ」「ファイルの場所を開く」「削除」。
- **お気に入り解除**: コンテキストメニューの「お気に入りから解除」または行右端の「`-`」ボタン（`RemoveFromPlaylistCommand`）押下により、確認ダイアログなしで即座にお気に入りを解除し `favorites.json` を更新する。
- **ヘッダー `X` ボタン**: `ShowPlaylistSelectorCommand` によりプレイリスト一覧画面へ遷移する。

## 7. パフォーマンス最適化要件

- 楽曲 `ListBox` は `VirtualizingPanel.IsVirtualizing="True"`, `VirtualizationMode="Recycling"`, `ScrollUnit="Pixel"`, `ScrollViewer.CanContentScroll="True"` の4属性を適用し、親 `ScrollViewer` を置かない。
- ライブラリスキャン時のお気に入り照合（`Reconcile`）はハッシュインデックスを用いた $O(T + F)$ の線形時間で処理し、大量楽曲環境でもスキャン速度を低下させない。

## 8. 関連Issue・仕様書

- [PlaylistTracksView.xaml](../../../AudioEffector/Presentation/Views/PlaylistTracksView.xaml) / [PlaylistViewModel.cs](../../../AudioEffector/Presentation/ViewModels/PlaylistViewModel.cs)
- [FavoriteTrackMatcher.cs](../../../AudioEffector.Domain/Services/FavoriteTrackMatcher.cs) / [JsonFavoriteRepository.cs](../../../AudioEffector.Infrastructure/Repository/JsonFavoriteRepository.cs)
- [ユースケース: 楽曲ライブラリ管理](../../ユースケース/02_楽曲ライブラリ管理.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [プレイリスト内楽曲UI仕様書](../プレイリスト内楽曲/プレイリスト内楽曲UI仕様書.md)
- 関連Issue: [#265](https://github.com/taketonnbo/Audio_Effector/issues/265)
