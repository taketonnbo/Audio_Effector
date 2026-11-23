# オブジェクト抽出テーブル: 再生キュー (PlayQueue)

## 1. 基本情報
- **オブジェクト名**: 再生キュー
- **英語名**: PlayQueue
- **分類**: コンテナオブジェクト / ワークスペースオブジェクト
- **責務**: 次に再生される未再生楽曲の2層リスト（予約キュー `UserQueue` ＋ アルバムの残り曲 `AlbumQueue`）およびセッション再生履歴（`PlayHistory`）を保持し、割り込み再生・シャッフル局所化・削除・クリアのリアルタイム制御を提供する。

## 2. コレクション / シングル構成

> [!IMPORTANT]
> **コレクション / シングル構成**:
> - **コレクション表示**:
>   1. `PlayQueueSidePanel.xaml`: メインウィンドウ右端に最前面オーバーレイとして配置され、開閉ボタン操作で右からスライドイン表示される専用サイドパネル（非表示時は `Visibility="Collapsed"`）。「再生リスト」タブ（`UserQueue` セクション＋`AlbumQueue` セクション）と「履歴」タブ（`PlayHistory`、インメモリ最大100件）の2タブ構成。
>   2. `MiniPlayerWindow.xaml`（下部連結キュー領域）: ミニプレイヤー下部に一体型として延長展開される軽量 `ListBox`（UI仮想化対応、格納時は `Visibility="Collapsed"`）。旧独立ダイアログ `PlayQueueDialog` は Issue #217 にて撤廃・統合済み。
> - **シングル表示**:
>   - 現在再生中の楽曲（`CurrentTrack`）は未再生キュー一覧からは除外され、メイン画面右側パネルまたは上部プレイヤーバーに表示される。

## 3. 属性（プロパティ）定義

| プロパティ名 | 型 | 説明 | コレクション表示 | 実装プロパティ ([PlayerControlViewModel.cs](../../AudioEffector/Presentation/ViewModels/PlayerControlViewModel.cs)) |
| :--- | :--- | :--- | :---: | :--- |
| **UserQueue** | `ObservableCollection<Track>` | ユーザーが「次に再生」等で予約した優先キュー | ○ (`予約キュー` セクション) | `PlayerControlViewModel.UserQueue`, `IAudioService.UserQueue` |
| **AlbumQueue** | `ObservableCollection<Track>` | 現在再生中アルバム/リストの後続未再生トラック群（シャッフル対象） | ○ (`アルバムの残り曲` セクション) | `PlayerControlViewModel.AlbumQueue`, `IAudioService.AlbumQueue` |
| **PlayQueue** | `ObservableCollection<Track>` | `UserQueue` と `AlbumQueue` を連結した未再生トラック全体リスト | ○ | `PlayerControlViewModel.PlayQueue` |
| **PlayHistory** | `ObservableCollection<Track>` | 再生終了・スキップした楽曲のインメモリ履歴（最新順、上限100件） | ○ (`履歴` タブ) | `PlayerControlViewModel.PlayHistory` |
| **CurrentTrack** | `Track?` | 現在再生中の楽曲オブジェクト | - (プレイヤー部に表示) | `PlayerControlViewModel.CurrentTrack` |
| **SelectedQueueTabIndex** | `int` | 選択中タブ（`0`: 再生リスト / `1`: 履歴） | ○ | `PlayerControlViewModel.SelectedQueueTabIndex` |
| **HasUserQueue / HasAlbumQueue** | `bool` | 各セクションの表示有無フラグ | ○ | `PlayerControlViewModel.HasUserQueue`, `HasAlbumQueue` |
| **AlbumQueueTitle** | `string` | アルバム残り曲セクションの見出し（`次のアルバムから: <名>` 等） | ○ | `PlayerControlViewModel.AlbumQueueTitle` |
| **IsPlayQueuePanelOpen** | `bool` | スライドパネルの開閉状態 | - | `MainViewModel.IsPlayQueuePanelOpen` |
| **IsMiniPlayerQueueOpen** | `bool` | ミニプレイヤー下部連結キューの開閉状態 | - | `PlayerControlViewModel.IsMiniPlayerQueueOpen` / `MainViewModel.IsMiniPlayerQueueOpen` |

