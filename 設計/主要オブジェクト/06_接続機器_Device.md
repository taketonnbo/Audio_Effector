# オブジェクト抽出テーブル: 接続機器 (Device)

## 1. 基本情報
- **オブジェクト名**: 接続機器（外部デバイス）
- **英語名**: Device（実装クラス: `MainViewModel.DeviceViewModel`, `DeviceAlbum`, `DeviceTrack`）
- **分類**: セカンダリオブジェクト / ターゲット（ロケーション）オブジェクト
- **責務**: PCに接続されたリムーバブルドライブ（USBメモリ、SDカード等）やMTPポータブルオーディオプレイヤーの内部フォルダ構造・転送済み楽曲を表現し、楽曲転送先およびストレージ管理対象としての役割を担う。

## 2. コレクション / シングル構成

> [!IMPORTANT]
> **コレクション / シングル構成**:
> - **コレクション表示**:
>   - `DeviceSyncView.xaml` 上部の「CONNECTED DEVICE」コンボボックス（`DeviceBrowserViewModel.ConnectedDevices`）。
> - **シングル表示**:
>   - `DeviceSyncView.xaml` の上部ペイン（選択された機器内部のディレクトリ・ファイル一覧 `DeviceDirectories` およびパスナビゲーションバー `CurrentDevicePath`）。
>   - `DeviceManagerDialog.xaml`（選択機器内の転送済みアルバム `DeviceAlbums` および収録曲 `DeviceTrack` の一覧・削除ダイアログ）。

## 3. 属性（プロパティ）定義

