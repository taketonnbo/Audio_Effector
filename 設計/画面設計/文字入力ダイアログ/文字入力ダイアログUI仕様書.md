# 文字入力ダイアログ UI仕様書

## 1. 概要 (Overview)

プレイリスト名やプリセット名など、呼出元から指定された文字列を入力して返す。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+---------------------------------------------------+
| 新しいプレイリストの名前を入力してください:       |
|                                                   |
| +-----------------------------------------------+ |
| | My Favorite Tracks 2024                     | | | <- TextBox (Loaded時に全選択フォーカス)
| +-----------------------------------------------+ |
|                                                   |
|                                [  OK  ]  [Cancel] |
+---------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

`300×150px`、`WindowStyle="None"`（タイトルバーなし）、`ResizeMode="NoResize"`、`WindowStartupLocation="CenterScreen"`。外枠 `1px`（`BorderBrush`）、内部 `StackPanel` の余白 `20px`。案内文（`Message`）、テキスト入力欄（`InputTextBox`）、右寄せの `"OK"`／`"Cancel"` ボタン列を縦に配置する。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | ダイアログ背景 |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | 入力欄背景 |
| `ControlBackgroundHighlightBrush` | <span style="background-color:#2F3746;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #2F3746</span> | `#2F3746` | Cancelボタン背景 |
| `BorderBrush` | <span style="background-color:#343E4E;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #343E4E</span> | `#343E4E` | ダイアログ外枠・入力欄枠線 |
| `TextForegroundBrush` | <span style="background-color:#F0F4F8;color:#161920;padding:2px 8px;border-radius:3px;">■ #F0F4F8</span> | `#F0F4F8` | 案内文・入力文字・Cancel文字 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | OKボタンアクセント |

入力 `Padding="5"`、案内文の下余白 `10px`、ボタン列の上余白 `20px`。ボタン幅 `60px`、間隔 `10px`。

## 5. 構成要素と共通コントロール

`Message` は案内テキスト、`InputText` は編集文字列。`"OK"` ボタンは `IsDefault="True"`、`"Cancel"` ボタンは `IsCancel="True"`。

## 6. アニメーションとインタラクション

- ダイアログの `Loaded` 時に自動で `InputTextBox.Focus()` および `SelectAll()` を実行し、即座にタイピング・上書き入力できる状態にする。
- ウィンドウ内の左ボタンドラッグで `DragMove()` によるウィンドウ移動が可能。
- 入力ごとに `UpdateSourceTrigger=PropertyChanged` で `InputText` へ反映し、`"OK"` または `Enter` で `DialogResult = true` を返す。`"Cancel"` または `Esc` で `DialogResult = false` として取り消す。

## 7. パフォーマンス最適化要件

固定要素のみのモーダルダイアログであるためコレクションのUI仮想化および動的エフェクトは対象外。表示要求時にインスタンス生成し、閉じた後は保持しない。

## 8. 関連Issue・仕様書

- [InputBox.xaml](../../../AudioEffector/Presentation/Views/InputBox.xaml)
- [ユースケース](../../ユースケース/03_プレイリスト管理.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/InputBox.xaml.cs)
