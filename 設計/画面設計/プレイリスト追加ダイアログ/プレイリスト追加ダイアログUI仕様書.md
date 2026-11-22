# プレイリスト追加ダイアログ UI仕様書

## 1. 概要 (Overview)

対象楽曲（単曲、アルバム全曲、または複数選択された `Track` 群）を追加する `UserPlaylist` を選択するモーダルダイアログ。

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

> **構想要素に関する注記**: モックアップ内の各プレイリスト曲数表示およびダイアログ内インライン新規プレイリスト作成は拡張構想（関連Issue: #233, #242, #254）です。

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`350×450px`、`ResizeMode="NoResize"`、`WindowStartupLocation="CenterOwner"`（親ウィンドウ中央表示）。3行 `Grid` で、行0が対象曲案内ラベル（`Auto`）、行1がプレイリスト選択 `ListBox`（`*`）、行2が `"ADD"` / `"CANCEL"` ボタン行（`Auto`）。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | ダイアログ背景 |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | リスト・ボタン背景 |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | ボタンホバー背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主テキスト |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 選択行強調・ADDボタンアクセント |

## 5. 構成要素と共通コントロール

- 上部案内ラベルは `Add "{代表曲名}" to:` を表示し、中央 `ListBox` は `Playlists` と `SelectedPlaylist` にバインドする。
- 下部右寄せで `"ADD"` ボタンと `"CANCEL"` ボタンを配置する。

## 6. アニメーションとインタラクション

- **確定 (`ADD`)**:
  - プレイリスト未選択のまま `"ADD"` を押下した場合は、選択を促す警告 `MessageBox` を表示する。
  - プレイリストを選択して `"ADD"` を押下すると `DialogResult = true` で閉じ、呼び出し元（`PlaylistViewModel`）が対象トラック群を追加する。
- **重複スキップと結果通知 (`PlaylistViewModel`)**:
  - 対象プレイリスト内に同一 `FilePath`（大文字小文字無視）の楽曲が既に存在する場合は重複登録をスキップする。
  - 追加完了時は結果 `MessageBox`（単曲時は追加完了または追加済み通知、複数曲/アルバム時は追加曲数または全曲追加済み通知）を表示し、対象トラックの `IsSelected` フラグリセット、2×2サムネイル更新、現在表示中プレイリストへの即時反映、および `playlists.json` 保存を実行する。
- **取消 (`CANCEL`)**: `"CANCEL"` 押下またはウィンドウを閉じると追加をキャンセルする。

## 7. パフォーマンス最適化要件

ListBoxがスクロールを所有する。Recycling／Pixelなどの4条件は明示指定しない。固定数の操作要素に動的エフェクトは使わない。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [PlaylistSelectionDialog.xaml](../../../AudioEffector/Presentation/Views/PlaylistSelectionDialog.xaml)
- [ユースケース](../../ユースケース/03_プレイリスト管理.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/PlaylistSelectionDialog.xaml.cs)
