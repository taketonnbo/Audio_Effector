# ライブラリ全体リスニング統計 UI仕様書

## 1. 概要 (Overview)

左サイドバーの「リスニング統計」から遷移する中央ワークスペース専用ビュー（`LibraryStatsView`）。ライブラリ全体の再生履歴（`ListeningStats.json`）を対象に、期間別 KPI サマリ・各種 TOP 10 ランキング・日別/月別推移グラフ・時間帯別/曜日別傾向グラフを 1 画面で俯瞰し、楽曲再生・プレイリスト作成・詳細画面遷移を完結させる。主要操作対象オブジェクトは `Track`・`Album`・`UserPlaylist`。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+---------------------------------------------------------------------------------------------------+
| リスニング統計          [今週] [今月] [今年] [●全期間]         [▶ TOP 10 を再生] [＋ プレイリスト化] |
+---------------------------------------------------------------------------------------------------+
| +---------------------+ +---------------------+ +---------------------+ +---------------------+   |
| | 総再生回数          | | 累計再生時間        | | 聴いたユニーク曲数  | | トップアーティスト  |   |
| | 128 回              | | 8時間 42分          | | 46 曲               | | Aimer               |   |
| | 日平均 4.3 回       | | 完了再生ベース集計  | | 12 アーティスト/9ALB| | 34 回再生           |   |
| +---------------------+ +---------------------+ +---------------------+ +---------------------+   |
|                                                                                                   |
| +---------------------------------------------+ +-----------------------------------------------+ |
| | よく聴く曲 TOP 10    クリック再生/右クリック | | よく聴くアーティスト TOP 10 [●アーティスト][アルバム]| |
| | #1 [Art] Track A / Artist 1          18 回  | | #1 (Art) Artist 1 (8曲 • 1h 12m)       34 回  | |
| |          ============================       | |          ============================         | |
| | #2 [Art] Track B / Artist 2          14 回  | | #2 (Art) Artist 2 (5曲 • 48m)          24 回  | |
| |          ======================             | |          ====================                 | |
| +---------------------------------------------+ +-----------------------------------------------+ |
|                                                                                                   |
| +---------------------------------------------+ +-----------------------------------------------+ |
| | 月別の再生推移                 [日別][●月別] | | リスニング傾向             [●時間帯別][曜日別] | |
| |  +---------------------------------------+  | |  +-----------------------------------------+  | |
| |  |     ▄       █       ▆     ▄     █     |  | |  |  _ _ _ ▂ ▄ ▁ _ ▃ ▆ █ ▇ ▄ ▂ _ _ _        |  | |
| |  | 6月 7月 8月 9月 10月 11月 12月 1月 ...|  | |  | 0 1 2 ... 20 21 22 23                   |  | |
| |  +---------------------------------------+  | |  +-----------------------------------------+  | |
| |                                             | | [ 21:00 台が最もよく聴かれています（24 回）]  | |
| +---------------------------------------------+ +-----------------------------------------------+ |
+---------------------------------------------------------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

[全体レイアウト・ペイン構成UI仕様書](../全体レイアウト・ペイン構成/全体レイアウト・ペイン構成UI仕様書.md) における中央ワークスペースの `ContentControl` 内に、`CurrentViewType == ViewType.ListeningStats` の `DataTrigger` で `LibraryStatsView`（`DataContext="{Binding LibraryStats}"`）を表示する。

- ルートは `Grid` の 2 行構成：
  - Row 0（`Auto`）: 上部ヘッダー（`Padding="20,14"`、左: 画面タイトル、中央: 期間フィルタ 4 セグメント、右: クイックアクション 2 ボタン）。
  - Row 1（`*`）: 縦スクロール可能なオールインワン本体（`ScrollViewer`、`Padding="20,16"`）。
