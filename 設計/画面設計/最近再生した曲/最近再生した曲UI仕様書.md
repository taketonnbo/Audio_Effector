# 最近再生した曲 UI仕様書

## 1. 概要 (Overview)

ユーザーが再生した楽曲（`RecentTrackEntry` / `Track`）の履歴を再生日時の新しい順（最大100件）で永続化・一覧表示し、履歴内検索・再再生・キュー投入・プレイリスト追加・履歴整理を行う画面。

- **主要操作対象オブジェクト**: [楽曲 (`Track`)](../../主要オブジェクト/01_楽曲_Track.md)、`RecentTrackEntry`
- **ユーザー体験**: アプリ再起動後も保持される最近聴いた楽曲への素早いアクセス、再生日時の確認、お気に入り登録や履歴の一括/個別消去。
- **再生キュー「履歴」タブとの役割分担**: 本画面は `IAudioService.TrackChanged`（再生開始・切替時）を起点として `%AppData%\AudioEffector\RecentTracks.json` に永続化されるライブラリ履歴であるのに対し、再生キュースライドパネルの「履歴」タブ（`PlayerControlViewModel.PlayHistory`）は `TrackPlaybackEnded`（再生終了・スキップ時）を起点とするインメモリのセッション履歴であり、それぞれ独立して動作する。

---

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+--------------------------------------------------------------------------------------------------------------------+
| [▶ 履歴から再生]  [履歴をクリア]    [🔍 履歴内を検索...                         ✕]               最近再生した 42 曲 |
+--------------------------------------------------------------------------------------------------------------------+
|    | [Art] | 再生日時         | ★ | タイトル                     | アーティスト          | アルバム         | 時間  |
|----+-------+------------------+---+------------------------------+-----------------------+------------------+-------|
| || | [▶24] | 2026/09/11 21:15 | ★ | Midnight City                | M83                   | Hurry Up, We'... | 04:03 |
|    | [24]  | 2026/09/11 21:11 | ☆ | Starboy                      | The Weeknd            | Starboy          | 03:50 |
|    | [24]  | 2026/09/11 21:05 | ★ | Get Lucky (feat. Pharrell)   | Daft Punk             | Random Access... | 06:09 |
|    | [24]  | 2026/09/11 20:59 | ☆ | Instant Crush                | Daft Punk             | Random Access... | 05:37 |
|    | ...   | ...              |   | ...                          | ...                   | ...              | ...   |
+--------------------------------------------------------------------------------------------------------------------+
  ^ 選択行または再生中行: 左端3pxシアンバー (||) / ダブルクリックで即時再生 / 右クリックでキュー追加・履歴削除
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

---

## 3. 画面配置とペインレイアウト

- **位置付け**: `MainWindow` の中央ワークスペース列（`CurrentViewType = Recent`、`DataContext = MainViewModel.Recent`）。
- **グリッド構造（3行構成）**:
  - 行0 (`Auto`): ツールバー（「▶ 履歴から再生」ボタン、「履歴をクリア」ボタン、検索ボックス＋クリア `✕` ボタン、履歴件数サマリー `最近再生した N 曲`）。
  - 行1 (`Auto`, 高さ `30px`): 固定カラムヘッダー行（`再生日時`, `★`, `タイトル`, `アーティスト`, `アルバム`, `時間`）。縦スクロールバーの表示幅に連動して右パディングを自動同期する。
  - 行2 (`*`): UI仮想化対応の履歴 `ListBox` および Empty State オーバーレイ（履歴自体が0件の場合は「再生履歴はありません」、検索結果が0件の場合は「一致する再生履歴が見つかりません」を切り替え表示）。
- **サイズ要件**: ワークスペースの幅・高さに100%追従。

---

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 画面背景 |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | ツールバーおよび列ヘッダー背景 |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | 「履歴をクリア」セカンダリボタン・検索ボックス背景 |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | 行ホバー・選択背景 |
| `BorderBrush` | <span style="background-color:#343E4E;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #343E4E</span> | `#343E4E` | ツールバー・列ヘッダー境界線、ボタン・検索枠線 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | プライマリボタン背景、左端3pxアクセントバー、再生中曲名強調 |
| `AlwaysNeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | サムネイル暗幕上の再生中 `▶` アイコン |
| `InverseTextForegroundBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 「▶ 履歴から再生」ボタン文字色 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 曲名、セカンダリボタン文字色 |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | アーティスト名、アルバム名 |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | 再生日時、再生時間、列ヘッダー、空状態テキスト |

- **行高**: `36px`。
- **タイポグラフィ**: 曲名: `13pt`（再生中は `SemiBold`＋`NeonCyanBrush`）、アーティスト・アルバム: `12pt`、再生日時・時間: `11pt`。

---

## 5. 構成要素と永続化・同期仕様

