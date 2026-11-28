---
name: address-pr-review
description: PRのレビュー指摘コメントを取得し、コードベースと照合して妥当性を判定したうえで修正・ビルド/テスト検証・粒度を守ったコミット＆プッシュ・各コメントへの返信までを一貫して行うスキル。
---

# PRレビュー対応スキル (address-pr-review)

このスキルは、ユーザーから「PR #〇〇 のレビュー指摘に対応してプッシュ・返信してほしい」という指示を受けた際に使用します。
エージェントは指摘を鵜呑みにせず、必ずコードベース・仕様書・プロジェクトルールと照合して妥当性を判定したうえで、安全かつ漏れなく対応を進めてください。

---

## ワークフロー手順

### 1. PR 情報とレビュー指摘の網羅的取得
- 指定された PR 番号について、以下の 4 種類をすべて取得し、未返信・未解決の指摘を漏れなくリストアップします。
  1. PR メタ情報・本文・ブランチ名: `gh pr view <PR番号> --json number,title,headRefName,baseRefName,state,body,url`
  2. レビュー総評一覧: `gh api repos/:owner/:repo/pulls/<PR番号>/reviews --paginate`
  3. インラインレビューコメント一覧: `gh api repos/:owner/:repo/pulls/<PR番号>/comments --paginate`
  4. PR 通常コメント一覧: `gh api repos/:owner/:repo/issues/<PR番号>/comments --paginate`
- 作業ブランチが PR の `headRefName` と一致していること、および未コミットの作業ツリー状態（`git status`）を確認します。
- 関連するプロジェクトルール（`.agents/rules/git_operation_rules.md`, `issue_pr_rules.md`, `build_rules.md`, `test_rules.md`, `branch_operation_rules.md`）および関連仕様書（`設計/詳細設計/パフォーマンス最適化.md` 等）を確認します。

### 2. 各指摘の妥当性判定（トリアージ）
すべての指摘コメントについて、該当コードおよび呼び出し元・関連クラスを実際に読み込み、以下の 3 区分で妥当性を判定します。

| 判定 | 基準 | アクション |
| :--- | :--- | :--- |
| **妥当（対応する）** | バグ、パフォーマンス問題、設計・ルール違反、視認性・UX 問題、テスト不足など、コード上の裏付けがあり修正すべきもの | コード修正・テスト追加・PR本文修正を実施 |
| **一部妥当 / 代替案あり** | 問題認識は正しいが、提案手法が既存アーキテクチャやパフォーマンス方針（例: 仮想化、DI ライフサイクル）と衝突する場合 | より安全な代替アプローチで根本原因を解消し、理由を返信に記載 |
| **妥当でない / スコープ外** | 前提誤認、既存仕様として意図的、または元 Issue のスコープを超える機能追加要求の場合 | 無理に実装せず、コード・仕様書に基づく根拠（および必要なら後続 Issue 化方針）を丁寧に返信 |

> **注意（コミット粒度・履歴書き換え指摘の扱い）**:
> リモートへ既にプッシュ済みかつインラインレビューコメントが紐づいている PR では、強制プッシュ（`--force`）による過去コミット書き換えはレビューコメントの文脈や差分追跡を壊すリスクがあります。ユーザーから明示的な履歴整理の指示がない限り、既存コミットは保全しつつ、**今回の修正コミットを対応単位ごとに細かく分割して通常の `git push`** を行い、その旨を丁寧に返信してください。

