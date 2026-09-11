# フォルダ UI仕様書

## 1. 概要 (Overview)

ローカルストレージ内のフォルダ階層を探索し、指定フォルダ内の音声ファイルを直接ブラウズ・スキャン・再生するファイルエクスプローラー画面。

- **主要操作対象オブジェクト**: ローカルディレクトリ、[楽曲 (`Track`)](../../主要オブジェクト/01_楽曲_Track.md)
- **ユーザー体験**: タグ付けが未整理な音源ファイルの直接再生、新規音楽フォルダの追加スキャン、フォルダ構造に基づいた直感的なブラウジング。

---

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+--------------------------------------------------------------------------------------------------------------------+
| [📁 フォルダをスキャン / 追加]    パス: C:\Users\Music\Hi-Res Audio                     [▶ フォルダ内全曲を再生]   |
+------------------------------------+-------------------------------------------------------------------------------+
| フォルダツリー                     | 含まれる楽曲ファイル (18 ファイル)                                            |
|------------------------------------|-------------------------------------------------------------------------------|
| v [📁] Hi-Res Audio                |    | #  | ファイル名                    | タイトル         | アーティスト | 時間  |
|   > [📁] 2024 New Releases         |----+----+-------------------------------+------------------+--------------+-------|
|   v [📁] Electronic & Synth        | || | 01 | 01_Midnight_City.flac         | Midnight City    | M83          | 04:03 |
|       [📁] Daft Punk               |    | 02 | 02_Get_Lucky.flac             | Get Lucky        | Daft Punk    | 06:09 |
|       [📁] M83                     |    | 03 | 03_Instant_Crush.flac         | Instant Crush    | Daft Punk    | 05:37 |
|   > [📁] Rock & Alternative        |    | 04 | 04_Lose_Yourself.flac         | Lose Yourself    | Daft Punk    | 05:53 |
|   > [📁] Soundtracks               |    | 05 | 05_Giorgio_by_Moroder.flac    | Giorgio by M...  | Daft Punk    | 09:04 |
|                                    |    | ...| ...                           | ...              | ...          | ...   |
+------------------------------------+-------------------------------------------------------------------------------+
  ^ 階層ツリー (TreeView)              ^ 選択フォルダ内ファイルデータグリッド (UI仮想化対応)
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

---

## 3. 画面配置とペインレイアウト

- **位置付け**: MainWindowの中央ワークスペース列（`CurrentViewType=Folders`）。
- **グリッド構造**:
  - 行0 (`Auto`): ツールバー（フォルダ追加・スキャンボタン、現在パス表示、一括再生ボタン）。
  - 行1 (`*`): 2列構成（列0: フォルダツリー `280px / Auto`、列Splitter: `4px`、列1: 楽曲ファイル一覧 `*`）。
- **サイズ要件**: ワークスペースの幅・高さに100%追従（TreeView最小幅200px）。

---

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 画面背景 |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | ツールバーおよびツリー背景 |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | ツリーノードおよび行ホバー背景 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 主要操作ボタン、選択バー（3px） |
| `BorderBrush` | <span style="background-color:#343E4E;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #343E4E</span> | `#343E4E` | スプリッターおよび境界線 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主要テキスト |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | ファイル名、パス表示 |

---

## 5. 構成要素と共通コントロール

1. **ツールバー**:
   - `PrimaryButton`: 「フォルダをスキャン / 追加」（ダイアログを開きライブラリ対象フォルダを指定）。
   - パス表示: 現在選択されているフォルダのフルパス表示。
   - `SecondaryButton`: 「フォルダ内全曲を再生」。
2. **左ペイン（フォルダツリー）**:
   - 階層型 `TreeView`（展開/折りたたみアイコン、フォルダアイコン、フォルダ名）。
   - 選択時ネオンシアンハイライト。
3. **右ペイン（ファイル一覧）**:
   - `VirtualizedListBox` / `DataGrid`: ファイル名、トラック番号、曲名、アーティスト、再生時間、ファイルサイズ、形式。
   - 行選択時の左端ネオンシアンアクセント縦バー（幅3px）。

---

## 6. アニメーションとインタラクション

- **ツリーノードクリック**: 対象フォルダ内の音声ファイルを右ペインへ即時ロード。
- **ファイル行ダブルクリック**: 該当音声ファイルを即時再生（`PlayTrackCommand`）。
- **右クリック（フォルダツリー）**:
  - 「フォルダ内全曲を再生」「次に再生」「最後に再生（キューに追加）」
  - 「エクスプローラーで開く」
- **右クリック（ファイル行）**:
  - 「再生」「キューに追加」「プレイリストに追加」「プロパティ」「ファイルの場所を開く」
- **ドラッグ＆ドロップ (D&D)**:
  - 外部エクスプローラーからフォルダまたは音声ファイルをウィンドウへD&Dして即時インポート。
  - 選択した楽曲ファイルを右側再生キューパネルへD&D。

---

## 7. パフォーマンス最適化要件

- 大規模なディレクトリ構造でも快適に操作できるよう、フォルダツリーはオンデマンド遅延読み込み（Lazy Loading）を採用。
- 右ペインの楽曲一覧にはUI仮想化を必須適用：
  ```xml
  VirtualizingPanel.IsVirtualizing="True"
  VirtualizingPanel.VirtualizationMode="Recycling"
  VirtualizingPanel.ScrollUnit="Pixel"
  ScrollViewer.CanContentScroll="True"
  ```
- 高負荷エフェクトは排除。

---

## 8. 関連Issue・仕様書

- [主要オブジェクト仕様: 楽曲 (`Track`)](../../主要オブジェクト/01_楽曲_Track.md)
- [インタラクション標準化ガイドライン](../../主要オブジェクト/インタラクション標準化ガイドライン.md)
- [ユースケース: 楽曲ライブラリ管理](../../ユースケース/02_楽曲ライブラリ管理.md)
- [デザイントークン仕様書](../../デザインシステム/01_デザイントークン仕様書.md)
- [共通コントロールスタイル仕様書](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [パフォーマンス最適化詳細設計書](../../詳細設計/パフォーマンス最適化.md)
