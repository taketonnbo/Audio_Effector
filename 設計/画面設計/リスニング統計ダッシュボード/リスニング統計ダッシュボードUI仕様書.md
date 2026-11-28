# リスニング統計ダッシュボード UI仕様書

## 1. 概要 (Overview)

メイン画面右側パネル下部拡張領域の 3 タブ（`歌詞` / `ダッシュボード` / `タグ`）のうち `ダッシュボード` タブに表示し、対象楽曲の累計統計（再生回数・累計時間・最終再生日時）と対象楽曲が所属するアルバム内の「アルバム内でよく聴く曲 TOP 5」を確認する軽量パネルの UI 仕様を定義する。

- **主要対象オブジェクト**: [楽曲 (`Track`)](../../主要オブジェクト/01_楽曲_Track.md)、`TrackListeningStat`、`TrackPlayEvent`
- **配置コンポーネント**: `AudioEffector/Presentation/Views/ListeningStatsView.xaml`（`DataContext`: `MainViewModel.ListeningStats`）

---

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+-----------------------------------------------------------------------------------------+
| [Art 50] Midnight City / M83                                                     [🗖]   |
|          [Hi-Res] FLAC 96kHz/24bit                                                      |
|   [🔀] [|<] [ ▶ ] [>|] [🔁] [≡]   01:23 =======O========== 04:03        [+] [★]        |
+-----------------------------------------------------------------------------------------+
|         歌詞          |      ダッシュボード      |          タグ                         |
|                       +========================+                                        |
+-----------------------------------------------------------------------------------------+
| +-------------------------------------------------------------------------------------+ |
| | [選択中の曲を表示中]                                           [ 再生中の曲に戻す ]  | |
| | Midnight City                                                                       | |
| | M83  •  Hurry Up, We're Dreaming                                                    | |
| +-------------------------------------------------------------------------------------+ |
| +-------------------------+  +-------------------------+  +-------------------------+   |
| | 累計再生回数             |  | 累計再生時間             |  | 最終再生日時             |   |
| | 12 回                   |  | 48分 36秒                |  | 2026/11/29 14:30        |   |
| +-------------------------+  +-------------------------+  +-------------------------+   |
| +-------------------------------------------------------------------------------------+ |
| | アルバム内でよく聴く曲 TOP 5                             Hurry Up, We're Dreaming   | |
| | #1  Midnight City               M83                                       12 回     | |
| |     [==========================================================================]    | |
| | #2  Wait                        M83                                        9 回     | |
| |     [=======================================================                   ]    | |
| | #3  Intro                       M83                                        6 回     | |
| |     [=====================================                                     ]    | |
| +-------------------------------------------------------------------------------------+ |
+-----------------------------------------------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

---

## 3. 画面配置とペインレイアウト

- **位置付け**: `MainWindow.xaml` の右側パネル下部拡張領域（`Grid.Row="1"`）内、3 タブバー（高さ `34px`）直下のコンテンツ領域へ `LyricsView`・`LrcGeneratorView`・タグパネルと排他配置する。
- **可視性連動**: `IsDashboardViewVisible`（`IsRightPanelOpen && !IsAlbumViewMaximized && ActiveRightExtensionPanel == RightExtensionPanelType.Dashboard`）を `BoolToVis` でバインドし、非表示時は `Visibility="Collapsed"` とする。
- **内部レイアウト**: 単一 `ScrollViewer` 内に以下の 3 セクションを縦積み配置する。
  1. 対象楽曲ヘッダーカード（再生中以外の曲を表示時のみバッジ＋「再生中の曲に戻す」ボタン行を表示）
  2. KPI サマリカード（`UniformGrid Columns="3"`：累計再生回数 / 累計再生時間 / 最終再生日時）
  3. アルバム内でよく聴く曲 TOP 5 カード（見出し＋対象アルバム名、`ItemsControl`＋アルバム未記録／対象曲未選択エンプティメッセージ切替）

---

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `PanelBackgroundBrush` | <span style="background-color:#1B2028;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #1B2028</span> | `#1B2028` | パネル背景・3 タブバー背景・プログレスバー下地 |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | ヘッダーカード・KPI カード・TOP 5 カード背景 |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | タブ・TOP 5 行ホバー背景・選択曲バッジ背景 |
| `BorderBrush` | <span style="background-color:#343E4E;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #343E4E</span> | `#343E4E` | カード枠線・タブバー境界線 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | アクティブタブ下線・累計再生回数強調・順位・プログレスバー前景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 曲名・KPI 値・セクション見出し |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | アーティスト・アルバム・非選択タブ文字 |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | KPI ラベル・エンプティ案内テキスト |
| `InverseTextForegroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | セカンダリボタンホバー時反転文字 |

- `DarkTheme.xaml` / `LightTheme.xaml` の両テーマへ `{DynamicResource ...}` で動的追従する。

---

## 5. インタラクション

- **3 タブ切替**: 3 タブバーの「歌詞」「ダッシュボード」「タグ」ボタン（`SelectRightExtensionTabCommand`）で下部拡張パネルを即時排他切替する。
- **プレイリスト右クリック遷移**: 右側トラックリストの右クリックメニュー「ダッシュボードを表示」（`OpenTrackDashboardCommand`）で選択曲を対象に右側タブを開きダッシュボードを表示する。
- **再生中の曲へ戻す**: 再生中以外の曲を表示中は「再生中の曲に戻す」ボタン（`ReturnToPlayingTrackCommand`）で現在再生中の楽曲へ即座に復帰する。
- **TOP 5 行クリック**: ランキング行をクリック（`SelectTopTrackCommand`）すると、同アルバム内のその楽曲を対象曲として KPI サマリをプレビュー表示し、同アルバム内のランキング表示を維持する。
- **アルバム切替追従**: 再生曲切替・右側プレイリスト右クリック・再生中曲復帰で別アルバムの楽曲へ切り替わった場合、ランキングカードの見出し横アルバム名と TOP 5 一覧が新しい対象アルバム内へ即座に更新される。

---

## 6. パフォーマンス最適化要件

- `DropShadowEffect` および `BlurEffect` は一切使用せず、軽量なフラット `Border` と `ProgressBar`（`Mode=OneWay` 明示）で描画する。
- 非表示時は `Visibility="Collapsed"` とし、かつ `ListeningStatsViewModel.IsActiveView == false` の間に届いた統計更新はダーティフラグで遅延集計して次回アクティブ化時に反映する。

---

## 7. 関連Issue・仕様書

- [ListeningStatsView.xaml](../../../AudioEffector/Presentation/Views/ListeningStatsView.xaml) / [ListeningStatsViewModel.cs](../../../AudioEffector/Presentation/ViewModels/ListeningStatsViewModel.cs)
- [機能仕様書: リスニング統計ダッシュボード](../../機能仕様/リスニング統計ダッシュボード.md)
- [詳細設計書: リスニング統計ダッシュボード](../../詳細設計/リスニング統計ダッシュボード.md)
- [右側アルバム情報表示サイズ切り替え機能UI仕様書](../右側アルバム情報表示サイズ切り替え機能/右側アルバム情報表示サイズ切り替え機能UI仕様書.md)
- [最近再生した曲UI仕様書](../最近再生した曲/最近再生した曲UI仕様書.md)
- 関連 Issue: [#121](https://github.com/taketonnbo/Audio_Effector/issues/121), [#123](https://github.com/taketonnbo/Audio_Effector/issues/123), [#470](https://github.com/taketonnbo/Audio_Effector/issues/470)
