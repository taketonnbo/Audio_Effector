---
name: run-dev-loop
description: Issue 着手から実装・統合検証（tools/verify_loop.ps1）・修正反復・分割コミット・PR 作成・独立エージェントによる PR レビュー（review-pr）・指摘対応（address-pr-review）・再レビュー収束・振り返り改善ループまでを一気通貫で自律反復する標準開発ワークフロースキル。/goal 等と組み合わせて Issue 対応を一貫推進する際に使用する。
---

# 自律開発ループ実行スキル (run-dev-loop)

このスキルは、Audio Effector リポジトリにおいて **Issue 着手から PR 作成・独立エージェントによる PR レビュー・指摘対応・収束・改善ループまで** を、安全・高速・再現可能に一気通貫で自律反復するための標準ワークフローを定義します。

本スキルを実行する際は、必ず以下の前提ルールおよび連携スキルを遵守してください：
- **必須ルール**: `.agents/rules/loop_engineering_rules.md`（Verifier 保護・停止条件・ロールバック・コンテキスト分離・改善ループ・並列運用基準）
- **関連ルール**: `.agents/rules/branch_operation_rules.md`, `git_operation_rules.md`, `build_rules.md`, `test_rules.md`, `issue_pr_rules.md`
- **連携ツール**: `tools/verify_loop.ps1`, `tools/worktree_manager.ps1`, `tools/sync_branch.ps1`
- **連携スキル**: `create-pr-from-issue`, `review-pr`, `address-pr-review`, `sync-specs`, `check-equivalence`（※公式 PR レビュー `review-pr` は実装履歴を引き継がない独立サブエージェントまたは別セッションで実施）

---

## 全体フロー

```mermaid
flowchart TD
    P0["Phase 0: 準備・Done定義固定・ブランチ作成"] --> P1["Phase 1: 実装・単体テスト追加"]
    P1 --> P2["Phase 2: 統合検証 (verify_loop.ps1)"]
    P2 -->|FAIL & 停止条件未満| P3["Phase 3: 原因分析・修正反復"]
    P3 --> P2
    P2 -->|停止条件到達| ESC["安全停止・ユーザーへエスカレーション"]
    P2 -->|PASS (Full Gate)| P4["Phase 4: 分割コミット・PR作成・独立エージェントレビュー起動"]
    P4 --> EXT["独立エージェント（サブエージェント / 別セッション）による PR レビュー (review-pr)"]
    EXT -->|指摘あり| P5["Phase 5: PRレビュー指摘対応ループ (address-pr-review + verify_loop.ps1 -Full)"]
    P5 -->|再レビュー依頼| EXT
    EXT -->|指摘 0 件のレビューコメント確認| P6["Phase 6: 振り返り・改善ループ"]
```

---

## ワークフロー手順

### Phase 0: 準備・完了条件（Done 定義）の固定
1. 対象 Issue の内容を `gh issue view <Issue番号>` で読み込み、関連コード・設計書（`設計/`）を調査する。
2. 以下の **Done 定義** を作業開始時に明確化する：
   - 受け入れ条件（Issue の期待される動作チェックリスト）
   - 変更対象レイヤーと単体テスト対象範囲
   - 更新が必要な仕様書（`python tools/check_spec_sync.py --files ...` で事前確認）
   - 停止条件（同一原因の連続失敗 3 回、実装ループ最大 10 回、レビュー対応最大 5 往復）
3. 実行モードに応じて最新の `develop`（または `origin/develop`）から命名規則（`<Type>/<Issue番号>-<簡潔な説明>`）に従って作業環境を準備する：
   - **単一実行モード（親リポジトリ直下）**:
     ```powershell
     git checkout develop
     if ($?) { git pull origin develop }
     if ($?) { git checkout -b <Type>/<Issue番号>-<説明> }
     ```
   - **並列ワークツリーモード（Epic 並列実行・複数 Issue 同時進行時）**:
     - `tools/worktree_manager.ps1` を用いて `.agent_worktrees/issue-<Issue番号>` を作成し（最大 3 並列上限ガード付き）、以降の全作業・コマンド実行・検証ログ出力（`.agent_logs/verify_issue_<Issue番号>.json`）はそのワークツリー内で行う。親リポジトリ直下や他ワークツリー、および `.agent_logs/epic_<番号>_state.json` は変更しない。
     ```powershell
     .\tools\worktree_manager.ps1 -Action create -IssueNumber <Issue番号> -BranchName "<Type>/<Issue番号>-<説明>" -Json
     Set-Location -LiteralPath ".agent_worktrees\issue-<Issue番号>"
     ```

