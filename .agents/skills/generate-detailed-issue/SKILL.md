---
name: generate-detailed-issue
description: GitHub Issue の新規作成および更新を行うスキル。コードベース・仕様書・既存 Issue の事前調査、プロジェクト規約に沿った本文構成、適切なラベル（タグ）選定、および文字化け防止を含む作成・検証手順を提供する。
---

# 詳細 Issue 作成・更新スキル (generate-detailed-issue)

本プロジェクト（Audio Effector）で GitHub Issue を新規作成または更新する際は、必ず事前にコードベース・設計書・既存 Issue を調査し、実装者が迷わず着手できる粒度と適切なラベル（タグ）を備えて作成してください。

本スキルは `.agents/rules/issue_pr_rules.md` のルールを前提とし、具体的な調査・ラベル選定・CLI 操作手順を定義します。

---

## ワークフロー手順

### 1. 事前調査（Investigate First）

本文を書く前に、以下の 4 点を必ず確認します。

1. **リポジトリの最新ラベル一覧を取得**:
   - `gh label list` を実行し、現在利用可能なラベル名と説明を確認します。
2. **関連する既存 Issue の確認**:
   - `gh issue list --state all --limit 40` 等で重複 Issue や親/関連 Issue（Epic や類似機能）がないかを確認し、リンク対象を特定します。
3. **コードベース調査**:
   - 変更対象となるレイヤー（Domain / Application / Infrastructure / Presentation / Tests）の実ファイルを読み込み、正確なファイルパス・クラス名・メソッド名・プロパティ名・イベント名・コマンド名を把握します。
4. **関連仕様書の確認（必須）**:
   - GitHub Wiki の `仕様書同期・メンテナンスルール` および `設計/spec_map.json` を確認します。
   - 変更予定のファイルパスが特定できたら `python tools/check_spec_sync.py --files <変更予定ファイルパス...>` を実行し、該当する仕様書（Primary / Related）を漏れなく洗い出します。
   - 該当仕様書の内容を読み込み、実装方針との整合性および本 Issue で更新すべき仕様書を確定します。

---

### 2. タイトル命名規則

タイトルは内容が一目で分かる簡潔な日本語とし、先頭に種別プレフィックスを付与します。

- `[機能追加]` : 新規機能の追加
- `[機能改善]` : 既存機能の使い勝手・挙動改善
- `[機能変更]` : 既存仕様の変更・統合・廃止を伴う改修
- `[UI改修]` : 画面レイアウト・デザイン・スタイルの改善
- `[バグ修正]` / `[不具合修正]` : 不具合の修正
- `[パフォーマンス改善]` : 起動速度・描画・メモリ・CPU 負荷の改善
- `[リファクタリング]` : 機能変更を伴わない内部構造の改善
- `[検討・仕様策定]` : 仕様検討やドキュメント整備が主目的のタスク
- `[Epic]` : 複数のサブ Issue を束ねる親 Issue
- `[OOUI PhaseX]` : OOUI 設計・移行プロジェクトに属するタスク

---

### 3. ラベル（タグ）付与ルール

Issue 作成時（および更新時）は、必ず `--label`（または `--add-label`）で適切なラベルを設定します。
基本方針として **「種別ラベル（必須）」＋「該当する領域ラベル（1つ以上）」＋「プロジェクトラベル（該当時のみ）」** を組み合わせて付与します。

#### 3.1 種別ラベル（原則 1 つ必須）

| ラベル名 | 付与基準 |
| :--- | :--- |
| `enhancement` | 新機能追加、機能改善、機能変更、UI 改修、パフォーマンス改善、リファクタリング |
| `bug` | バグ・不具合修正 |
| `documentation` | 仕様書・設計書・README 等のドキュメント作成・更新のみが目的の場合 |
| `epic` | 複数のサブ Issue を束ねる親 Issue（必要に応じて `enhancement` 等と併用） |
| `question` | 方針相談や調査・確認が主目的の場合 |

#### 3.2 領域ラベル（該当するものを複数付与可）

| ラベル名 | 付与基準 |
| :--- | :--- |
| `area:frontend` | WPF / XAML 実装、ViewModel、画面遷移、UI 操作、フロントエンド単体テスト等 |
| `area:backend` | コアロジック、音声処理（DSP/再生エンジン）、データ永続化、リポジトリ、機器転送等 |
| `area:ui-design` | 新規画面・レイアウト設計、デザイン追加・刷新、テーマ/デザインシステム、Figma モックアップ等 |
| `area:spec` | 機能仕様書・画面仕様書・詳細設計書の策定が主目的の場合 |

#### 3.3 プロジェクト・横断ラベル（該当時のみ）

| ラベル名 | 付与基準 |
| :--- | :--- |
| `ooui` | OOUI（オブジェクト指向 UI）設計・移行プロジェクト関連の Issue |
| `good first issue` / `help wanted` | 該当する場合のみ付与 |

#### 3.4 付与パターンの目安

- **新UIデザインを伴う機能追加**（例: ミニプレイヤー新デザイン、新規パネル追加）:
  - `enhancement`, `area:frontend`, `area:ui-design`