### 3. 修正実装とテスト拡充
- 妥当と判定した指摘をすべて実装します。
- **仕様書の追従更新（必須チェック）**: レビュー指摘対応によって画面 UI・操作挙動・永続化形式・アーキテクチャ制約などの仕様に変更が生じた場合は、GitHub Wiki `仕様書同期・メンテナンスルール` に従い、該当する `設計/` 配下の仕様書も必ずあわせて更新します（別コミット `docs: 📝 #<Issue番号> ...` として分割）。
- **パフォーマンス方針の順守**: `設計/詳細設計/パフォーマンス最適化.md`（スロットリング/デバウンス、不要な通知抑止、UI仮想化の維持、`Freeze()` 等）に反していないか確認します。
- **テストルール（`.agents/rules/test_rules.md`）の順守**:
  - テストメソッド名は日本語の `テスト対象の処理_テスト事前条件_期待される結果` 形式、XML コメント・`// Arrange` / `// Act` / `// Assert`・`sut` 命名を徹底します。
  - `IDisposable` なテスト対象・依存オブジェクトは `using var` で確実に破棄します。
  - WPF の STA スレッドテストでは、必ず `finally` 句で `Dispatcher.CurrentDispatcher.InvokeShutdown()` を実行し、必要に応じて `DispatcherSynchronizationContext` を設定して非同期継続によるテストホストクラッシュを防止します。
  - XAML ビューの変更を含む場合は、`BindingErrorTraceListener` によるビュー実体のバインディングエラー 0 件テストや `ThemeResourceTests` の追加を検討します。

### 4. 統合検証ハーネスによる隔離ビルド・テスト検証（`.agents/rules/build_rules.md` / `loop_engineering_rules.md` 厳守）
- ユーザーのデバッグ実行および他並列ワークツリーを阻害しないよう、原則として統合検証ハーネス **`tools/verify_loop.ps1 -Full`** を用いて隔離ビルド・全単体テスト・仕様書同期・XAML バインディング等価性・Python ツール単体テストの全 5 ゲートを一括検証します。
  - `-BaseBranch` には PR の `baseRefName`（通常 PR は `develop`、Epic 子 Issue PR は当該 Epic の統合ブランチ `epic/<Epic番号>-<説明>`）を指定します。
  ```powershell
  .\tools\verify_loop.ps1 -Full -BaseBranch "<baseRefName>" -PreferOriginBase -JsonOut ".agent_logs\verify_review.json"
  ```
  - 並列ワークツリー（`.agent_worktrees/issue-<番号>`）内で実行する場合は、`origin/<baseRefName>` が比較ベースとして自動優先され、Gate 1 ビルド工程は名前付き Mutex により安全に直列化されます（明示的に最新比較したい場合は `-PreferOriginBase -FetchBase` を併用）。
- **レビュー対応中に `origin/<baseRefName>` が進んだ場合（`BEHIND` / `CONFLICTING` 対応）**:
  - 履歴書き換え（`rebase` / `--force`）は禁止のため、未コミット変更をコミットした後に `.\tools\sync_branch.ps1 -BaseBranch "<baseRefName>" -Json` で非破壊追従マージを実行します。
  - `CONFLICT`（終了コード `2`）発生時は `.agents/rules/loop_engineering_rules.md` Section 4 の基準に従い、自動解消可能な独立衝突のみ解消して `.\tools\verify_loop.ps1 -Full -BaseBranch "<baseRefName>" -PreferOriginBase` を再実行し、ロジック衝突や再検証連続 2 回失敗時は `.\tools\sync_branch.ps1 -BaseBranch "<baseRefName>" -Abort -Json` でロールバックしてユーザーへ報告します。
- **警告 0・エラー 0・全テスト合格（終了コード `0`）** になるまで修正を繰り返します（StyleCop / CA 警告もすべて解消します）。
- ビルド・テスト実行後は `.agent_build/` ディレクトリが残っていないことを必ず確認します。

### 5. コミット分割とプッシュ（`.agents/rules/git_operation_rules.md` 厳守）
- `git status` で不要・一時ファイルが含まれていないことを確認します。
- `.editorconfig`（`charset = utf-8-bom`, `end_of_line = crlf`）に準拠するため、変更対象ファイルの文字コード（UTF-8 BOM）と改行コード（CRLF）をコミット前に統一します。
- 変更内容が複数の関心事にまたがる場合は、1 コミットにまとめず **意味のある論理単位（ViewModel 最適化 / 再生連携・購読解除 / メタデータ抽出 / UI・テーマ修正 / テスト拡充 等）ごとにステージングしてコミットを分割** します。
- コミットメッセージは `<Type>: <Emoji> #<Issue番号> <現在形の日本語Subject>` 形式を厳守します。
- **【必須】リモートへのプッシュ実行**: すべてのコミット完了後、必ず作業ブランチをリモートへプッシュします（`git push origin <ブランチ名>`）。ローカルコミットのみで作業を終了してはなりません。

