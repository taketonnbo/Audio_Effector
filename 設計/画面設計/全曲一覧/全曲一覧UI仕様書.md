# 全曲一覧 UI仕様書

## 1. 概要 (Overview)

ライブラリ内のすべての楽曲（`Track` オブジェクト）をフラットなデータグリッド形式で一覧表示し、ブラウズ・検索・一括管理・再生を行うメイン画面。

- **主要操作対象オブジェクト**: [楽曲 (`Track`)](../../主要オブジェクト/01_楽曲_Track.md)
- **ユーザー体験**: ライブラリ全体からの高速な楽曲絞り込み、柔軟なソート、複数選択による一括操作（キュー投入、プレイリスト追加、タグ編集、端末転送）。

---

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+--------------------------------------------------------------------------------------------------------------------+
| [▶ すべて再生]  [🔀 シャッフル]    [🔍 楽曲を検索...                            ]               全 1,248 曲 (78.5 時間) |
+--------------------------------------------------------------------------------------------------------------------+
|    | #   | ★ | タイトル                     | アーティスト          | アルバム              | 時間  | 形式  | ジャンル |
|----+-----+---+------------------------------+-----------------------+-----------------------+-------+-------+----------|
| || | 1   | ★ | Midnight City                | M83                   | Hurry Up, We're D...  | 04:03 | FLAC  | Synthpop |
|    | 2   | ☆ | Starboy                      | The Weeknd            | Starboy               | 03:50 | MP3   | R&B      |
| >  | 3   | ★ | Get Lucky (feat. Pharrell)   | Daft Punk             | Random Access Memo... | 06:09 | HiRes | Disco    |
|    | 4   | ☆ | Instant Crush                | Daft Punk             | Random Access Memo... | 05:37 | HiRes | Synthpop |
|    | 5   | ☆ | Lose Yourself to Dance       | Daft Punk             | Random Access Memo... | 05:53 | HiRes | Disco    |
|    | 6   | ★ | The Less I Know The Better   | Tame Impala           | Currents              | 03:36 | FLAC  | Psyche   |
|    | 7   | ☆ | Let It Happen                | Tame Impala           | Currents              | 07:48 | FLAC  | Psyche   |
|    | ... |   | ...                          | ...                   | ...                   | ...   | ...   | ...      |
+--------------------------------------------------------------------------------------------------------------------+
  ^ 選択行: 左端3pxシアンバー (||)      ^ 再生中アイコン (>)
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

---

## 3. 画面配置とペインレイアウト

- **位置付け**: MainWindowの中央ワークスペース列（`CurrentViewType=AllSongs`）。
- **グリッド構造**:
  - 行0 (`Auto`): ツールバー（一括再生アクション、検索ボックス、楽曲総数・総時間サマリー）。
  - 行1 (`*`): UI仮想化対応の全曲データグリッド（`VirtualizedListBox` / `DataGrid`）。
- **サイズ要件**: ワークスペースの幅・高さに100%追従（MinWidth: 400px）。

---

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 画面背景 |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | ヘッダーおよびツールバー背景 |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | 行選択・ホバー背景 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 選択行アクセントバー（3px）、再生中アイコン |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主要テキスト（曲名） |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | 補助テキスト（アーティスト、アルバム、時間） |

- **タイポグラフィ**: 曲名: `Body (13pt) / SemiBold`、メタデータ: `Caption (11pt) / Regular`。
- **行高**: 36px（リスト表示の標準行高）。

---

## 5. 構成要素と共通コントロール

1. **ツールバー領域**:
   - `PrimaryButton`: 「すべて再生」（ライブラリ全曲をキューに追加して先頭から再生）。
   - `SecondaryButton`: 「シャッフル」（全曲をシャッフルして即時再生）。
   - `SearchBox`: リアルタイムインクリメンタル検索（曲名、アーティスト、アルバムを横断フィルタ）。
   - カウント表示: `全 N 曲 (HH.H 時間)` のテキスト表示。
2. **データグリッド列構成**:
   - 左端アクセント: 選択時に幅3pxのネオンシアンバーを表示。
   - 再生状態アイコン: 再生中（`IsPlaying=True`）の波形・再生アイコン。
   - `#`: トラック連番（ソート状態追従）。
   - `★`: お気に入りトグルボタン（クリックで即時反転）。
   - `タイトル`: 楽曲タイトル（文字色: `TextForegroundBrush`）。
   - `アーティスト`: アーティスト名（クリックでアーティスト画面へ遷移可能）。
   - `アルバム`: アルバム名（クリックでアルバム詳細へ遷移可能）。
   - `時間`: 再生時間（`mm:ss` 形式、右寄せ）。
   - `形式`: 音声フォーマット・バッジ（FLAC, Hi-Res, MP3等）。
   - `ジャンル`: 音楽ジャンル。

---

## 6. アニメーションとインタラクション

- **ダブルクリック**: 対象楽曲を即時再生（`PlayTrackCommand`）。
- **シングルクリック**: 行の選択（`IsSelected` 反転）。
- **複数選択 (マルチセレクト)**:
  - `Ctrl + Click`: 個別追加選択。
  - `Shift + Click`: 範囲連続選択。
  - `Ctrl + A`: リスト内全選択。
- **右クリック（コンテキストメニュー）**:
  - 「再生」「次に再生」「最後に再生（キューに追加）」
  - 「お気に入りに追加 / 解除」
  - 「プレイリストに追加...」（サブメニューまたはダイアログ表示）
  - 「端末へ転送」（接続機器がある場合）
  - 「プロパティ」（メタデータ・タグ表示）
  - 「ファイルの場所を開く」
  - 「ライブラリから削除」（Ctrl+Z Undo対応）
- **ドラッグ＆ドロップ (D&D)**:
  - 選択した1曲または複数曲を右側再生キューパネルへドロップしてキュー割り込み投入。
  - 左サイドバーのプレイリスト項目へドロップしてプレイリストへ楽曲追加。

---

## 7. パフォーマンス最適化要件

数万曲規模の大規模ライブラリにおいても60fpsの滑らかなスクロールを維持するため、以下のUI仮想化を必須とします：

```xml
VirtualizingPanel.IsVirtualizing="True"
VirtualizingPanel.VirtualizationMode="Recycling"
VirtualizingPanel.ScrollUnit="Pixel"
ScrollViewer.CanContentScroll="True"
```

- 親コンテナによる `ScrollViewer` 内包を禁止し、リスト自身の仮想化スクローラーを使用。
- 各行セル内のバインディングは極力シンプルに保ち、過度なコンバーターや高負荷エフェクト（DropShadowEffect/BlurEffect）を排除。

---

## 8. 関連Issue・仕様書

- [主要オブジェクト仕様: 楽曲 (`Track`)](../../主要オブジェクト/01_楽曲_Track.md)
- [インタラクション標準化ガイドライン](../../主要オブジェクト/インタラクション標準化ガイドライン.md)
- [ユースケース: 楽曲ライブラリ管理](../../ユースケース/02_楽曲ライブラリ管理.md)
- [デザイントークン仕様書](../../デザインシステム/01_デザイントークン仕様書.md)
- [共通コントロールスタイル仕様書](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [パフォーマンス最適化詳細設計書](../../詳細設計/パフォーマンス最適化.md)