- **設定追加＋バックエンド挙動変更**（例: 再生終了時の自動移行設定）:
  - `enhancement`, `area:backend`, `area:frontend`
- **既存画面の操作改善・ViewModel改修**（デザイン変更なし）:
  - `enhancement`, `area:frontend`
- **UI/フロントエンド起因の不具合修正**:
  - `bug`, `area:frontend`
- **音声再生・永続化・デバイス転送の不具合修正**:
  - `bug`, `area:backend`
- **仕様書・設計ドキュメント策定のみ**:
  - `documentation`, `area:spec`（OOUI 関連なら `ooui` も追加）
- **複数サブ Issue を束ねる親 Issue**:
  - `epic` ＋ 該当する種別/領域ラベル

> **注意**: リポジトリに存在しないラベル名は指定しないでください。事前に必ず `gh label list` で最新一覧を確認し、新設が必要と思われる場合のみユーザーへ提案してください。

---

### 4. Issue 本文の構成（日本語）

`.agents/rules/issue_pr_rules.md` の必須項目を満たしつつ、以下の構成で記述します。

#### `### 1. 概要 (Overview)`
- **背景 (Background)**: なぜこの Issue が必要か、現状の課題や制約を簡潔に記載します。
- **目的 (Objective)**: 本 Issue で最終的に達成したいゴールを明記します。

#### `### 2. 実装内容 (Implementation Details)`
最重要セクションです。事前調査の結果に基づき、具体的に記述します。
- アーキテクチャ層または論理コンポーネントごとにグループ化します（例: `① 設定データモデルの拡張`, `② ViewModel・UI拡張`, `③ 永続化・サービス層`, `④ テストおよびドキュメント更新`）。
- **明示的なファイルパス**を必ずカッコ書きで記載します（例: `` `AudioEffector/Domain/Entities/AppSettings.cs` ``, `` `AudioEffector/Presentation/Views/SettingsDialog.xaml` ``）。
- **具体的なコードシンボル**（クラス名、列挙体名、プロパティ名、メソッド名、コマンド名、イベント名、XAML 要素名など）を記載します（例: `MiniPlayerTopmostBehavior`, `IsPlaying`, `RotateTransform`, `PreviewKeyDown`）。
- 実装上の注意点（イベント購読解除によるメモリリーク防止、UI 仮想化の維持、フォールバック表示、既存動作との互換性など）も補足します。

#### `### 期待される動作 (Expected Behavior)`
- 実装完了後に満たすべき受け入れ条件をチェックボックス（`- [ ]`）形式で列挙します。
- 既存機能へのデグレがないことの確認項目も含めます。

#### `### 更新対象の仕様書 (Target Specs)`【必須】
- `python tools/check_spec_sync.py --files <変更予定ファイル...>` の結果および GitHub Wiki `仕様書同期・メンテナンスルール` に基づき、本 Issue で更新対象となる `設計/` 配下の仕様書パスをチェックボックス形式（`- [ ]`）で明記します。
  - 例: `` - [ ] `設計/画面設計/最近再生した曲/最近再生した曲UI仕様書.md` ``
- 新規画面・新規機能を追加する場合は、`設計/spec_map.json`・`設計/index.md` への登録もあわせて記載します。
- 仕様変更を伴わない内部リファクタリング・軽微なバグ修正などで更新不要な場合は、省略せず **「なし（理由: 既存の仕様・UI・永続化形式に変更がないため）」** のように理由を明記します。

#### `### 3. 関連Issue (Related Issues)`
- 親 Issue・子 Issue・関連 Issue・統合対象 Issue がある場合は番号と概要をリンクします（例: `- 親Issue: #211`、`- 関連Issue: #217（...）`）。該当がない場合は「なし」または省略可。

---

### 5. GitHub CLI での作成・更新（文字化け防止必須）

Windows PowerShell 環境では、日本語の本文をコマンドライン引数（`--body "..."`）で直接渡すと文字化けする恐れがあります。必ず以下の手順で実行してください。

1. 本文を **UTF-8 の一時ファイル**（リポジトリ外の一時領域、または scratch ディレクトリなど `git status` を汚さない場所）に書き出します。
2. PowerShell でコンソール入出力エンコーディングを UTF-8 に切り替えたうえで、`--body-file` と `--label` を指定して `gh issue create`（または `gh issue edit`）を実行します。

```powershell
chcp 65001 > $null
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

gh issue create `
  --title "[機能追加] タイトルをここに記載" `
  --body-file "<UTF-8の本文ファイルパス>" `
  --label "enhancement" `
  --label "area:frontend" `
  --label "area:ui-design"
```

---

### 6. 作成・更新後の検証

コマンド実行直後に、必ず作成・更新した Issue を取得して目視確認します。

```powershell
gh issue view <Issue番号>
```

以下を確認してからユーザーへ報告してください：
- タイトルおよび本文の日本語が文字化けしていないこと
- 適切なラベル（種別・領域等）が漏れなく付与されていること
- 関連 Issue 番号やファイルパスに誤りがないこと
- リポジトリ内に一時ファイルが残っていないこと
