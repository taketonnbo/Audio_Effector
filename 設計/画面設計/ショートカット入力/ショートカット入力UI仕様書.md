# ショートカット入力 UI仕様書

## 1. 概要 (Overview)

設定画面内で1つの機能にキーと修飾キーを割り当てる。対象はShortcutKeyConfig。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
【通常状態 (割当済み)】
+-----------------------------------------------+
| 再生 / 一時停止:                              |
| +-------------------------------------------+ |
| | Space                                     | |
| +-------------------------------------------+ |
+-----------------------------------------------+

【キー入力フォーカス時 (待機中)】
+-----------------------------------------------+
| 次の曲:                                       |
| +-------------------------------------------+ |
| | キーを入力してください...                 | | <- 半透明ブルー背景で待機強調
| +-------------------------------------------+ |
+-----------------------------------------------+
  (ToolTip: クリックしてショートカットキーを入力 / BackSpaceまたはDeleteでクリア)

【OS・他アプリとのホットキー競合警告表示時】
+-----------------------------------------------+
| 音量を上げる:                                 |
| +-------------------------------------------+ |
| | Ctrl + Up                                 | |
| +-------------------------------------------+ |
| ⚠️ このショートカットは他のアプリで既に使われています。 |
+-----------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

設定画面（`SettingsDialog`）の「ショートカット」カテゴリ内に縦積み配置する `UserControl`。`Label`（機能名）、幅 `250px` の読み取り専用 `TextBox`（`InputTextBox`）、および競合警告 `TextBlock`（`WarningTextBlock`、通常は `Collapsed`）の3要素で構成する。下余白 `15px`、入力 `Padding="5"`、警告の上余白 `5px`。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `SecondaryTextForegroundBrush` | <span style="background-color:#A0AEC0;color:#161920;padding:2px 8px;border-radius:3px;">■ #A0AEC0</span> | `#A0AEC0` | 機能名ラベル |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | 通常時の入力欄背景 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 入力欄テキスト |
| `BorderBrush` | <span style="background-color:#343E4E;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #343E4E</span> | `#343E4E` | 入力欄ボーダー |

ラベルは `SecondaryTextForegroundBrush`、警告文は `12pt`・オレンジ（`#FFAA00`）。フォーカス待機中の入力欄背景は半透明ブルー（`ARGB(30, 0, 120, 215)`）に切り替わる。

## 5. 構成要素と共通コントロール

- `Label` と `Shortcut`（`ShortcutKeyConfig`、`BindsTwoWayByDefault`）の依存関係プロパティに連動する。
- 割当済みは `Ctrl` / `Shift` / `Alt` / `Win` ＋ キー名、未割当は `"割り当てなし"`、フォーカス待機中は `"キーを入力してください..."` を表示する。
- 操作ヒント（「クリックしてショートカットキーを入力 (BackSpaceまたはDeleteでクリア)」）は `InputTextBox.ToolTip` として提供する。

## 6. アニメーションとインタラクション

- クリックで入力欄へフォーカスし入力待機状態に移行。修飾キー単体の押下は無視し、`Delete` または `BackSpace` で割り当てをクリア（`Key.None`）する。
- **3段階の検証と警告**:
  1. **WPF `KeyBinding` 非対応キー組合せ**: エラー `MessageBox` を表示して割り当てを拒否する。
  2. **アプリ内他機能との重複**: 重複警告 `MessageBox` を表示して割り当てを拒否する（`WarningTextBlock` は表示しない）。
  3. **OS／他アプリとの競合検知**: Win32 API `RegisterHotKey` / `UnregisterHotKey` によるテスト登録を行い、失敗した場合に `WarningTextBlock`（「⚠️ このショートカットは他のアプリで既に使われています。」）を `Visible` にして注意喚起する。
- フォーカスを外すと現在の `Shortcut` の表示文字列へ戻す。

## 7. パフォーマンス最適化要件

固定3要素の単一入力コントロールであるためコレクションのUI仮想化は対象外。警告文は競合検知時のみ `Visibility="Visible"`（通常は `Collapsed`）とし、`RegisterHotKey` による競合検査はキー入力確定時のみ実行して常時ポーリングを行わない。

## 8. 関連Issue・仕様書

- [ShortcutInputBox.xaml](../../../AudioEffector/Presentation/Views/ShortcutInputBox.xaml)
- [ユースケース](../../ユースケース/06_設定・カスタマイズ.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [設定UI仕様書](../設定/設定UI仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/ShortcutInputBox.xaml.cs)
