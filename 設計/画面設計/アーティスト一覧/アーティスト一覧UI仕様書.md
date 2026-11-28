# アーティスト一覧 UI仕様書

## 1. 概要 (Overview)

ライブラリ内の楽曲をアーティスト単位（`ArtistGroup`）で集約し、2ペイン構成でブラウズ・所属アルバム/楽曲の探索・一括再生を行う画面。

- **主要操作対象オブジェクト**: アーティスト (`ArtistGroup` による `Track.Artist` および所属 `Album` 集約)
- **ユーザー体験**: 左ペインでの高速なアーティスト探索と、右ペインでの詳細ヘッダー・所属アルバム横スクロール一覧・全所属楽曲一覧の即時確認および再生。

---

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+--------------------------------------------------------------------------------------------------------------------+
| [名前順 ▼] [▲]   [アーティストを検索...                        ✕]                                 全 64 アーティスト |
+---------------------------+----------------------------------------------------------------------------------------+
| | ( 👤 ) Daft Punk        | ( 👤 ) Daft Punk                                         [▶ すべて再生] [シャッフル]   |
| |        4 アルバム • 68 曲 |        4 アルバム • 68 曲                                                               |
|                           +----------------------------------------------------------------------------------------+
|   ( 👤 ) M83              | アルバム                                                                               |
|          3 アルバム • 45 曲 |  [Art] Random Access Memories  [Art] Discovery        [Art] Homework                   |
|                           |        13 曲                         14 曲                    16 曲                     |
|   ( 👤 ) The Weeknd       +----------------------------------------------------------------------------------------+
|          5 アルバム • 82 曲 | 楽曲一覧（ダブルクリックで再生）                                                       |
|                           |   1   ★  Get Lucky                           Random Access Memories           06:09   |
|   ( 👤 ) Tame Impala      |   2   ☆  Instant Crush                       Random Access Memories           05:37   |
|          4 アルバム • 54 曲 |   3   ★  One More Time                       Discovery                        05:20   |
+---------------------------+----------------------------------------------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

---

## 3. 画面配置とペインレイアウト

- **位置付け**: `MainWindow` の中央ワークスペース列（`CurrentViewType = Artists`、`DataContext = MainViewModel.Artists`）。
- **グリッド構造**:
  - 行0 (`Auto`): ツールバー（ソート `ComboBox`「名前順 / 曲数順 / アルバム数順」、昇降順トグル、検索ボックス＋クリア `✕` ボタン、アーティスト総数サマリー `全 N アーティスト`）。
  - 行1 (`*`): 2ペインブラウザー領域
    - 列0 (`260px`, `MinWidth=200px`): 左ペイン（仮想化アーティスト一覧。Empty State 時は `Visibility="Collapsed"`）。
    - 列1 (`*`): 右ペイン（選択アーティスト詳細ヘッダー、所属アルバム横スクロールリスト、所属楽曲一覧）。未選択時は `Visibility="Collapsed"`。
    - Empty State (`Grid.ColumnSpan="2"`): 表示対象アーティストが 0 件の際に中央表示（「表示するアーティストがありません」）。
- **サイズ要件**: ワークスペースの幅・高さに100%追従。

---

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 画面背景・Empty State背景 |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | ツールバー・左ペイン・詳細ヘッダー背景 |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | 検索ボックス・セカンダリボタン・アルバムカード背景 |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | ホバー・選択行ハイライト |
| `BorderBrush` | <span style="background-color:#343E4E;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #343E4E</span> | `#343E4E` | ペイン間境界線・ツールバー境界線 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 選択アクセントバー（3px）、プライマリボタン背景、再生中タイトル |
| `InverseTextForegroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920 / #FFFFFF</span> | Dark `#161920` / Light `#FFFFFF` | プライマリボタン（「▶ すべて再生」）文字色 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | アーティスト名・曲名・アルバム名 |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | 楽曲行の所属アルバム名等 |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | アルバム数、曲数サマリー、再生時間 |

- **角丸・アバター形状**:
  - 左ペインアバター (`36×36px`) および詳細ヘッダーアバター (`56×56px`): 円形クリッピング（代表トラックのアルバムアートを非同期読込）。
  - アルバムカードサムネイル (`40×40px`): 角丸 `4px`。

---

## 5. 構成要素と共通コントロール