### Phase 1: 実装・単体テスト追加
1. 計画に沿ってプロダクションコードを実装する（アーキテクチャ境界・コーディング規約・パフォーマンス基準を順守）。
2. `test_rules.md` に準拠した単体テスト（4 層対称配置、`sut` 命名、AAA コメント、日本語テスト名・XML コメント、`using` 破棄、STA スレッドの `InvokeShutdown()`）を追加・更新する。
3. 仕様・UI・永続化・アーキテクチャ境界に変更が生じる場合は、該当する `設計/` 配下の仕様書も更新する（必要に応じて `sync-specs` スキルを参照）。

### Phase 2: 統合検証（`tools/verify_loop.ps1`）
1. **途中イテレーション（高速ループ）**:
   - 実装途中の動作確認では、`-SkipSpecSync` や `-TestFilter` を併用して高速にフィードバックを得る。
   ```powershell
   .\tools\verify_loop.ps1 -SkipSpecSync -TestFilter "<対象テストフィルタ>" -JsonOut ".agent_logs\verify_iter.json"
   ```
2. **最終完了ゲート（コミット・PR 作成前に必須）**:
   - 実装と仕様書更新が完了したら、必ず `-Full` モードで全 5 ゲートを実行し、終了コード `0` を確認する。
   - 並列ワークツリー実行時は `origin/develop` が比較ベースとして自動優先され、Gate 1 ビルド工程は名前付き Mutex により他ワークツリーと安全に直列化される。明示的に `origin/develop` を最新取得したい場合は `-PreferOriginBase -FetchBase` を併用する。
   ```powershell
   .\tools\verify_loop.ps1 -Full -PreferOriginBase -JsonOut ".agent_logs\verify_issue_<Issue番号>.json"
   ```
   - 検証完了後、`.agent_build/` が残留していないことを確認する。

### Phase 3: 原因分析・修正反復
- Phase 2 が失敗（終了コード非 `0`）した場合、標準出力および `.agent_logs/verify_*.json` から失敗ゲート・エラー原因を特定し、**失敗したゲートのみを的確に修正**して Phase 2 を再実行する。
- **厳守事項（`loop_engineering_rules.md`）**:
  - テスト削除・`Skip` 化・アサーション弱体化・`verify_loop.ps1` の基準緩和は**絶対に行わない**。
  - **同一原因の失敗が連続 3 回**、または**実装ループが 10 回**に達した場合は直ちに安全停止し、原因仮説・試した対策・選択肢を添えてユーザーへ報告する。
  - 要件や仕様の判断が必要な場合も無理に進めず安全停止して確認する。

### Phase 4: 分割コミット・最新化同期・PR 作成・独立エージェントレビュー起動
1. `create-pr-from-issue` スキルおよび `git_operation_rules.md` に従い、レイヤー・責務・変更種別ごとにファイルを指定して分割コミットする（`git add .` による巨大単一コミットは禁止）。
   - コミットメッセージ形式: `<Type>: <Emoji> #<Issue番号> <現在形の日本語Subject>`
   - コミット後、`git log develop..HEAD --stat` で粒度の偏りがないか確認する。
2. **PR 作成前の `origin/develop` 最新化と自動コンフリクト解消プロトコル**:
   - 未コミット変更がない状態で `.\tools\sync_branch.ps1 -Json` を実行し、最新の `origin/develop` への非破壊追従を確認・実施する。
   - `status: "UP_TO_DATE"`（終了コード `0`）: そのまま Push・PR 作成へ進む。
   - `status: "MERGED"`（終了コード `0`）: 取り込み差分を含めて `.\tools\verify_loop.ps1 -Full -PreferOriginBase -JsonOut ".agent_logs\verify_final_<Issue番号>.json"` を再実行し、全 5 ゲート合格（終了コード `0`）を確認してから Push へ進む。
   - `status: "CONFLICT"`（終了コード `2`）: `conflict_files` を確認し、`loop_engineering_rules.md` Section 4 の基準に従って対応する：
     - **自動解消許可範囲**（`using` 追記、`.csproj` / `spec_map.json` / DI 登録 / テストの独立追記等）であれば解消・マージコミットし、`.\tools\verify_loop.ps1 -Full -PreferOriginBase` で全 5 ゲート合格を確認する。
     - **ロジック・設計判断衝突、または解消後の再検証が連続 2 回失敗した場合**は、直ちに `.\tools\sync_branch.ps1 -Abort -Json` でロールバックし、コンフリクトファイル一覧と論点を添えてユーザーへ安全停止・エスカレーションする。
