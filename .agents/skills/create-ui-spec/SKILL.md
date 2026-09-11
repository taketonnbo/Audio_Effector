---
name: create-ui-spec
description: >-
  Use this skill when the user asks you to create a UI specification document (UI仕様書). It provides the required directory structure, markdown layout, design system token alignment, XAML offscreen rendering with tools/MockupRenderer, and linking steps adhering to .agents/rules/ui_spec_rules.md.
---

# UI仕様書作成スキル (create-ui-spec)

このスキルは、ユーザーから各画面・コンポーネントの「UI仕様書を作成する」よう指示された場合に使用します。
エージェントは [UI仕様書作成ルール (.agents/rules/ui_spec_rules.md)](../../rules/ui_spec_rules.md) および [デザインシステム仕様書 (設計/デザインシステム/)](../../../設計/デザインシステム/index.md) の各規定を厳格に遵守して作業を進めてください。

---

## ワークフロー手順

### 1. 前提ドキュメントの確認
作業を開始する前に、以下の設計書を必ず確認してください：
- [デザインシステム概要](../../../設計/デザインシステム/index.md)
- [01. デザイントークン仕様書](../../../設計/デザインシステム/01_デザイントークン仕様書.md)
- [02. 共通コントロールスタイル仕様書](../../../設計/デザインシステム/02_共通コントロールスタイル仕様書.md)
- [全体レイアウト・ペイン構成UI仕様書](../../../設計/画面設計/全体レイアウト・ペイン構成/全体レイアウト・ペイン構成UI仕様書.md)
- [パフォーマンス最適化詳細設計書](../../../設計/詳細設計/パフォーマンス最適化.md)

### 2. ディレクトリとファイルの準備
- プロジェクトルートから `設計/画面設計/<機能名・画面名>/` ディレクトリを作成します。
- そのディレクトリ内に `<画面名>UI仕様書.md` というMarkdownファイルを作成します（ファイル名は必ず日本語にすること）。

### 3. モックアップの策定（ASCIIモックアップおよびFigma連携）
- 主要オブジェクト仕様（Track, Album, UserPlaylist, PlayQueue等）に基づき、現在のコード実装状況に左右されない**本来あるべき画面構成（To-Be）**のASCIIアートモックアップを作成します。
- 後続タスクで詳細グラフィックを整えるためのFigmaリンク項目を記載します。
- （※XAMLオフスクリーンレンダラー `tools/MockupRenderer` 等による画面キャプチャは、仕様書本文には直接配置せず、Figmaイメージと実際の実装との比較検証用として使用します。）

### 4. UI仕様書の記述（マークダウンフォーマット）
以下の必須セクション構成に従って仕様書を作成してください：

```markdown
# <画面・コンポーネント名> UI仕様書

## 1. 概要 (Overview)
画面の目的、ユーザー体験、主要操作対象オブジェクト（Track, Album, Playlist, Device等）を明記。

## 2. 実装イメージ（モックアップ）
### 2.1 ASCIIモックアップ
```text
+-----------------------------------------------------------------------+
| ... 主要オブジェクトに基づくあるべきレイアウトのASCIIモックアップ ... |
+-----------------------------------------------------------------------+
```

### 2.2 Figmaモックアップ
- [Figmaデザイン（準備中 / 別タスクにて作成予定）](#)

## 3. 画面配置とペインレイアウト
全体レイアウト（3ペイン構成）における位置付け、Grid構造（Row/Column）、推奨サイズおよびリサイズ挙動。

## 4. デザイントークンの適用
使用するカラートークン、タイポグラフィ、角丸、スペーシング。
※必ず「カラー見本 (Preview)」列付きのテーブルを記載すること：
| トークン名 | カラー見本 (Preview) | カラーコード | 用途 |
| :--- | :---: | :--- | :--- |
| `NeonCyanBrush` | <span style="background-color:#00FFFF; color:#10141B; padding:2px 8px; border-radius:3px; font-weight:bold;">■ #00FFFF</span> | `#00FFFF` | アクティブ表示 |

## 5. 構成要素と共通コントロール
使用する共通コントロール（PrimaryButton, SearchBox, NeonSliderStyle, VirtualizedListBox等）およびオブジェクトの各状態スタイル（Normal, Hover, Pressed, Selected, Disabled, Active）。
※リスト行選択時の「左端ネオンシアンアクセント縦バー（幅3px）」仕様を明記すること。

## 6. アニメーションとインタラクション
クリック、ダブルクリック、右クリック（コンテキストメニュー）、ドラッグ＆ドロップ（D&D）、スライドアニメーション（CubicEase）などの詳細。

## 7. パフォーマンス最適化要件
- コレクション表示におけるUI仮想化プロパティ（VirtualizingPanel.IsVirtualizing="True", VirtualizationMode="Recycling", ScrollUnit="Pixel"）の指定。
- 非表示時の完全描画抑止（Visibility="Collapsed" 連動）。
- 高負荷動的エフェクト（DropShadowEffect/BlurEffect）の排除確認。

## 8. 関連Issue・仕様書
親Issue、関連ユースケース、関連主要オブジェクト仕様書へのリンク。
```

### 5. 品質チェックリスト（作成後検証）
- [ ] **過渡的表現の排除**: 「未実装」「現状の」「現状」「現行」等の表現が一切含まれていないか（`grep_search` 等で検査）。
- [ ] **デザイントークン準拠**: 独自カラーコードの直書きがなく、トークン名とカラー見本プレビューが併記されているか。
- [ ] **Mermaid記法の安全確認**: サブグラフ名やノードラベル内の特殊文字（`()`, `[]`, `:`, `#`, `<br/>`）がダブルクォート `["..."]` で囲まれているか。
- [ ] **パフォーマンス方針準拠**: コレクションビューのUI仮想化や描画抑止（Collapsed）が定義されているか。

### 6. ドキュメント目次の更新
作成した仕様書へのリンクを以下の2ファイルに必ず追加してください：
1. `設計/index.md`
2. `設計/toc.yml`（DocFX目次ツリー）

### 7. コミットとPR作成
- `create-pr-from-issue` スキルおよび `.agents/rules/git_operation_rules.md` に従い、作業ブランチ作成、コミット（`docs: 📝 #<Issue> <画面名>UI仕様書を策定する`）、プッシュ、PR作成を行ってください。