- 本体内部はデータ 0 件時のエンプティ状態カードと、データあり時の 3 段ダッシュボード構成（上段: 4 カラム KPI `UniformGrid`、中段: `1* / 16px / 1*` 2 カラムランキング、下段: `1* / 16px / 1*` 2 カラムバーチャート）を排他表示する。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 画面ルート背景 |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | ヘッダー背景・グラフプロット背景・ゲージトラック背景 |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | KPI カード・ランキングカード・グラフカード背景 |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | ランキング行およびセカンダリボタンのホバー背景 |
| `BorderBrush` | <span style="background-color:#343E4E;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #343E4E</span> | `#343E4E` | カード枠線・0 件バー表示 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;font-weight:bold;">■ #00FFFF</span> | `#00FFFF` | 選択中フィルタ・主要ボタン・順位・ゲージ・ピークバー・インサイト文言 |
| `DarkNeonCyanBrush` | <span style="background-color:#00E5FF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00E5FF</span> | `#00E5FF` | 通常グラフバー |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 画面見出し・カード見出し・主要値・項目タイトル |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | 補助ラベル・アーティスト/アルバム副情報 |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | KPI ラベル・グラフ軸ラベル・操作ヒント |
| `InverseTextForegroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | プライマリボタンおよび選択中セグメント文字色 |

- タイポグラフィ: 画面タイトル `20pt Bold`、KPI 数値 `18〜22pt Bold`、カード見出し `14pt Bold`、項目タイトル `12pt SemiBold`、補助テキスト `10〜11pt`、グラフ軸ラベル `9pt`。
- 角丸: カード `8px`（`Radius.Medium`）、内部プロット枠・行ホバー `6px`、ボタン・セグメント・サムネイル `4px`（`Radius.Small`）、アーティストアイコン `15px` 円形。
- スペーシング: 4px グリッド（`4px / 8px / 12px / 16px / 20px`）。

## 5. 構成要素と共通コントロール

- **期間フィルタセグメント（`StatsSegmentRadioButtonStyle`）**: `今週` / `今月` / `今年` / `全期間` の 4 排他ボタン。`IsChecked` で `NeonCyanBrush` 背景＋`InverseTextForegroundBrush` 文字へ切り替わる。
- **クイックアクションボタン**:
  - `▶ TOP 10 を再生`（`StatsPrimaryButtonStyle`、`PlayTopTracksCommand`）
  - `＋ プレイリスト化`（`StatsSecondaryButtonStyle`、`CreateTopTracksPlaylistCommand`）
  - データ 0 件時は `IsEnabled = false`（`Opacity = 0.4`）で非活性化。
- **ランキング行（`StatsRankingRowButtonStyle`）**: 順位（`#1`〜`#10`）、`AlbumArtLoader.SourcePath` サムネイル（`30×30`）、名称・副情報、再生回数、`StatsGaugeProgressBarStyle`（`Value="{Binding RatioPercent, Mode=OneWay}"`）。
- **バーチャート（`UniformGrid Rows="1"`）**: 各バケットを `Border`（`Height="{Binding BarHeight}"`、`VerticalAlignment="Bottom"`）で軽量描画し、ピークバーは `NeonCyanBrush`、通常バーは `DarkNeonCyanBrush`、0 件バーは `BorderBrush` で色分けする。

## 6. アニメーションとインタラクション

- **期間フィルタ切替**: 選択直後に KPI・TOP 10・推移グラフ（今週/今月は日別、今年/全期間は月別へ自動切替）・傾向グラフを再集計して即時反映する。
- **よく聴く曲 TOP 10**:
  - 左クリック: 該当楽曲を即時再生（TOP 10 リストを再生コンテキストとして設定）。
  - 右クリックコンテキストメニュー: 「今すぐ再生」「次に再生」「再生キューの最後に追加」「プレイリストに追加...」「アーティスト画面へ移動」。
- **アーティスト / アルバム TOP 10**:
  - タブ切替でアーティスト TOP 10 とアルバム TOP 10 を排他表示。
  - アーティスト行クリックで該当アーティストを選択した状態で `ViewType.Artists` へ遷移。
  - アルバム行クリックで該当アルバム詳細画面（`ViewType.AlbumDetail`）へ遷移。
- **グラフ切替・ホバー**:
  - 推移グラフの「日別 / 月別」切替、傾向グラフの「時間帯別 / 曜日別」切替に即時追従し、各バーへのホバーで詳細期間・再生回数・累計再生時間をツールチップ表示する。

## 7. パフォーマンス最適化要件

- ランキングは最大 10 件、推移グラフは最大 31 点、傾向グラフは最大 24 点の固定少数バケットであるため、軽量 `ItemsControl` とフラット `Border` 描画によりレイアウト計算負荷を最小化する。
- `DropShadowEffect` および `BlurEffect` は一切使用しない。
- アルバムアートサムネイルは `AlbumArtLoader.SourcePath`（LRU キャッシュ＋非同期デコード）で読み込む。
- 画面非表示時（`LibraryStatsViewModel.IsActiveView == false`）は統計更新イベントおよびライブラリ同期イベントを受けてもダーティフラグのみ立てて再集計を遅延し、ビュー復帰時に 1 回のみ最新化する。また連続更新は `Interlocked.Exchange` により 1 フレームへコアレッシングする。
- エンプティ状態とダッシュボード本体は `Visibility="Collapsed"` で排他制御する。

## 8. 関連Issue・仕様書

- 親 Epic: [#452](https://github.com/taketonnbo/Audio_Effector/issues/452)
- 対象 Issue: [#453](https://github.com/taketonnbo/Audio_Effector/issues/453)
- 関連機能仕様書: [ライブラリ全体リスニング統計.md](../../機能仕様/ライブラリ全体リスニング統計.md)
- 関連詳細設計書: [ライブラリ全体リスニング統計.md](../../詳細設計/ライブラリ全体リスニング統計.md)
- 関連 UI 仕様書: [左サイドバーUI仕様書.md](../左サイドバー/左サイドバーUI仕様書.md), [全体レイアウト・ペイン構成UI仕様書.md](../全体レイアウト・ペイン構成/全体レイアウト・ペイン構成UI仕様書.md), [画面遷移・ナビゲーション設計書.md](../画面遷移・ナビゲーション設計書.md), [リスニング統計ダッシュボードUI仕様書.md](../リスニング統計ダッシュボード/リスニング統計ダッシュボードUI仕様書.md)
