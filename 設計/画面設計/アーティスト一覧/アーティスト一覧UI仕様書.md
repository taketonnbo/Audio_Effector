# アーティスト一覧 UI仕様書

## 1. 概要 (Overview)

ライブラリ内の楽曲をアーティスト単位で集約し、2ペイン構成でブラウズ・アルバム/楽曲へのドリルダウン・一括再生を行う画面。

- **主要操作対象オブジェクト**: アーティスト (`Track.Artist` および所属 `Album` 集約)
- **ユーザー体験**: 左ペインでの高速なアーティスト探索と、右ペインでの詳細ヘッダー・所属アルバム・全楽曲一覧の即時確認および一括再生。

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
|                           |   4   ☆  Harder, Better, Faster, Stronger      Discovery                        03:44   |
+---------------------------+----------------------------------------------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

---

## 3. 画面配置とペインレイアウト

- **位置付け**: MainWindowの中央ワークスペース列（`CurrentViewType=Artists`）。
- **グリッド構造**:
  - 行0 (`Auto`): ツールバー（ソートComboBox「名前順 / 曲数順 / アルバム数順」、昇降順トグル、検索ボックス＋クリアボタン、アーティスト総数サマリー）。
  - 行1 (`*`): 2ペインブラウザー領域
    - 列0 (`260px`, `MinWidth=200px`): 左ペイン（仮想化アーティスト一覧。Empty State 時は `Visibility="Collapsed"`）。
    - 列1 (`*`): 右ペイン（選択アーティスト詳細ヘッダー、所属アルバム横スクロールリスト、楽曲一覧）。未選択時は `Visibility="Collapsed"`。
    - Empty State (`Grid.ColumnSpan="2"`): アーティスト 0 件時に `WindowBackgroundBrush` 背景付きで中央表示。
- **サイズ要件**: ワークスペースの幅・高さに100%追従。

---

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 画面背景・Empty State背景 |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | ツールバー・左ペイン・詳細ヘッダー背景 |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | 検索ボックス・セカンダリボタン・アルバムカード背景 |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | ホバー・選択行ハイライト |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 選択アクセントバー、プライマリボタン背景、再生中タイトル |
| `InverseTextForegroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920 / #FFFFFF</span> | Dark `#161920` / Light `#FFFFFF` | プライマリボタン（「▶ すべて再生」）文字色 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | アーティスト名・曲名・アルバム名 |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | アルバム数、曲数サマリー、再生時間 |

- **角丸・クリッピング**:
  - 左ペインアバター (36x36): `CornerRadius="18"` + `<EllipseGeometry Center="17,17" RadiusX="17" RadiusY="17"/>`
  - 詳細ヘッダーアバター (56x56): `CornerRadius="28"` + `<EllipseGeometry Center="26.5,26.5" RadiusX="26.5" RadiusY="26.5"/>`
  - アルバムカードサムネイル (40x40): `CornerRadius="4"` + `<RectangleGeometry Rect="0,0,40,40" RadiusX="4" RadiusY="4"/>`

---

## 5. 構成要素と共通コントロール

1. **左ペイン（アーティスト一覧）**:
   - 選択アクセントバー、円形アバター画像（代表トラックのアルバムアート）、アーティスト名、所属アルバム数・楽曲数サマリー。
   - UI仮想化（`IsVirtualizing="True"`, `VirtualizationMode="Recycling"`, `ScrollUnit="Pixel"`, `CanContentScroll="True"`）。
2. **右ペイン（アーティスト詳細）**:
   - **詳細ヘッダー**: 円形アバター、アーティスト名、サマリー、「▶ すべて再生」プライマリボタン、「シャッフル」セカンダリボタン。
   - **所属アルバム横スクロールリスト**: 仮想化水平 `ListBox`、角丸ジャケット、アルバム名、収録曲数（クリックでアルバム再生）。
   - **楽曲一覧**: 仮想化 `ListBox`、連番、お気に入りトグルボタン（`☆`/`★`）、曲名（再生中ハイライト）、アルバム名、再生時間。

---

## 6. アニメーションとインタラクション

- **クリック**:
  - 左ペイン行クリック: アーティスト選択および右ペイン詳細の切り替え（`SelectedTrack` はリセット）。
  - アルバムカードクリック: 該当アルバムの全曲を再生（`PlayAlbumCommand`）。
- **ダブルクリック（左ボタンかつアイテム行上のみ有効、ボタン・スクロールバー・余白は除外）**:
  - 左ペインアーティスト行: アーティストの全楽曲を再生開始（`PlayArtistCommand`）。
  - 右ペイン楽曲行: アーティスト全曲コンテキストで対象楽曲を即時再生（`PlayTrackCommand`）。
- **右クリック（コンテキストメニュー）**:
  - アーティスト行: 「アーティスト全曲を再生」「シャッフル再生」「次に再生」「最後に再生（キューに追加）」
  - 楽曲行: 「再生」「次に再生」「最後に再生（キューに追加）」「お気に入りに追加 / 解除」「プレイリストに追加...」「プロパティ」「ファイルの場所を開く」「ライブラリから削除」
- **今後の拡張タスク（後続 Issue 対象・未実装）**:
  - アーティスト単位の「端末へ転送」メニュー
  - アーティスト行／カードの右側再生キューパネルへのドラッグ＆ドロップ (D&D)

---

## 7. パフォーマンス最適化要件

- ライブラリ変更時の連続 `CollectionChanged` イベントを `DispatcherPriority.Background` でコアレッシングし、`Tracks.Count > 0` の通常経路では `Albums.CollectionChanged` による再構築を抑止。
- アーティスト集約結果をキャッシュし、検索・ソート変更時はキャッシュに対してのみフィルタ・ソートを実行（検索入力には 200ms デバウンスを適用）。
- 全リスト（アーティスト一覧・所属アルバム横リスト・楽曲一覧）で UI 仮想化 4 属性を適用。
- 詳細ペイン非選択時および Empty State 時は不要領域を `Visibility="Collapsed"` とし、動的エフェクト（BlurEffect/DropShadowEffect）を排除。

---

## 8. 関連Issue・仕様書

- [主要オブジェクト仕様: 楽曲 (`Track`)](../../主要オブジェクト/01_楽曲_Track.md)
- [主要オブジェクト仕様: アルバム (`Album`)](../../主要オブジェクト/02_アルバム_Album.md)
- [インタラクション標準化ガイドライン](../../主要オブジェクト/インタラクション標準化ガイドライン.md)
- [ユースケース: 楽曲ライブラリ管理](../../ユースケース/02_楽曲ライブラリ管理.md)
- [デザイントークン仕様書](../../デザインシステム/01_デザイントークン仕様書.md)
- [共通コントロールスタイル仕様書](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