### 6. PR 本文の更新および全レビューコメントへの返信
- PR 本文の修正指摘（例: `Resolves #〇〇` → `Closes #〇〇`、スコープ差分の明記、検証結果の更新）がある場合は、`gh pr edit <PR番号> --body-file <一時ファイル>` で更新します（使用後に一時ファイルを削除）。
- **文字化け防止**: Windows PowerShell 環境で日本語を直接コマンド引数に渡すとエンコーディング崩れが起きやすいため、UTF-8 JSON を `--input -`（またはリポジトリ外 scratch ディレクトリの一時 JSON ファイル）で渡す方式を使用します。
- **各インラインコメントへの個別返信**:
  - エンドポイント: `POST repos/:owner/:repo/pulls/<PR番号>/comments/<comment_id>/replies`
  - 各返信には以下を簡潔に明記します：
    1. 妥当性判定の結果（対応したか、見送ったか）
    2. 具体的な修正内容（または見送り・代替案の根拠）
    3. 該当するコミットハッシュ（例: `` `1f93b36` ``）
- **PR 全体への総括コメント投稿**:
  - エンドポイント: `POST repos/:owner/:repo/issues/<PR番号>/comments`
  - レビュー総評・追加レビュー（コミット粒度等）への回答、コミット一覧、およびビルド・テスト検証結果をまとめて投稿します。

### 7. プッシュ完了および作業ツリークリーン状態の最終検証（必須ゲート）
- ユーザーへ完了報告を行う**直前**（すべてのツール操作・コメント投稿が終わった後）に、必ず以下のコマンドを実行して最終状態を確認します：
  ```powershell
  git status -sb
  git log origin/<ブランチ名>..HEAD --oneline
  ```
- **以下の 2 条件を両方満たすまで完了報告を行ってはなりません**：
  1. **未コミット差分の完全ゼロ化**: カレントブランチの作業ツリーに未ステージの変更（`M`）、ステージ済みの未コミット変更、未追跡ファイル（`??`）が **1 件も残っていないこと**。
     - ※ `edit_file` / `write_file` 実行後に PowerShell 等で BOM 正規化を行った場合、IDE のエディタバッファ同期によってコミット直後に先頭 BOM 剥がれ差分（1行目の差分）が作業ツリーへ再出現することがあります。その場合は `git diff` で内容を確認したうえで `git checkout -- .` を実行し、再度 `git status -sb` で変更ファイルが 0 件であることを必ず確認してください。
  2. **リモートプッシュ完了の確認**: `git log origin/<ブランチ名>..HEAD --oneline` の出力が**空**であり、ローカルの `HEAD` とリモートの `origin/<ブランチ名>` が完全に一致していること。

---

## 完了チェックリスト
- [ ] PR のレビュー総評・全インラインコメント・通常コメントを漏れなく確認した
- [ ] 全指摘についてコードベースの裏付けをもとに妥当性を判定した
- [ ] `.\tools\verify_loop.ps1 -Full -BaseBranch "<baseRefName>" -PreferOriginBase -JsonOut ".agent_logs\verify_review.json"` で全 5 ゲート合格（エラー 0・警告 0・全テスト合格・仕様書同期 OK・XAML 等価性 OK・Python ツールテスト合格）、`.agent_build/` 非残留を確認した
- [ ] 変更内容を意味のある単位ごとにコミット分割し、リモートへプッシュ（`git push origin <ブランチ名>`）まで完了した
- [ ] PR 本文（必要な場合）を更新し、全インライン指摘への個別返信と総括コメントを投稿した
- [ ] 最終ステップで `git status -sb` および `git log origin/<ブランチ名>..HEAD --oneline` を実行し、カレントブランチにコミット前の差分が1件も残っておらずリモートと完全同期していることを確認した
