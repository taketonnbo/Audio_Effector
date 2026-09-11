# 文字入力ダイアログ UI仕様書

## 1. 概要 (Overview)

プレイリスト名やプリセット名など、呼出元から指定された文字列を入力して返す。

## 2. 実装イメージ（モックアップ）

### 2.1 ASCIIモックアップ

```text
+---------------------------------------------------+
| プレイリスト名の入力                         [X]  |
+---------------------------------------------------+
| 新しいプレイリストの名前を入力してください:       |
|                                                   |
| +-----------------------------------------------+ |
| | My Favorite Tracks 2024                     | | | <- TextBox (シアン枠線)
| +-----------------------------------------------+ |
|                                                   |
|                      [  OK (確定)  ]  [ キャンセル ]|
+---------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト

300×150、WindowStyle=None、ResizeMode=NoResize、起動位置CenterScreen。外枠1、内容StackPanelの余白20。案内文、入力欄、右寄せのOK／Cancelを縦に配置する。

## 4. デザイントークンの適用

| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `WindowBackgroundBrush` | <span style="background-color:#161920;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #161920</span> | `#161920` | 画面背景 |
| `ControlBackgroundBrush` | <span style="background-color:#232934;color:#F0F4F8;padding:2px 8px;border-radius:3px;">■ #232934</span> | `#232934` | 入力・カード背景 |
| `NeonCyanBrush` | <span style="background-color:#00FFFF;color:#161920;padding:2px 8px;border-radius:3px;">■ #00FFFF</span> | `#00FFFF` | 操作アクセント |

入力Padding=5、案内文の下余白10、ボタン列の上余白20。ボタン幅60、間隔10。

## 5. 構成要素と共通コントロール

`Message` は説明、`InputText` は編集文字列。OKはシアン背景、CancelはControlBackgroundHighlightBrush。OKはIsDefault、CancelはIsCancel。選択行の幅3pxバーは対象外。

## 6. アニメーションとインタラクション

入力ごとにUpdateSourceTrigger=PropertyChangedでInputTextへ反映。OKまたはEnterで `DialogResult=True` を返す。CancelまたはEscで取り消す。名前の空白検証・重複確認は呼出元の処理に従う。

## 7. パフォーマンス最適化要件

固定要素のみ。表示要求があるときに生成し、閉じたダイアログは保持しない。UI仮想化、動的エフェクトは対象外。

共通要件は[パフォーマンス最適化](../../詳細設計/パフォーマンス最適化.md)に従う。多数の項目の標準指定は `IsVirtualizing=True / VirtualizationMode=Recycling / ScrollUnit=Pixel / CanContentScroll=True`。上記の個別構成と区別して適用範囲を判断する。

## 8. 関連Issue・仕様書

- [InputBox.xaml](../../../AudioEffector/Presentation/Views/InputBox.xaml)
- [ユースケース](../../ユースケース/03_プレイリスト管理.md)
- [主要オブジェクト一覧](../../主要オブジェクト/index.md)
- [デザイントークン](../../デザインシステム/01_デザイントークン仕様書.md)・[共通コントロール](../../デザインシステム/02_共通コントロールスタイル仕様書.md)
- [コードビハインド](../../../AudioEffector/Presentation/Views/InputBox.xaml.cs)
