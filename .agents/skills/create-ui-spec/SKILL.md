---
name: create-ui-spec
description: >-
  Use this skill when the user asks you to create a UI specification document (UI仕様書). It provides the required directory structure, markdown layout, design system token alignment, image generation instructions, and linking steps adhering to .agents/rules/ui_spec_rules.md.
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
- モックアップ画像を保存するため、同ディレクトリ内に `images/` サブディレクトリを作成します。

### 3. モックアップ画像の生成
- `generate_image` ツールを使用して、対象画面のUIモックアップ画像を生成します。
- **必須プロンプト要件**:
  - `Windows 11 desktop application, Fluent Design system`
  - `Dark theme, mica effect, deep charcoal slate background (#161920)`
  - `Sleek neon cyan accent (#00FFFF)`
  - 対象画面のペイン位置（左サイドバー、中央ワークスペース、右スライドパネル、下部プレイヤーバー）に合わせたレイアウト
  - `High-resolution UI presentation, no external monitor frames`
- 生成した画像を `<画面名>UI仕様書.md` と同じディレクトリの `images/` 配下にコピー（または移動）します。
- Markdownファイル内に `![実装イメージ](images/<画像ファイル名>)` として画像を挿入します。

### 4. UI仕様書の記述（マークダウンフォーマット）
以下の必須セクション構成に従って仕様書を作成してください：

```markdown
# <画面・コンポーネント名> UI仕様書

## 1. 概要 (Overview)
画面の目的、ユーザー体験、主要操作対象オブジェクト（Track, Album, Playlist, Device等）を明記。

## 2. 実装イメージ（モックアップ）
![<画面名> 実装イメージ](images/<画像名>.jpg)

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
