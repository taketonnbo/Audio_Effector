# オブジェクト抽出テーブル: 再生キュー (PlayQueue)

## 1. 基本情報
- **オブジェクト名**: 再生キュー
- **英語名**: PlayQueue
- **分類**: コンテナオブジェクト / ワークスペースオブジェクト
- **責務**: 現在再生中および次に再生される楽曲の動的な順序付きリストを保持し、割り込み再生・順序変更・削除などのリアルタイム制御を提供する。

## 2. コレクション / シングル構成

> [!IMPORTANT]
> **実装仕様 (As-Is)**:
> - **コレクション表示**: `PlayQueueSidePanel.xaml`
>   - メインウィンドウ右端に最前面オーバーレイ（`Panel.ZIndex="30"`）として配置され、ツールバーの開閉ボタン操作で右からスライドイン表示される専用サイドパネル。
>   - パネル内に楽曲の縦スクロールリストが表示される。
> - **シングル表示**:
>   - キュー内の各楽曲行。現在再生中の楽曲はハイライトされ、再生/一時停止の波形アイコン等が表示される。

## 3. 属性（プロパティ）定義

| プロパティ名 | 型 | 説明 | コレクション表示 | 実装プロパティ ([PlayerControlViewModel.cs](file:///c:/Users/tnish/vs_code_git/Audio_Effector/AudioEffector/Presentation/ViewModels/PlayerControlViewModel.cs)) |
| :--- | :--- | :--- | :---: | :--- |
| **PlayQueue** | `ObservableCollection<Track>` | キューに含まれる楽曲リスト | ○ | `PlayerControlViewModel.PlayQueue` |
| **CurrentTrack** | `Track?` | 現在再生中の楽曲オブジェクト | ○ (ハイライト) | `PlayerControlViewModel.CurrentTrack` |
| **PreloadedTrack** | `Track?` | ギャップレス再生用に事前デコード待機中の次曲 | - | オーディオエンジン連携 |
| **IsPlayQueuePanelOpen**| `bool` | パネルの開閉状態 | - | `MainViewModel.IsPlayQueuePanelOpen` |
| **QueueCount** | `int` | キュー内の曲数 | ○ (ヘッダー表示) | `PlayQueue.Count` |

## 4. アクション（操作）定義

| アクション名 | 動詞 | トリガー / 操作 | 影響・結果 | 関連コマンド / 実装 |
| :--- | :--- | :--- | :--- | :--- |
| **開閉トグル** | Toggle | ツールバーのキューアイコンボタン押下 | サイドパネルのスライド開閉アニメーション実行 | `TogglePlayQueuePanelCommand` ([MainViewModel.cs](file:///c:/Users/tnish/vs_code_git/Audio_Effector/AudioEffector/Presentation/ViewModels/MainViewModel.cs)) |
| **パネルを閉じる** | Close | パネル右上の「✕」ボタン押下 | サイドパネルを右端へ収納 | `ClosePlayQueuePanelCommand` |
| **指定曲から再生** | Play At | キュー行のアートまたはタイトルボタンクリック | 該当インデックスから再生開始 | `PlayFromQueueCommand` |
| **再生順の並び替え** | Reorder | キュー行のドラッグ＆ドロップ操作 | キュー内の順序をリアルタイム入れ替え（Undo対応） | `PlayQueueSidePanel.xaml.cs` |
| **1曲削除** | Remove | キュー行右端の「✕」ボタン、または右クリック「キューから削除」 | 該当楽曲をキューから除外（Undo対応） | `RemoveFromQueueCommand` |
| **キューの全クリア** | Clear All | パネルヘッダーの「Clear」ボタン押下 | キューを空にして再生停止（誤操作時Undo対応） | `ClearQueueCommand` |
| **ギャップレス遷移** | Gapless Next | 曲終了検知による自動トリガー | プリロード済み次曲へ無音なくシームレスに切り替え | オーディオサービス制御 |

## 5. OOUI設計上の課題と今後の指針 (To-Be)
- **ライブラリからのダイレクト投入**: ライブラリ画面（全曲、アルバム、プレイリスト）から楽曲を選択し、開いているキューパネルへ直接ドラッグ＆ドロップして任意位置へ挿入できるインタラクションの強化。
- **ギャップレス再生の保証**: 次曲の事前デコードとシームレスなバッファ切り替えにより、トラック境界でのノイズや途切れをゼロにする再生基盤の連携。
- **キュー操作のUndo対応**: 「Clear」ボタンや誤ったドラッグ順序変更を行った際に、`Ctrl+Z` で直前のキュー状態へ即座に復元できる安全設計の導入。