3. リモートへプッシュし、`gh pr create --base develop --title "[種別] ..." --body-file <一時ファイル>` で PR を作成する（使用後に一時ファイルを削除）。
   - PR 本文には `Closes #<Issue番号>`、概要、対応内容、検証結果、仕様書同期結果、スコープ差分を記載する。
4. **独立エージェントによる公式 PR レビュー実行（コンテキスト分離の原則）**:
   - 確証バイアス防止のため、**実装エージェント自身で公式の PR レビューコメント投稿（`review-pr`）を行わない**。
   - ユーザーから明示的な停止指示がない限り、PR 作成後に作業を止めず、**実装履歴を引き継がない新規の独立サブエージェント**（`invoke_subagent` 等）を起動して `review-pr` を実行し、PR へ公式レビューを投稿させる。
   - 独立サブエージェントへは PR 番号・関連 Issue 番号・重点確認観点のみを渡し、実装側の主観や前提は引き継がないこと。

### Phase 5: PR レビュー指摘対応・マージ前追従同期ループ
独立エージェント（または人間のレビュアー）から PR にレビューコメントが投稿された後、以下のループを収束するまで反復する（最大 5 往復）：
1. `address-pr-review` スキルの手順に従い、レビュー総評・全インラインコメント・通常コメントを取得し、コードベースと照合して妥当性を判定する（妥当 / 一部妥当・代替案 / 妥当でない・スコープ外）。
2. 妥当と判定した指摘を修正し、必要に応じて単体テスト・仕様書を更新する。
3. **`.\tools\verify_loop.ps1 -Full -PreferOriginBase -JsonOut ".agent_logs\verify_review.json"` を実行**し、全 5 ゲート合格（終了コード `0`）を確認する。
4. 対応単位ごとに分割コミットして通常プッシュ（`git push origin <ブランチ名>`、`--force` 禁止）を行う。
5. 各インラインコメントへの個別返信（判定結果・修正内容・コミットハッシュ）および総括コメントを投稿し、必要なら PR 本文を更新する。
6. `git status -sb` と `git log origin/<ブランチ名>..HEAD --oneline` が空であることを確認したうえで、**再度独立エージェントへ再レビューを依頼する**。
   - **2 回目以降の再レビューで指摘が 0 件の場合でも、必ず「指摘解消確認・新規指摘なし・LGTM（マージ可能）」のレビューコメントを PR へ投稿**させ、それをもって収束（Phase 6 へ遷移）とする。
7. **マージ前の追従同期（他並列 PR の先行マージにより `BEHIND` / `CONFLICTING` となった場合）**:
   - `.\tools\sync_branch.ps1 -Json` を実行して最新の `origin/develop` を非破壊マージで取り込む（`CONFLICT` 時は Phase 4 手順 2 の自動コンフリクト解消プロトコルまたは `-Abort` エスカレーションを適用）。
   - マージコミットが生成された場合は、必ず `.\tools\verify_loop.ps1 -Full -PreferOriginBase` の全 5 ゲート合格を確認したうえで通常 Push し、マージ可能なクリーン状態を保証する。

### Phase 6: 振り返り・改善ループ（メタ改善）
PR の全指摘が解消・完了（または安全停止）したタイミングで、以下を振り返って簡潔にユーザーへ提示する：
1. **ループ実績サマリ**: イテレーション回数、詰まったゲート、修正に要した手戻り要因。
2. **再発防止・基盤改善の提案**: 次回以降のループ効率・品質を高めるための `.agents/rules/`、`.agents/skills/`、または `tools/verify_loop.ps1` への改善案（ユーザーの承認なく自動改変しない）。

---

## 完了チェックリスト
- [ ] Phase 0 で Done 定義と作業ブランチ（`develop` 派生）を確定した
- [ ] Verifier 保護（テスト改ざん・基準緩和なし）と停止条件を順守した
- [ ] 最終ゲートとして `.\tools\verify_loop.ps1 -Full` が終了コード `0` で合格し、`.agent_build/` が残留していない
- [ ] 意味のある単位で分割コミットし、`develop` 向けに PR を作成した
- [ ] 公式 PR レビューは独立エージェント（サブエージェント / 別セッション）で実施し、コンテキスト分離を守った
- [ ] レビュー指摘対応後、全コメント返信・再レビューでの指摘 0 件コメント確認・リモート同期・作業ツリークリーンを確認した
- [ ] 振り返りと改善候補をユーザーへ提示した