1. **集約・検索・ソート仕様 (`ArtistsViewModel`)**:
   - アーティスト名またはアルバム名が空白の楽曲は `"Unknown Artist"` / `"Unknown Album"` に正規化し、大文字小文字を区別せずに集約する。
   - 各アーティスト内の所属アルバムはリリース年降順→タイトル昇順、所属楽曲はアルバム順→トラック番号昇順→タイトル昇順に整列する。
   - 検索ボックスは **アーティスト名・所属アルバム名・所属曲名** の3項目を横断検索（200msデバウンス）し、再構築時は前回選択していたアーティストを維持（該当がない場合は先頭を自動選択）する。また、リスニング統計ビューからの遷移時は `SelectArtistByName` により検索フィルタを解除して対象アーティストを選択状態にする。
2. **左ペイン（アーティスト一覧）**:
   - 左端3px選択アクセントバー、円形アバター画像、アーティスト名、`N アルバム • M 曲` サマリー。
3. **右ペイン（アーティスト詳細）**:
   - **詳細ヘッダー**: 円形アバター、アーティスト名、サマリー、「▶ すべて再生」プライマリボタン、「シャッフル」セカンダリボタン。
   - **所属アルバム横スクロールリスト**: 仮想化水平 `ListBox`、角丸ジャケット、アルバム名、収録曲数（クリックで該当アルバムを再生）。
   - **楽曲一覧**: 仮想化 `ListBox`、連番、お気に入りトグルボタン（`☆`/`★`）、曲名（再生中ハイライト）、アルバム名、再生時間。

---

## 6. アニメーションとインタラクション

- **クリック**:
  - 左ペイン行クリック: アーティスト選択および右ペイン詳細の切り替え（`SelectedTrack` はリセット）。
  - アルバムカードクリック: 該当アルバムの全曲を再生（`PlayAlbumCommand`）。
- **ダブルクリック（左ボタンかつアイテム行上のみ有効、子ボタン・スクロールバー・余白は除外）**:
  - 左ペインアーティスト行: アーティストの全楽曲を再生開始（`PlayArtistCommand`）。
  - 右ペイン楽曲行: アーティスト全曲コンテキストで対象楽曲を即時再生（`PlayTrackCommand`）。
- **右クリック（コンテキストメニュー）**:
  - アーティスト行: 「アーティスト全曲を再生」「シャッフル再生」「次に再生」「最後に再生」
  - 楽曲行: 「再生」「次に再生」「最後に再生」「お気に入りに追加 / 解除」「プレイリストに追加...」「プロパティ」「ファイルの場所を開く」「削除」
- **拡張構想（関連Issue #233）**:
  - アーティスト単位の端末転送および再生キューパネルへのドラッグ＆ドロップ投入。

---

## 7. パフォーマンス最適化要件

- ライブラリ変更時の連続 `CollectionChanged` イベントを `DispatcherPriority.Background` でコアレッシングし、`Tracks.Count > 0` の通常経路では `Albums.CollectionChanged` による重複再構築を抑止。
- アーティスト集約結果をキャッシュし、検索・ソート変更時はキャッシュに対してのみフィルタ・ソートを実行（検索入力には 200ms デバウンスを適用）。
- 全リスト（アーティスト一覧・所属アルバム横リスト・楽曲一覧）で UI 仮想化 4 属性（`IsVirtualizing="True"`, `VirtualizationMode="Recycling"`, `ScrollUnit="Pixel"`, `CanContentScroll="True"`）を適用。
- 詳細ペイン非選択時および Empty State 時は不要領域を `Visibility="Collapsed"` とし、動的エフェクトを排除。

---

## 8. 関連Issue・仕様書

- [ArtistsView.xaml](../../../AudioEffector/Presentation/Views/ArtistsView.xaml) / [ArtistsViewModel.cs](../../../AudioEffector/Presentation/ViewModels/ArtistsViewModel.cs)
- [主要オブジェクト仕様: 楽曲 (`Track`)](../../主要オブジェクト/01_楽曲_Track.md)
- [主要オブジェクト仕様: アルバム (`Album`)](../../主要オブジェクト/02_アルバム_Album.md)
- [インタラクション標準化ガイドライン](../../主要オブジェクト/インタラクション標準化ガイドライン.md)
- [ユースケース: 楽曲ライブラリ管理](../../ユースケース/02_楽曲ライブラリ管理.md)
- [デザイントークン仕様書](../../デザインシステム/01_デザイントークン仕様書.md)
- [共通コントロールスタイル仕様書](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