1. **ツールバー領域**:
   - `RecentPrimaryButtonStyle`: 「▶ 履歴から再生」（表示中の履歴リスト先頭から連続再生。リスト空時は非活性）。
   - `RecentSecondaryButtonStyle`: 「履歴をクリア」（`HasHistory == False` 時は非活性。押下時に Yes/No 確認 `MessageBox` を表示）。
   - 検索ボックス: 曲名・アーティスト・アルバムの3項目横断インクリメンタルフィルタ＋クリアボタン `✕`。
   - サマリー表示: `最近再生した N 曲`。
2. **リスト列構成 (`RecentTrackEntry`)**:
   - 左端アクセントバー: 選択時（`IsSelected`）または再生中（`Track.IsPlaying`）に幅 `3px` の `NeonCyanBrush` 縦バーを表示。
   - サムネイル列: `24×24px`（角丸 `3px`）のアルバムアートサムネイル＋再生中 `▶` オーバーレイ。
   - `再生日時`: `PlayedAtDisplay`（`yyyy/MM/dd HH:mm` 形式で事前整形）。
   - `★`: お気に入りトグルボタン（`LibraryViewModel.ToggleFavoriteCommand` 連携）。
   - `タイトル` / `アーティスト` / `アルバム` / `時間`（`DurationString`）。
3. **自動記録・重複排除・JSON永続化仕様 (`RecentViewModel` / `JsonRecentTrackRepository`)**:
   - **記録トリガー**: `IAudioService.TrackChanged` イベント発火時に自動記録。
   - **重複排除と上限**: 同一 `FilePath`（大文字小文字無視）の既存エントリがあれば削除して先頭（インデックス 0）に最新日時で再挿入する。最大保持件数は **`MaxHistoryCount = 100`** 件とし、超過した古い末尾エントリを自動削除する。
   - **永続化フォーマット**: `%AppData%\AudioEffector\RecentTracks.json` に `FilePath`, `Title`, `Artist`, `Album`, `DurationSeconds`, `PlayedAt` を JSON 配列で保存（`.tmp` 一時ファイル書き出し＋`File.Move` によるアトミック保存、破損時やファイル不在時は空リストへフォールバック）。
   - **ライブラリ実体同期**: 起動時および `LibraryViewModel.Tracks.CollectionChanged` 発火時（`DispatcherPriority.Background` コアレッシング）に、`FilePath` でライブラリ上の最新 `Track` インスタンスへ差し替え（`WithTrack`）、お気に入り状態や再生中状態のバインディング連動を維持する。

---

## 6. アニメーションとインタラクション

- **ダブルクリック**: 対象エントリの楽曲から表示中履歴リストをコンテキストとして即時再生（`PlayEntryCommand`）。子ボタン・スクロールバー・余白のダブルクリックは除外。
- **シングルクリック**: 行の選択（`SelectedEntry` 更新）。
- **右クリック（コンテキストメニュー）**:
  - 「再生」「次に再生」「最後に再生」
  - 「お気に入りに追加 / お気に入りから解除」
  - 「プレイリストに追加...」
  - 「履歴から削除」（選択した1件のみ履歴から削除し `RecentTracks.json` を即時更新）
  - 「プロパティ」「ファイルの場所を開く」
- **履歴全クリア**: 「履歴をクリア」ボタン押下時に確認 `MessageBox`（「すべての再生履歴を消去しますか？」）を表示し、`Yes` 選択で全件削除および JSON 保存を実行。

---

## 7. パフォーマンス最適化要件

- UI仮想化4属性を適用し、親 `ScrollViewer` の内包を禁止：
  ```xml
  VirtualizingPanel.IsVirtualizing="True"
  VirtualizingPanel.VirtualizationMode="Recycling"
  VirtualizingPanel.ScrollUnit="Pixel"
  ScrollViewer.CanContentScroll="True"
  ```
- 再生日時文字列（`PlayedAtDisplay`）は `RecentTrackEntry` コンストラクタで事前整形し、スクロール時の文字列フォーマット負荷を排除。
- `LibraryViewModel.Tracks` の連続変更通知は `Interlocked.Exchange` と `DispatcherPriority.Background` で1回の同期処理にコアレッシングする。

---

## 8. 関連Issue・仕様書

- [RecentView.xaml](../../../AudioEffector/Presentation/Views/RecentView.xaml) / [RecentViewModel.cs](../../../AudioEffector/Presentation/ViewModels/RecentViewModel.cs) / [JsonRecentTrackRepository.cs](../../../AudioEffector.Infrastructure/Repository/JsonRecentTrackRepository.cs)
- [主要オブジェクト仕様: 楽曲 (`Track`)](../../主要オブジェクト/01_楽曲_Track.md)
- [主要オブジェクト仕様: 再生キュー (`PlayQueue`)](../../主要オブジェクト/04_再生キュー_PlayQueue.md)
- [インタラクション標準化ガイドライン](../../主要オブジェクト/インタラクション標準化ガイドライン.md)
- [ユースケース: 楽曲再生・再生制御](../../ユースケース/01_楽曲再生・再生制御.md)
- [デザイントークン仕様書](../../デザインシステム/01_デザイントークン仕様書.md)
- [共通コントロールスタイル仕様書](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- 関連Issue: [#105](https://github.com/taketonnbo/Audio_Effector/issues/105)
