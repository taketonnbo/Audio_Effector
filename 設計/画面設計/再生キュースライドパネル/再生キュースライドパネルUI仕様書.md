# 再生キュースライドパネル UI仕様書

## 1. 概要 (Overview)

メイン画面右端にキューと再生履歴を表示し、予約順の確認・再生・削除を行う。対象はPlayQueueとTrack。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+------------------------------------+
| 再生キュー               [🗑]  [X] |
+------------------------------------+
| [ 再生リスト (10) ] |   履歴 (12)  |
+------------------------------------+
| 予約キュー (2)       [予約をクリア] |
| +--------------------------------+ |
| | [Art 36] Starboy          03:50| |
| |          The Weeknd       [✕]  | |
| +--------------------------------+ |
| | [Art 36] Get Lucky        06:09| |
| |          Daft Punk        [✕]  | |
| +--------------------------------+ |
|                                    |
| 次のアルバムから: Random... 残り8曲|
| +--------------------------------+ |
| | [Art 36] Instant Crush    05:37| |
| |          Daft Punk        [✕]  | |
| +--------------------------------+ |
| | [Art 36] Lose Yourself    05:53| |
| |          Daft Punk        [✕]  | |
| +--------------------------------+ |
| | ...                            | |
+------------------------------------+
```

> **構想要素に関する注記**: キュー内アイテムのドラッグ＆ドロップによる再生順並び替えは拡張構想（関連Issue: #216）です。また、再生リスト内には現在再生中の楽曲は含めず、これから再生される未再生楽曲のみを2セクション（「予約キュー」「アルバムの残り曲」）に分けて表示します。

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`UserControl` 幅 `360px`、`MainWindow` 内のホストも幅 `360px`、右揃え、`Panel.ZIndex="30"`。パネル本体は行0がヘッダー（`Auto`：「再生キュー」見出し＋右端に現在タブ全クリアのゴミ箱アイコン `28×28` と閉じる `X` アイコン `28×28`）、行1がタブバー（`Auto`：「`再生リスト ({PlayQueue.Count})`」と「`履歴 ({PlayHistory.Count})`」）、行2がコンテンツ領域（`*`）の3行構成。格納時の `TranslateTransform.X` は `380`。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | パネル背景 |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | セクションヘッダー背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主テキスト |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | 非選択タブ・再生時間・空状態テキスト |
| `AlwaysNeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | アクティブタブ文字・下端2pxインジケーター・「予約をクリア」文字 |

タブ高 `36px`、選択インジケーター高 `2px`。ヘッダー操作ボタン `28×28px`、曲アート `36×36px`。曲名 `13pt / SemiBold`、アーティスト `11pt`、時間 `mm:ss`。

## 5. 構成要素と共通コントロール

- **タブ0「再生リスト」 (`SelectedQueueTabIndex == 0`)**:
  - **セクション1「予約キュー (`UserQueue`)」**: `HasUserQueue == True` 時のみ表示。ヘッダー左に `予約キュー ({UserQueue.Count})`、右端に「`予約をクリア`」ボタン（`ClearUserQueueCommand`）を配置。
  - **セクション2「アルバムの残り曲 (`AlbumQueue`)」**: `HasAlbumQueue == True` 時のみ表示。ヘッダー左に `AlbumQueueTitle`（`次のアルバムから: {PlaybackListName}`、未選択時は `アルバムの残り曲`）、右端に `残り {AlbumQueue.Count} 曲` を表示。
  - **空状態表示**: `PlayQueue.Count == 0` の場合、「再生キューは空です」「楽曲を右クリックして「最後に再生」または「次に再生」できます」を表示。
- **タブ1「履歴」 (`SelectedQueueTabIndex == 1`)**:
  - `PlayHistory`（`IAudioService.TrackPlaybackEnded` 発火時に先頭追加、同一曲は先頭へ再配置、インメモリ最大 `100` 件）を仮想化 `ListBox` で表示。
  - **空状態表示**: `PlayHistory.Count == 0` の場合、「再生履歴はありません」「再生が終了した楽曲がここに表示されます」を表示。

## 6. アニメーションとインタラクション

- **パネル開閉**: 開く際は即座に `Visibility = Visible`・`IsHitTestVisible = True` にして `350ms` `CubicEase` (`EaseOut`) でスライドイン。閉じる際は `300ms` `CubicEase` (`EaseIn`) でスライドアウトし、アニメーション完了イベント（`Completed`）で `Visibility = Collapsed`・`IsHitTestVisible = False` へ変更する。
- **再生リスト操作**: アイテムクリックまたは右クリック「再生」で `PlayFromQueueCommand`、行末「✕」または右クリック「キューから削除」で `RemoveFromQueueCommand`。「予約をクリア」で予約キューのみ消去。
- **履歴操作**: アイテムクリックまたは右クリック「再生」で `PlayFromHistoryCommand`（対象曲を再生開始し、自動的にタブ0「再生リスト」へ切り替え）。右クリック「次に再生」「最後に再生」「履歴から削除」（`RemoveFromHistoryCommand`）に対応。
- **現在タブ全クリア**: ヘッダー右上のゴミ箱ボタン（`ClearCurrentTabCommand`）は、タブ0選択時は `ClearQueue()`（全キュー消去＋再生停止）、タブ1選択時は `ClearHistory()`（履歴全消去）を実行する。

## 7. パフォーマンス最適化要件

履歴ListBoxにはIsVirtualizing=True、Recycling、Pixel、CanContentScroll=Trueを指定。キューのセクションはScrollViewer内のItemsControlでUI仮想化を行わない。閉状態はコードビハインドでCollapsed。動的な影やぼかしを曲行に追加しない。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [PlayQueueSidePanel.xaml](../../../AudioEffector/Presentation/Views/PlayQueueSidePanel.xaml)
- [ユースケース](../../ユースケース/01_楽曲再生・再生制御.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/PlayQueueSidePanel.xaml.cs)
- [PlayerControlViewModel](../../../AudioEffector/Presentation/ViewModels/PlayerControlViewModel.cs)
- 関連Issue（設計経緯）: [#211](https://github.com/taketonnbo/Audio_Effector/issues/211)、[#217](https://github.com/taketonnbo/Audio_Effector/issues/217)、[#225](https://github.com/taketonnbo/Audio_Effector/issues/225)
