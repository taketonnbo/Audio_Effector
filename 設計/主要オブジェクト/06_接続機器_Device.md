# オブジェクト抽出テーブル: 接続機器 (Device)

## 1. 基本情報
- **オブジェクト名**: 接続機器（外部デバイス）
- **英語名**: Device
- **分類**: セカンダリオブジェクト / ターゲット（ロケーション）オブジェクト
- **責務**: PCに接続されたポータブルオーディオプレイヤーやUSBストレージの接続状態・容量・内部フォルダ構造を表現し、転送先ターゲットとしての役割を担う。

## 2. コレクション / シングル構成

> [!IMPORTANT]
> **実装仕様 (As-Is)**:
> - **コレクション表示**:
>   - `DeviceSyncView.xaml` 上部の「CONNECTED DEVICE」コンボボックス（`RemovableDrives`）。
>   - `DeviceManagerDialog.xaml` 内の接続ドライブ一覧リスト。
> - **シングル表示**:
>   - `DeviceSyncView.xaml` の上部ペイン（選択された機器内部のフォルダブラウザおよびナビゲーションバー）。
>   - 機器内の各フォルダ階層をドリルダウン閲覧可能。

## 3. 属性（プロパティ）定義

| プロパティ名 | 型 | 説明 | 表示箇所 | 実装プロパティ ([DeviceBrowserViewModel.cs](file:///c:/Users/tnish/vs_code_git/Audio_Effector/AudioEffector/Presentation/ViewModels/DeviceBrowserViewModel.cs)) |
| :--- | :--- | :--- | :---: | :--- |
| **Name / DisplayName** | `string` | デバイス名（ボリューム名等） | コンボボックス、機器ダイアログ | `RemovableDrive.Name` |
| **RootPath** | `string` | ルートドライブパス（例：`E:\`） | コンボボックス内部データ | `RemovableDrive.RootPath` |
| **CurrentDirectory** | `string` | 機器内の現在閲覧中フォルダパス | ナビゲーションバー | `DeviceBrowserViewModel.CurrentDirectory` |
| **Directories** | `ObservableCollection<DeviceDirectoryItem>` | 機器内のフォルダ一覧 | 上部フォルダリスト | `DeviceBrowserViewModel.Directories` |
| **TotalSpace** | `long` | 機器ストレージ総容量 (バイト) | 機器ダイアログ、容量バー | ドライブ情報 |
| **FreeSpace** | `long` | 機器の現在空き容量 (バイト) | 機器ダイアログ、容量バー | ドライブ情報 |
| **ProjectedFreeSpace** | `long` | 転送後の予測空き容量 (バイト) | リアルタイム容量バー | 選択アイテム合計とFreeSpaceから算出 |
| **HasCapacityWarning** | `bool` | 空き容量不足警告フラグ | 警告メッセージ、赤色バー表示 | 予測容量不足時にtrue |
| **IsTransferring** | `bool` | 転送処理中かどうか | 進捗プログレスバーの表示制御 | `DeviceBrowserViewModel.IsTransferring` |

## 4. アクション（操作）定義

| アクション名 | 動詞 | トリガー / 操作 | 影響・結果 | 関連コマンド / 実装 |
| :--- | :--- | :--- | :--- | :--- |
| **機器選択** | Select | コンボボックスで対象機器を選択 | 機器内部のルートディレクトリ読込 | `SelectedDevice` セッター |
| **フォルダ移動** | Browse | フォルダ一覧項目のダブルクリック | 選択フォルダへ下位ドリルダウン | 内部ナビゲーション |
| **容量予測と警告** | Predict Capacity | 転送対象アルバム/楽曲の選択変更時 | 転送後空き容量をリアルタイム計算し、不足時は警告表示 | 容量計算ロジック |
| **アルバム転送** | Transfer | 下部アルバムのチェックボックス選択後、「TRANSFER TO DEVICE」ボタン押下、またはD&D | 選択アルバムの機器転送タスク開始（容量不足時は抑止） | `TransferCommand` |
| **転送キャンセル** | Cancel | 転送中の「Cancel」ボタン押下 | ファイル転送処理の中止 | `CancelTransferCommand` |
| **機器管理を開く** | Manage | 「⚙ Manage」ボタン押下 | `DeviceManagerDialog` モーダル表示 | `ShowDeviceManagerCommand` |

## 5. OOUI設計上の課題と今後の指針 (To-Be)
- **サイドバーのロケーション化**: 独立した専用転送画面（2ペイン分割画面）ではなく、左サイドバーに接続機器をマウント表示し、通常のライブラリ画面から直接ドラッグ＆ドロップして転送できる直感的な操作感を導入する。
- **リアルタイム空き容量予測と安全な転送**: 楽曲やアルバムのドラッグ中やチェック選択時に、機器の空き容量バーがリアルタイムに伸縮・警告表示（オーバー時は赤色表示）され、容量不足による転送失敗を直感的に防止する。

