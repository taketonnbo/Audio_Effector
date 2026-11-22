# 機器転送 UI仕様書

## 1. 概要 (Overview)

接続機器の転送先を選び、PC側のAlbum／Trackをコピーする。対象はDevice、Album、Track。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
【通常状態 (転送対象選択時)】
+--------------------------------------------------------------------------------------------------------------------+
| 接続機器: [ WALKMAN NW-A100 (E:\) ▼ ]   空き容量: [======--------] 24.5 GB / 64.0 GB      [⚙ ストレージ管理]       |
+--------------------------------------------------------------------------------------------------------------------+
| [機器側エクスプローラー] パス: E:\Music\                                            [.. 上へ] [↻ 更新]             |
| +----------------------------------------------------------------------------------------------------------------+ |
| | [📁] Hi-Res       [📁] Pop & Rock    [📁] Soundtracks   [📁] Unsorted                                          | |
| +----------------------------------------------------------------------------------------------------------------+ |
|=================================== 分割スプリッター (上下ドラッグでリサイズ) =====================================|
| [PC側ライブラリから転送するアルバム / 楽曲を選択]                    選択中: 3 アルバム, 38 曲 (2.1 GB / 空き十分) |
| +----------------------------------------------------------------------------------------------------------------+ |
| | [✓] [Art 40] Random Access Memories - Daft Punk (13曲, 680MB)                               [v 収録曲を展開]   | |
| | [✓] [Art 40] Starboy - The Weeknd (18曲, 420MB)                                             [v 収録曲を展開]   | |
| | [ ] [Art 40] Currents - Tame Impala (13曲, 510MB)                                           [v 収録曲を展開]   | |
| | [✓] [Art 40] Hurry Up, We're Dreaming - M83 (7曲選択 / 22曲中, 310MB)                       [^ 収録曲を閉じる] | |
| |     |-- [✓] 01. Intro (05:22)        |-- [✓] 02. Midnight City (04:03)    |-- [ ] 03. Reunion (03:55)          | |
| +----------------------------------------------------------------------------------------------------------------+ |
| [ ▶ 選択した楽曲を機器へ転送 (38曲 / 2.1 GB) ]                                                                    |
+--------------------------------------------------------------------------------------------------------------------+

【転送実行中 (Transferring)】
+--------------------------------------------------------------------------------------------------------------------+
| 転送中: 14 / 38 曲 (42%) - "Midnight City.flac" をコピーしています...                                              |
| [====================================------------------------------------------------] 42% (残り時間: 約1分20秒)  |
|                                                                                                  [✕ 転送を中止]    |
+--------------------------------------------------------------------------------------------------------------------+
```

> **構想要素に関する注記**: モックアップ内の空き容量バー・選択サマリ・転送プログレスバー・残り時間・転送中止ボタンは拡張構想（関連Issue: #96, #97, #169, #170, #233）です。`DeviceSyncView.xaml` では下部ステータスは `"Transferring: {TransferProgress:0}%"` テキストとボタン文言切り替え（`"TRANSFER SELECTED"` / `"TRANSFERRING..."`）で表示されます。

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`CurrentViewType = DeviceSync`、`DataContext` は `MainViewModel.DeviceBrowser`（`DeviceBrowserViewModel`）。行0が機器側エクスプローラー `1*`、行1が高さ `5px` の `GridSplitter` `Auto`、行2がPC側アルバム/楽曲選択一覧 `1*`、行3が下部進捗テキストおよび転送開始ボタン `Auto` の4行構成。上下の領域配分を `GridSplitter` のドラッグで調整できる。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | 入力・カード・リスト背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主テキスト |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | アーティスト名・パス表示 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 見出し・転送ボタンアクセント |

機器 `ComboBox` と `"⚙ Manage"` ボタンは高さ `30px`、上へ（`..`）／更新（`↻`）ボタンは `30×25px`、転送ボタンは高さ `50px`。アルバム行アートは `40×40px`、名称 `14pt`、アーティスト `11pt`。

## 5. 構成要素と共通コントロール

- **上部（機器側エクスプローラー）**: `"CONNECTED DEVICE"` 見出し、接続機器 `ComboBox`（リムーバブルドライブおよびMTPデバイス両対応）、`"⚙ Manage"` ボタン、親フォルダへ移動 `".."`、現在パス `CurrentDevicePath`、再読込 `"↻"`、および `DeviceDirectories`（サブフォルダ `📁` とファイル `📄` の一覧）を表示する。
- **下部（PC側選択リスト）**: `"SELECT ALBUMS / TRACKS TO TRANSFER"` 見出し、`Albums` の `Expander` と `CheckBox`（展開時は配下 `Tracks` の `CheckBox`）。
- **重複防止とロック**: 転送先（`<CurrentDevicePath>\<Artist>\<Album>\<FileName>`）に全曲存在するアルバムは `Album.IsOnDevice = True` となり選択 `CheckBox` が無効化される（転送実行時にも既存同名ファイルは自動スキップ）。転送中（`IsTransferring = True`）はナビゲーション・選択・転送ボタンをロックし、ボタン文言を `"TRANSFERRING..."` に切り替えて進捗率 `Transferring: XX%` を更新する。

## 6. アニメーションとインタラクション

- 画面遷移時（`ViewType.DeviceSync`）に自動で `RefreshDrives()` を実行し、スペクトラムアナライザを非表示（`IsSpectrumVisible = false`）に格納する。
- 機器内フォルダ（`📁`）のダブルクリックで下位階層へ移動、`".."` ボタンは `NavigateUpCommand`、`"↻"` ボタンは `RefreshDirectoryCommand`、`"⚙ Manage"` ボタンは `OpenDeviceManagerCommand`（`DeviceManagerDialog` 表示）を実行する。
- アルバム/楽曲にチェックを入れて `"TRANSFER SELECTED"`（`TransferSelectedCommand`）を押下すると転送を開始する。転送中にメインウィンドウを閉じようとした場合は中断確認 `MessageBox` を表示する。

## 7. パフォーマンス最適化要件

PC側アルバム一覧 `ListBox` には `VirtualizingStackPanel.IsVirtualizing="True"` および `VirtualizationMode="Recycling"` を指定する（機器ディレクトリ一覧および展開トラック `ItemsControl` は標準描画）。

## 8. 関連Issue・仕様書

- [DeviceSyncView.xaml](../../../AudioEffector/Presentation/Views/DeviceSyncView.xaml)
- [ユースケース](../../ユースケース/05_機器転送・同期.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [機器ストレージ管理UI仕様書](../機器ストレージ管理/機器ストレージ管理UI仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/DeviceSyncView.xaml.cs)
- [DeviceBrowserViewModel](../../../AudioEffector/Presentation/ViewModels/DeviceBrowserViewModel.cs)
