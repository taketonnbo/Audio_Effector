# 再生キュースライドパネル UI仕様書

## 1. 概要 (Overview)

メイン画面右端にキューと再生履歴を表示し、予約順の確認・再生・削除を行う。対象はPlayQueueとTrack。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+------------------------------------+
| 再生キュー (Play Queue)        [X] |
| 12 曲 (48 分)         [🗑 全クリア] |
+------------------------------------+
| [ 再生キュー (12) ] |  再生履歴 (25) |
+------------------------------------+
| ▼ 現在再生中 (Now Playing)         |
| +--------------------------------+ |
| | > [Art 36] Midnight City       | |
| |            M83 - 01:23 / 04:03 | |
| +--------------------------------+ |
|                                    |
| ▼ 次に再生 (Next in Queue)         |
| +--------------------------------+ |
| | ≡ [Art 36] Starboy        03:50| |
| |            The Weeknd     [✕]  | |
| +--------------------------------+ |
| | ≡ [Art 36] Get Lucky      06:09| |
| |            Daft Punk      [✕]  | |
| +--------------------------------+ |
| | ≡ [Art 36] Instant Crush  05:37| |
| |            Daft Punk      [✕]  | |
| +--------------------------------+ |
| | ≡ [Art 36] Lose Yourself  05:53| |
| |            Daft Punk      [✕]  | |
| +--------------------------------+ |
| | ...                            | |
+------------------------------------+
  ^ ドラッグ＆ドロップで並び替え (≡)
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

UserControl幅360、MainWindow内のホストも幅360、右揃え、ZIndex=30。パネル本体はヘッダー `Auto`、キュー／履歴タブ `Auto`、本文 `*` の3行。非表示位置へのTranslateTransform.Xは380。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 画面背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主テキスト |
| `AlwaysNeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | テーマ共通アクセント |

タブ高36、選択インジケーター高2。ヘッダー操作ボタン28×28、曲アート36×36。曲名13/SemiBold、アーティスト11、時間mm:ss。

## 5. 構成要素と共通コントロール

キュー／履歴の件数を表示し、`SelectedQueueTabIndex=0/1` に連動。キューはUserQueue（手動予約）とAlbumQueue（アルバム由来）を別セクションで表示する。履歴はPlayHistory。上部は現在タブの消去と閉じる。空キューは案内文を表示する。

タブの選択は下端2pxインジケーター。曲行の幅3px選択バーは指定しない。キューや履歴の行末には削除操作を置く。

## 6. アニメーションとインタラクション

開く際はVisibleにして350ms CubicEase/EaseOut、閉じる際は300ms CubicEase/EaseInで移動し、完了後CollapsedとIsHitTestVisible=Falseへ変更する。タブは即時切替。キュー行は再生と削除、履歴行は再生／次に再生／最後に再生／履歴削除などの右クリック操作を持つ。消去ボタンは選択中タブを対象にする。

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