| プロパティ名 | 型 | 説明 | 表示箇所 | 状態 | 実装プロパティ ([DeviceBrowserViewModel.cs](../../AudioEffector/Presentation/ViewModels/DeviceBrowserViewModel.cs)) |
| :--- | :--- | :--- | :--- | :---: | :--- |
| **Name** | `string` | デバイス表示名（ドライブレター＋ボリュームラベル、またはMTP機器名） | コンボボックス | 実装済み | `MainViewModel.DeviceViewModel.Name` |
| **ConnectionTypeLabel** | `string` | 接続種別ラベル（`USB` または `MTP`） | 機器詳細ヘッダーバー | 実装済み | `DeviceViewModel.ConnectionTypeLabel` |
| **TotalSpaceBytes / FreeSpaceBytes / UsedSpaceRatio / StorageSummaryText** | `long` / `double` / `string` | 機器の総容量・空き容量・使用率バー値・表示サマリ（取得不可時はフォールバック表示） | 機器詳細ヘッダーバー | 実装済み | `DeviceViewModel.TotalSpaceBytes`, `FreeSpaceBytes`, `UsedSpaceRatio`, `StorageSummaryText` |
| **HasConnectedDevices / SelectedSizeSummaryText / IsInsufficientSpace** | `bool` / `string` | 接続有無・選択楽曲の合計サイズ予測・容量不足判定 | 未接続案内・下部ヘッダー | 実装済み | `DeviceBrowserViewModel.HasConnectedDevices`, `SelectedSizeSummaryText`, `IsInsufficientSpace` |
| **Drive / MtpDevice** | `DriveInfo?` / `MediaDevice?` | ファイルシステムドライブまたはMTPデバイス実体参照 | 内部転送・探索制御 | 実装済み | `DeviceViewModel.Drive`, `DeviceViewModel.MtpDevice` |
| **CurrentDevicePath** | `string` | 機器内の現在閲覧中/転送先フォルダパス | ナビゲーションバー | 実装済み | `DeviceBrowserViewModel.CurrentDevicePath` |
| **DeviceDirectories** | `ObservableCollection<DirectoryItem>` | 現在フォルダ配下のサブフォルダ（`📁`）およびファイル（`📄`）一覧 | 上部ディレクトリリスト | 実装済み | `DeviceBrowserViewModel.DeviceDirectories` |
| **IsTransferring** | `bool` | 転送処理中かどうか（UI操作ロックおよびウィンドウ終了確認と連動） | 転送画面全体 | 実装済み | `DeviceBrowserViewModel.IsTransferring` |
| **TransferProgress** | `double` | 転送進捗率（`0`〜`100`%） | 下部 `Transferring: XX%` 表示 | 実装済み | `DeviceBrowserViewModel.TransferProgress` |
| **DeviceAlbums** | `ObservableCollection<DeviceAlbum>` | デバイス内に保存されているアルバム・楽曲一覧 | `DeviceManagerDialog.xaml` | 実装済み | `DeviceManagerViewModel.DeviceAlbums` ([DeviceManagerViewModel.cs](../../AudioEffector/Presentation/ViewModels/DeviceManagerViewModel.cs)) |
| **DeviceKey / IsCurrentFolderDefault / CurrentDeviceDefaultFolderPath** | `string` / `bool` | 機器識別子と機器ごとの既定転送先フォルダ登録状態（`AppSettings.DeviceDefaultTransferFolders` 永続化） | ナビゲーションバー（`☆ 既定` / `★ 既定`） | 実装済み (#385) | `DeviceViewModel.DeviceKey`, `DeviceBrowserViewModel.IsCurrentFolderDefault`, `CurrentDeviceDefaultFolderPath` |
| **Transcode設定** | 各種 | 転送時フォーマット変換設定 | — | 拡張構想 (#96/#97) | — |

## 4. アクション（操作）定義

| アクション名 | 動詞 | トリガー / 操作 | 影響・結果 | 状態 | 関連コマンド / 実装 |
| :--- | :--- | :--- | :--- | :---: | :--- |
| **機器検出・更新** | Refresh | `ViewType.DeviceSync` への画面切替時 | リムーバブルドライブおよびMTP機器を検出し一覧更新 | 実装済み | `RefreshDrives` ([DeviceBrowserViewModel.cs](../../AudioEffector/Presentation/ViewModels/DeviceBrowserViewModel.cs)) |
| **機器選択** | Select | コンボボックスで対象機器を選択 | 機器ルートディレクトリ読込と転送済みアルバム照合（`CheckDeviceAlbums`） | 実装済み | `SelectedDevice` セッター |
| **フォルダ移動・再読込** | Browse | フォルダ項目のダブルクリック、上部「`..`」（親階層へ）ボタン、「`↻`」ボタン | 選択フォルダへの移動・再読込と重複転送状態（`Album.IsOnDevice`）の再判定 | 実装済み | `NavigateUpCommand`, `RefreshDirectoryCommand` |
| **アルバム/楽曲転送** | Transfer | 下部リストでアルバム/曲を選択し「`TRANSFER SELECTED`」ボタン押下 | `<CurrentDevicePath>\<Artist>\<Album>\<FileName>` へコピー転送（既存同名ファイルは自動スキップ） | 実装済み | `TransferSelectedCommand` ([DeviceBrowserViewModel.cs](../../AudioEffector/Presentation/ViewModels/DeviceBrowserViewModel.cs)), `TransferTracksCommand` ([DeviceSyncViewModel.cs](../../AudioEffector/Presentation/ViewModels/DeviceSyncViewModel.cs)) |
| **機器管理を開く** | Manage | 「`⚙ Manage`」ボタン押下 | `DeviceManagerDialog` を開きデバイス内のアルバム・楽曲一覧を表示（閉じた後に転送画面を再同期） | 実装済み | `OpenDeviceManagerCommand` |
| **端末内アルバム/曲の削除** | Delete on Device | `DeviceManagerDialog` 内のアルバム削除または曲削除ボタン押下 | デバイス上の実ファイルを削除（アルバム内最後の1曲削除時は空フォルダも自動削除） | 実装済み | `DeleteAlbumCommand`, `DeleteTrackCommand` ([DeviceManagerViewModel.cs](../../AudioEffector/Presentation/ViewModels/DeviceManagerViewModel.cs)) |
| **全階層重複スキャン / 曲単位グレーアウト / トランスコード / 既定フォルダ記憶** | 各種 | サブフォルダ全体の重複検出、曲単位の `IsOnDevice` 制御、転送時変換、容量予測バー、キャンセル | より柔軟で安全な同期ワークフロー | 拡張構想 (#96, #97, #169, #170, #233) | — |

## 5. 拡張設計指針
- **重複転送防止の高度化（Issue #169, #170）**: 全サブフォルダを対象とした重複スキャンおよび、アルバム単位だけでなく曲単位での既存ファイル非活性化表示。
- **トランスコード転送（Issue #96, #97）**: FLAC等の大容量音源をポータブル機器向けにMP3/AAC等へ変換しながら転送する機能。
- **端末別既定フォルダ記憶・空き容量予測バー（Issue #233）**: 端末ごとの転送先フォルダ記憶、リアルタイム空き容量予測、常時接続ドライブの除外設定。
