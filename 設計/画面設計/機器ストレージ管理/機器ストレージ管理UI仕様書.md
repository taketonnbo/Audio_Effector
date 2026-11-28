# 機器ストレージ管理 UI仕様書

## 1. 概要 (Overview)

接続機器内のアルバムと楽曲を確認し、不要なデータを削除する。対象はDevice、Album、Track。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+---------------------------------------------------------------------------------------------------+
| 機器ストレージ管理 (Device Storage Manager)                                                  [X]  |
+---------------------------------------------------------------------------------------------------+
| 対象機器: WALKMAN NW-A100 (E:\)                                                                   |
| 容量内訳: [■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■]  |
|           ■ 音楽: 32.4 GB (51%)   ■ その他: 7.1 GB (11%)   □ 空き: 24.5 GB (38%) / 全体: 64.0 GB |
| 端末内コンテンツ: 1,420 楽曲 / 98 アルバム                                   [↻ 再スキャン]       |
+---------------------------------------------------------------------------------------------------+
| +-----------------------------------------------------------------------------------------------+ |
| | [▼] [Art 40] Random Access Memories (2013) - Daft Punk                  (13曲, 680MB) [🗑 削除] | |
| |   |-- 01. Give Life Back to Music (04:34, 45MB)                                     [🗑 削除] | |
| |   |-- 02. The Game of Love (05:22, 52MB)                                            [🗑 削除] | |
| |   |-- 03. Giorgio by Moroder (09:04, 91MB)                                          [🗑 削除] | |
| |   `-- ...                                                                                     | |
| +-----------------------------------------------------------------------------------------------+ |
| | [>] [Art 40] Starboy (2016) - The Weeknd                                (18曲, 420MB) [🗑 削除] | |
| +-----------------------------------------------------------------------------------------------+ |
| | [>] [Art 40] Currents (2015) - Tame Impala                              (13曲, 510MB) [🗑 削除] | |
| +-----------------------------------------------------------------------------------------------+ |
+---------------------------------------------------------------------------------------------------+
|                                                                                [ 閉じる (Close) ] |
+---------------------------------------------------------------------------------------------------+
```

> **構想要素に関する注記**: モックアップ内の容量内訳バー・総数サマリー・再スキャンボタン・アルバムアート表示は拡張構想（関連Issue: #233, #254）です。`DeviceManagerDialog.xaml` では幅 `300px` のアルバム `Expander` カードを `WrapPanel` で折り返し表示する構成となります。

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

ウィンドウタイトル `"Device Storage Manager"`、`800×600px`、`WindowStartupLocation="CenterOwner"`。行0がヘッダー（`Auto`：`"Installed Albums on Device"`）、行1が一覧領域（`*`）、行2が `"Close"` ボタン（`Auto`）の3行構成。各アルバム `Expander` は幅 `300px` で `WrapPanel` 内に折り返し配置される。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | ダイアログ背景 |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | アルバムカード・ボタン背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 主テキスト |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | アーティスト名 |
| `MutedTextForegroundBrush` | <span style="background-color:#718096;color:#161920;padding:2px 8px;border-radius:3px;">■ #718096</span> | `#718096` | 曲数（`{Tracks.Count} tracks`） |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 見出し・プログレスバーアクセント |

`"Installed Albums on Device"` 見出し `24pt / Bold`、アルバム名 `14pt / Bold`、アーティスト名 `12pt`、曲数 `10pt`。`"Close"` ボタンは `100×30px`、削除ボタンはアルバム `30×30px`、曲 `24×24px`。

## 5. 構成要素と共通コントロール

- デバイス走査中（`IsLoading = True`）は `"Scanning device..."` と不定進捗バー（`ProgressBar IsIndeterminate="True"`）を表示する。
- 走査完了後は `DeviceAlbums`（`DeviceAlbum`）の名称（`Title`）、アーティスト名（`Artist`）、収録曲数（`{Tracks.Count} tracks`）、およびアルバム削除ボタンを表示する（アルバムアート画像は表示しない）。
- `Expander` 展開時は配下の `DeviceTrack` 一覧（表示名は実ファイル名）と曲単位の削除ボタンを表示する。各アルバム内の楽曲は、PC側ライブラリ情報と一致するトラック番号（未設定を除く）を優先したうえで、ファイル名の自然順（数値昇順・大文字小文字非区別フォールバック）で整列して表示する。

## 6. アニメーションとインタラクション

- アルバム `Expander` の展開で収録曲一覧を表示する。
- **アルバム削除 (`DeleteAlbumCommand`)**: 確認 `MessageBox` を表示した上でデバイス上のアルバムフォルダを削除し、親アーティストフォルダが空になった場合はあわせて削除する。
- **楽曲削除 (`DeleteTrackCommand`)**: デバイス上の対象楽曲ファイルを削除する。アルバム内の最後の1曲を削除した場合は、確認ダイアログなしで親アルバムフォルダ（および空になったアーティストフォルダ）も自動削除し、`DeviceAlbums` から該当アルバムカードを除去する。
- **ダイアログ終了時連携**: `"Close"` ボタンでダイアログを閉じた後、呼び出し元の `DeviceBrowserViewModel` が現在フォルダ（既定フォルダ遷移後・機器ルート含む）の再読み込み・重複インデックス（`.m4a`/`.mp3` 等の拡張子変換後ファイル、アルバム代表アーティスト配置・旧曲別アーティスト配置・末尾ピリオド正規化済みフォルダ名を含む）の再構築および接続機器の空き容量情報更新を実行し、転送画面側の曲単位・アルバム単位の `IsOnDevice` グレーアウト状態・ディレクトリ表示・容量表示を最新化する。

## 7. パフォーマンス最適化要件

外側ScrollViewer内のItemsControlとWrapPanel、収録曲ItemsControlはUI仮想化を行わない。読込中と一覧の表示条件は `IsLoading` に従う。大規模コレクションの共通仮想化条件と異なる構成として記録する。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [DeviceManagerDialog.xaml](../../../AudioEffector/Presentation/Views/DeviceManagerDialog.xaml)
- [ユースケース](../../ユースケース/05_機器転送・同期.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/DeviceManagerDialog.xaml.cs)
- [DeviceManagerViewModel](../../../AudioEffector/Presentation/ViewModels/DeviceManagerViewModel.cs)