## 4. アクション（操作）定義

| アクション名 | 動詞 | トリガー / 操作 | 影響・結果 | 状態 | 関連コマンド / 実装 |
| :--- | :--- | :--- | :--- | :---: | :--- |
| **パネル開閉トグル** | Toggle | プレイヤー部のキューアイコンボタン押下 | 右側スライドパネルの開閉アニメーション実行 | 実装済み | `TogglePlayQueuePanelCommand` ([MainViewModel.cs](../../AudioEffector/Presentation/ViewModels/MainViewModel.cs)) |
| **パネルを閉じる** | Close | パネル右上の閉じるボタン押下 | サイドパネルを右端へ格納し `Collapsed` 化 | 実装済み | `ClosePlayQueuePanelCommand` |
| **タブ切り替え** | Switch Tab | 「再生リスト」「履歴」タブボタン押下 | 表示タブを切り替え（`0` / `1`） | 実装済み | `SelectQueueTabCommand` ([PlayerControlViewModel.cs](../../AudioEffector/Presentation/ViewModels/PlayerControlViewModel.cs)) |
| **キュー内指定曲の再生** | Play From Queue | キュー内アイテムのクリック、または右クリック「再生」 | 該当トラックを即時再生しキューから除外 | 実装済み | `PlayFromQueueCommand` |
| **履歴から再生** | Play From History | 履歴タブ内アイテムのクリック、または右クリック「再生」 | 該当トラックを即時再生し、自動的に「再生リスト」タブ（`0`）へ切替 | 実装済み | `PlayFromHistoryCommand` |
| **予約キューのみクリア** | Clear User Queue | 予約キュー見出しの「予約をクリア」ボタン押下 | `AlbumQueue` を維持したまま `UserQueue` のみを全消去 | 実装済み | `ClearUserQueueCommand` |
| **1曲削除（キュー/履歴）** | Remove | 各行の「✕」ボタン、または右クリック「キューから削除」「履歴から削除」 | 該当トラックをキューまたは履歴から除外 | 実装済み | `RemoveFromQueueCommand`, `RemoveFromHistoryCommand` |
| **現在タブの全クリア** | Clear Current Tab | パネルヘッダー右上のゴミ箱アイコン押下 | 選択中タブに応じてキュー全消去（＋再生停止）または履歴全消去 | 実装済み | `ClearCurrentTabCommand` (`ClearQueueCommand` / `ClearHistoryCommand`) |
| **D&D並び替え** | Reorder | キュー行のドラッグ＆ドロップ操作 | セクション内並び替えおよび `AlbumQueue` → `UserQueue` 昇格移動を `IAudioService` と即時同期 | 実装済み | `MoveUserQueueItem` / `MoveAlbumQueueItem` / `MoveAlbumQueueItemToUserQueue` ([PlayQueueSidePanel.xaml.cs](../../AudioEffector/Presentation/Views/PlayQueueSidePanel.xaml.cs)) |
| **ミニプレイヤーキュー開閉** | Toggle / Close | ミニプレイヤーのキューボタンまたは右クリックメニュー操作 | ミニプレイヤー下部にキュー領域を延長展開（100px ↔ 350px）または格納 | 実装済み | `ToggleMiniPlayerQueueCommand`, `CloseMiniPlayerQueueCommand` ([PlayerControlViewModel.cs](../../AudioEffector/Presentation/ViewModels/PlayerControlViewModel.cs)) |

## 5. 拡張設計指針
- **ミニプレイヤー連結キューの機能拡張**: 必要に応じてミニプレイヤー側でも予約キュー／アルバム残り曲のセクション表示や並び替え操作へ拡張可能。
