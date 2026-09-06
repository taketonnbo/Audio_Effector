# 再生キュースライドパネル UI仕様書

## 1. 概要
本機能は、従来独立したウィンドウダイアログ（`PlayQueueDialog`）として提供されていた「再生キュー」画面を、メイン画面の右側から滑らかにスライドアウトしてオーバーレイ表示されるサイドパネルUI（`PlayQueueSidePanel`）に変更するものです。

これにより、ユーザーは楽曲ライブラリのブラウジングやイコライザー調整、アルバム詳細の閲覧など、メイン画面での操作を中断することなく、シームレスに再生キューの確認や編集（曲の削除・並び替え・追加）を行うことができ、大幅なUX向上を実現します。

なお、ミニプレイヤー（`MiniPlayerWindow`）から呼び出される再生キューについては、ミニプレイヤーのコンパクトな画面特性を維持するため、従来通り独立ダイアログ（`PlayQueueDialog`）による表示を継続します。

## 2. 実装イメージ（モックアップ）
以下は、メイン画面の右側から再生キュースライドパネルが展開（スライドイン）した状態のイメージです。

![実装イメージ](images/playqueue_slideout_mockup.jpg)

## 3. レイアウトとデザイン

### パネル全体構成
- **配置場所**: メインウィンドウ（`MainWindow`）のメインクライアント領域（Row 1）右端にオーバーレイ配置（`HorizontalAlignment="Right"`, `Panel.ZIndex="30"`）。
- **サイズ**: 幅 360px、高さはウィンドウに連動（Stretch）。
- **背景・境界線**: 
  - 背景: `{DynamicResource WindowBackgroundBrush}`
  - 左境界線: 幅 1px、`{DynamicResource ControlBorderBrush}`（メイン領域との境界を明確に区切る）。

### セクション構成
1. **ヘッダー領域**:
   - **タイトル**: 「再生キュー」を水色ネオン色（`{DynamicResource AlwaysNeonCyanBrush}`）かつ太字で表示。ライト/ダーク共通で鮮明な水色ハイライトを維持。
   - **キュー件数表示**: 現在キューに含まれている楽曲数を `(3)` のように補助テキストで併記。
   - **キュー全クリアボタン**: ゴミ箱アイコン（Tooltip: 「再生キューをすべてクリア」）。クリックで `ClearQueueCommand` を実行。
   - **閉じるボタン**: 右端に「×」アイコン（Tooltip: 「再生キューを閉じる」）。クリックで `ClosePlayQueuePanelCommand` を実行してパネルを格納。

2. **キューリスト領域**:
   - `ScrollViewer` 内に `PlayQueue` を `ItemsControl` で縦方向に一覧表示。
   - **空状態（Empty State）**: キューが0件の場合は、音符アイコンと「再生キューは空です」「楽曲を右クリックして『再生キューに追加』できます」というガイドメッセージを中央に淡く表示。
   - **各トラックアイテムカード**:
     - **アルバムアートサムネイル**: 36x36px（角丸 4px）。クリックで即時再生。
     - **再生中インジケータ**: 再生中の楽曲には半透明黒オーバーレイ（#80000000）＋水色ネオンハイライト（`AlwaysNeonCyanBrush`）の再生/一時停止アイコンを重畳表示（ライト/ダーク共通で鮮明な視認性を確保）。
     - **楽曲メタデータ**:
       - トラック名: 13pt、セミボールド、白色、長文時は末尾三点リーダー（Ellipsis）。クリックで即時再生。
       - アーティスト名: 11pt、セカンダリグレー、長文時Ellipsis。
     - **再生時間**: `mm:ss` 形式で右側に表示。
     - **インライン操作ボタン**:
       - **キューから削除（×）**: クリックで `RemoveFromQueueCommand` を実行し、キューから除外。
     - **右クリックコンテキストメニュー**:
       - 再生 / 停止
       - キューから削除
       - お気に入りに追加 / 解除
       - プレイリストに追加...
       - プロパティ
       - ファイルの場所を開く

## 4. アニメーションとインタラクション

### パネル開閉アニメーション
- **展開時（Slide-in）**:
  - メインウィンドウ上の「Play Queue」ボタン（コンパクトプレイヤー、マキシマイズプレイヤー、ミニマライズプレイヤー）をクリックすると `TogglePlayQueuePanelCommand` が発火し、`IsPlayQueuePanelOpen` が `True` に切り替わります。
  - パネルの `RenderTransform.X` が `380`（画面外）から `0`（画面内）へ、`0.35秒` の `DoubleAnimation`（`CubicEase EasingMode="EaseOut"`）で滑らかにスライドインします。
  - パネル展開と同時に `IsHitTestVisible` が `True` に設定され、パネル内の各種操作が可能になります。
- **格納時（Slide-out）**:
  - パネル右上の閉じるボタン（×）、または再度「Play Queue」ボタンをクリックすると `IsPlayQueuePanelOpen` が `False` に切り替わります。
  - パネルの `RenderTransform.X` が `0` から `380` へ、`0.30秒` の `DoubleAnimation`（`CubicEase EasingMode="EaseIn"`）で滑らかにスライドアウトします。
  - 格納後は `IsHitTestVisible` が `False` に切り替わるため、背後のライブラリ操作を阻害しません。
  - メインクライアント領域の `ClipToBounds="True"` により、画面外に退避したパネルがウィンドウ外に不自然にはみ出して描画されることを完全に防止しています。

### ホバー・クリック挙動
- リストの各アイテムカードにマウスホバーすると、背景が `{DynamicResource ControlBackgroundBrush}` にハイライトされ、クリック可能であることを示します。
- 操作ボタン（▲、▼、×、ゴミ箱）にホバーすると、各ボタンの背景がハイライトされます。
- 曲の並び替え・削除操作時は、`ObservableCollection<Track>` の更新と同時に `IAudioService.SetPlaylist` を同期呼び出しするため、再生順序や次曲の自動遷移に即座に反映されます。

## 5. 関連Issue
- [Issue #66: [UI変更] 再生キュー画面の右側スライドアウト化](https://github.com/taketonnbo/Audio_Effector/issues/66)
- [Issue #51: [UI変更] アプリ画面表示の調整対応 (Epic)](https://github.com/taketonnbo/Audio_Effector/issues/51)
