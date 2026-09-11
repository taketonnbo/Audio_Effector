# プレイリスト追加ダイアログ UI仕様書

## 1. 概要 (Overview)

対象Trackを追加するUserPlaylistを選択する。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+---------------------------------------------------+
| プレイリストに追加 (Add to Playlist)         [X]  |
+---------------------------------------------------+
| 追加対象:                                         |
| "Midnight City" - M83                             |
|                                                   |
| 追加先のプレイリストを選択:                       |
| +-----------------------------------------------+ |
| | > [📋] Driving Beats                (32 曲)   | | <- 選択中
| |   [📋] Chillout Lounge              (45 曲)   | |
| |   [📋] Synthwave 80s                (28 曲)   | |
| |   [📋] Acoustic Favorites           (18 曲)   | |
| |   [📋] Night Drive                  (50 曲)   | |
| +-----------------------------------------------+ |
|                                                   |
| [+ 新規プレイリストを作成して追加...]             |
| [ 新しいプレイリスト名を入力...                 ] |
+---------------------------------------------------+
|               [ 追加 (ADD) ]  [ キャンセル (CANCEL) ]
+---------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

350×450、ResizeMode=NoResize、所有者中央。余白15、行は対象曲の案内 `Auto`、プレイリストListBox `*`、ADD／CANCEL `Auto`。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | 入力・カード背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主テキスト |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 操作アクセント |

案内文14、プレイリスト名13、行Padding=10,8。ボタン80×30、間隔10、上余白15。

## 5. 構成要素と共通コントロール

案内は `Add "{曲名}" to:`。`Playlists` と `SelectedPlaylist` にバインドする。選択行はNeonCyanBrush背景と黒文字、ホバー時はローカル暗色背景。左端3px選択バーの方式は採用しない。末尾にADD、CANCEL。

## 6. アニメーションとインタラクション

行選択後ADDで選択したプレイリストを返す。未選択でADDしたときは選択を求める。CANCEL／Escは追加を取り消す。曲への追加処理は呼出元に委譲する。

## 7. パフォーマンス最適化要件

ListBoxがスクロールを所有する。Recycling／Pixelなどの4条件は明示指定しない。固定数の操作要素に動的エフェクトは使わない。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [PlaylistSelectionDialog.xaml](../../../AudioEffector/Presentation/Views/PlaylistSelectionDialog.xaml)
- [ユースケース](../../ユースケース/03_プレイリスト管理.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/PlaylistSelectionDialog.xaml.cs)
