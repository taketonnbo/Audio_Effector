---
name: run-epic-loop
description: Epic Issue 配下の複数子 Issue を依存関係・並列グループに従って順次・並列に完遂させる外側ループ（Outer Loop）スキル。tools/epic_tracker.py で進捗を追跡し、Inner Loop（run-dev-loop）・独立コンテキストでの PR レビュー（review-pr）・指摘対応（address-pr-review）を統括する。
---

# Epic 自律実行スキル (run-epic-loop)

このスキルは、Audio Effector リポジトリにおいて **複数の子 Issue を束ねる Epic Issue 全体** を、単一 Issue 用の自律開発ループ（Inner Loop: `run-dev-loop`）を土台として安全かつ再現可能に完遂させるための **外側ループ（Outer Loop）** 手順を定義します。

本スキルを実行する際は、必ず以下の前提ルールおよび連携スキルを遵守してください：
- **必須ルール**: `.agents/rules/loop_engineering_rules.md`（特に Section 5「コンテキスト分離の原則」および Section 7「Epic 運用ルール」）
- **関連ルール**: `.agents/rules/branch_operation_rules.md`, `git_operation_rules.md`, `build_rules.md`, `test_rules.md`, `issue_pr_rules.md`
- **連携ツール・スキル**:
  - 進捗・依存関係・並列スロットトラッカー: `tools/epic_tracker.py`（`--integration-branch` 対応、統合ブランチ・統合 PR 追跡、状態スナップショット: `.agent_logs/epic_<番号>_state.json` ※親オーケストレーター単独書き込み）
  - ワークツリー管理ツール: `tools/worktree_manager.ps1`（`.agent_worktrees/issue-<番号>` の作成・一覧・削除・整理、`-BaseBranch "<統合ブランチ>"` および最大 3 並列上限ガード）
  - ブランチ最新化・コンフリクト検知ツール: `tools/sync_branch.ps1`（`-BaseBranch "<統合ブランチ>"` / `develop` 非破壊マージ・コンフリクト検知 exit 2・`-DryRun`・`-Abort`）
  - 統合検証ハーネス: `tools/verify_loop.ps1`（`-BaseBranch "<統合ブランチ>"` / `develop`・`origin/<BaseBranch>` 自動優先解決・名前付き Mutex ビルド保護・ワークツリーレポート拡張）
  - Inner Loop: `run-dev-loop`（実装・検証・分割コミット・PR 作成・レビュー指摘対応・最新化同期）
  - 公式 PR レビュー: `review-pr`（※必ず実装とは独立したコンテキストで実行）
  - レビュー指摘対応: `address-pr-review`

---

## 全体フロー（2 階層ループ＋Epic 統合ブランチ構成）

```mermaid
flowchart TD
    P0["Phase 0: Epic 統合ブランチ作成・Push (epic/<番号>-<説明>)<br/>epic_tracker.py 初期化・実行計画確定"] --> P1["Phase 1: 次タスク選定・ディスパッチ<br/>(派生元: Epic 統合ブランチ)"]
    P1 -->|pending / implementing| INNER["Inner Loop: run-dev-loop (単一または並列 worktree)<br/>base: Epic 統合ブランチ"]
    INNER -->|PR作成完了 (in_review)| P2["Phase 2: 別セッション／独立サブエージェントによる PR レビュー (review-pr)"]
    P2 -->|指摘あり (fixing_review)| FIX["run-dev-loop Phase 5: address-pr-review + verify_loop.ps1 -Full -BaseBranch <統合ブランチ>"]
    FIX -->|再レビュー依頼| P2
    P2 -->|指摘解消・承認| P3["Phase 3: Epic 統合ブランチへ直列マージ (Merge Queue)・epic_tracker.py 再評価"]
    INNER -->|停止条件到達 (blocked)| BLK["blocked 記録・影響範囲切り分け"]
    BLK -->|blocked 2件以上| HALT["Outer Loop 一時停止・ユーザーへエスカレーション"]
    BLK -->|独立タスク継続可| P1
    P3 -->|未完了子 Issue あり| P1
    P3 -->|全子 Issue merged| P4["Phase 4: origin/develop 同期 + verify_loop.ps1 -Full -BaseBranch develop<br/>統合 PR 作成 (base: develop) → ユーザー判断による最終マージ待ち"]
```

---

## ワークフロー手順

### Phase 0: Epic 統合ブランチ作成・初期化・実行計画確定・事前コンフリクト回避チェック
1. **Epic 統合ブランチの作成・リモート Push**（未作成の場合）：
   - 最新の `origin/develop` から `epic/<Epic番号>-<簡潔な説明>` を作成し、直ちにリモートへ Push する：
     ```powershell
     git checkout develop
     if ($?) { git pull origin develop }
     if ($?) { git checkout -b epic/<Epic番号>-<説明> }
     if ($?) { git push -u origin epic/<Epic番号>-<説明> }
     ```
2. `tools/epic_tracker.py` を `--integration-branch` 付きで実行し、Epic 本文から全子 Issue・実施順序・依存関係・並列グループ・統合ブランチ／統合 PR 状態・稼働中ワークツリー・並列スロット残数を取得・可視化する：
   ```powershell
   python tools/epic_tracker.py --epic <Epic番号> --integration-branch "epic/<Epic番号>-<説明>" --max-parallel 3
   ```
   - 状態スナップショットは `.agent_logs/epic_<Epic番号>_state.json` へ自動保存され、統合ブランチ名も記録されるため、セッションを跨いでも `--offline` または再実行で進捗を引き継げる（このファイルの更新は親オーケストレーター専用とする）。
3. ユーザーと以下の **運用モード** を確定する（未指定の場合は安全な既定値を採用する）：
   - **最大並列数**: 既定 `3`（上限 `3`、最小 `1`）。
   - **レビュー運用モード**:
     - **半自動（既定）**: PR 作成後、人間または別セッションでの `review-pr` 実施を待つ。
     - **自動**: 実装セッションとは**完全に独立したコンテキストの新規サブエージェント**を起動して `review-pr` を実行する（実装コンテキストを共有しない）。
   - **子 Issue PR → Epic 統合ブランチのマージ方針**:
     - **手動マージ（既定）**: 独立レビュー完了後、ユーザーが GitHub 上で Epic 統合ブランチへマージする。
     - **条件付き自動マージ**: ユーザーが明示承認した場合のみ、**独立コンテキストでの `review-pr` 完了済み**、かつ未解決レビュー指摘 0 件、かつ最新 `origin/<統合ブランチ>` 同期後の `tools/verify_loop.ps1 -Full -BaseBranch "<統合ブランチ>" -PreferOriginBase` 合格済みの子 Issue PR を **ローカル Merge Queue で 1 件ずつ直列に** Epic 統合ブランチへマージする。
   - **Epic 統合ブランチ → `develop` の最終マージ方針（固定・変更不可）**:
     - **常にユーザー判断（自動マージ禁止）**: 全子 Issue が統合ブランチへマージ完了し、統合 PR（`<統合ブランチ> -> develop`）を作成した後、**`develop` への最終マージは必ずユーザーによる手動確認・承認・マージを待つ**。
4. **事前コンフリクト回避チェック（事前スコープ分離）**:
   - 同一順序グループに複数の `pending` 子 Issue が存在する場合、並列着手前に各子 Issue の変更対象ファイルを確認する。
   - 以下の **ホットスポットファイル** に複数子 Issue の大規模変更が重複する見込みが高い場合は、無理に並列化せず直列実行へ切り替えるか、変更責務を明確に分離してから着手する：
     - `AudioEffector.sln`, `*.csproj`
     - `App.xaml.cs`, `MainViewModel.cs`
     - `設計/spec_map.json`
5. Epic 本文の依存関係に循環依存や不明な外部依存・残留ワークツリー・子 Issue PR の Base ブランチ不一致警告が出ている場合は、着手前に確認・整理する。

### Phase 1: 次タスク選定・並列ディスパッチ（最大 3 並列）
1. 次に実行すべきアクションを機械的に取得する：
   ```powershell
   python tools/epic_tracker.py --epic <Epic番号> --next --max-parallel 3 --json
   ```
2. `epic_tracker.py`（`compute_next_actions()`）の優先順位（`create_integration_branch` [priority 1] / `fix_pr_base` [priority 1] / `sync_branch` [コンフリクト: priority 1] > `sync_branch` [追従: priority 2] / `fix_review` [priority 2] > `ready_to_merge` [priority 3] > `review_pr` [priority 4] > `resume_implementing` [priority 5] > `start_issue` [priority 6]）と並列スロット残数（`available_slots = max(0, max_parallel - active_slots)`）に従ってディスパッチする：
   - **単一 `pending` 子 Issue の場合**:
     - 最新の **Epic 統合ブランチ（`origin/<統合ブランチ>`）** から `<Type>/<子Issue番号>-<説明>` を作成し、ベースブランチに `<統合ブランチ>` を指定して `run-dev-loop`（Inner Loop）を実行する。
   - **同一順序グループに複数の独立した `pending` 子 Issue があり並列実行する場合（最大 `available_slots` 件、上限 3 並列）**:
     - 親オーケストレーターが `tools/worktree_manager.ps1` に **`-BaseBranch "<統合ブランチ>"`** を指定して子 Issue ごとに `.agent_worktrees/issue-<子Issue番号>` を作成する：
       ```powershell
       .\tools\worktree_manager.ps1 -Action create -IssueNumber <子Issue番号> -BranchName "<Type>/<子Issue番号>-<説明>" -BaseBranch "<統合ブランチ>" -MaxParallel 3 -Json
       ```
     - 各並列ワーカーには **自担当ワークツリーパス内のみでの編集・ビルド・テスト・コミット・Push・PR 作成（`--base "<統合ブランチ>"`）** を厳命し、自ワークツリー配下の `.agent_logs/verify_issue_<子Issue番号>.json` のみ出力させる（親リポジトリ直下・他ワークツリー・`.agent_logs/epic_<番号>_state.json` への書き込み禁止）。
     - 同一の作業ディレクトリ上で複数子 Issue の編集・ビルド・テストを同時実行してはならない。

### Phase 2: 別セッションレビュー連携・指摘対応
1. `in_review` 状態になった子 Issue PR（Base: `<統合ブランチ>`）について、Phase 0 で確定したレビュー運用モードに従って `review-pr` を実施する：
   - **コンテキスト分離の厳守**: Outer Loop のオーケストレーター自身や実装担当セッション自身が直接 `review-pr` コメントを投稿してはならない。必ず別セッション、または実装コンテキストを引き継がない新規独立サブエージェントへ PR 番号・URL・対象 Epic 統合ブランチ・変更概要・重点確認観点を渡して実施する。
2. レビュー結果により `fixing_review`（未解決レビュー指摘あり / `CHANGES_REQUESTED` / CI 失敗）となった子 Issue PR は、実装側コンテキストで `run-dev-loop` の **Phase 5** を実行する：
   - `address-pr-review` スキルに従い全指摘の妥当性判定 → 修正 → **`.\tools\verify_loop.ps1 -Full -BaseBranch "<統合ブランチ>" -PreferOriginBase -JsonOut ".agent_logs\verify_review.json"`** → 分割追加コミット＆通常 Push（`--force` 禁止）→ 各コメント返信を行う。
   - 追従同期が必要な場合は `.\tools\sync_branch.ps1 -BaseBranch "<統合ブランチ>" -Json` を用いる。
   - 再レビューが必要な場合は再び独立コンテキストへ引き継ぐ（最大 5 往復）。

### Phase 3: ローカル Merge Queue（統合ブランチへの直列マージ）・追従同期・次サイクル遷移
1. 承認済み（`ready_to_merge`、かつ `base_branch` が `<統合ブランチ>` と一致）の子 Issue PR は、複数同時マージを行わず **必ず 1 件ずつ直列** で以下の手順を実施する：
   - **マージ前追従同期・再検証**: 対象 PR のワークツリー上で `.\tools\sync_branch.ps1 -BaseBranch "<統合ブランチ>" -Json` を実行する。`MERGED`（先行子 Issue PR 取り込みあり）となった場合は、`.\tools\verify_loop.ps1 -Full -BaseBranch "<統合ブランチ>" -PreferOriginBase` の全 5 ゲート合格を確認したうえで通常 Push する（`CONFLICT` 時は `loop_engineering_rules.md` Section 4 の自動解消基準または `-Abort` エスカレーションを適用）。
   - **統合ブランチへのマージ実行とワークツリー後始末（`--delete-branch` 禁止・安全順序の厳守）**:
     - 対象作業ブランチが `.agent_worktrees/issue-<子Issue番号>` でチェックアウトされた状態のまま `gh pr merge <PR番号> --merge --delete-branch` を実行すると、GitHub 上のマージは成功する一方でローカルブランチ削除がエラー（終了コード `1`）となり後続の自動化処理が中断する。また、対象ワークツリー配下にカレントディレクトリがあるままだと削除時のディレクトリロックやチェックアウト競合で失敗する。
     - そのため、以下の 4 ステップの順序を標準手順とする（Windows PowerShell 5.1 互換のため `&&` は使用せず、`if ($?)` または `$LASTEXITCODE` で制御すること）：
       1. 親リポジトリルートへ戻る（`Set-Location -LiteralPath "<親リポジトリルート>"`）
       2. `gh pr merge <PR番号> --merge`（**`--delete-branch` なし**）で GitHub 上の **Epic 統合ブランチ** へのマージを実行する
       3. `.\tools\worktree_manager.ps1 -Action remove -IssueNumber <子Issue番号> -Force -Json` でワークツリーを削除する
       4. `git branch -D "<作業ブランチ名>"` でローカル作業ブランチを削除し、必要に応じてリモート作業ブランチ削除（`git push origin --delete "<作業ブランチ名>"`）および `git fetch origin "<統合ブランチ>" --prune` を実行したうえで、`git checkout "<統合ブランチ>"` および `git pull origin "<統合ブランチ>"` を実行する
     ```powershell
     Set-Location -LiteralPath "<親リポジトリルート>"
     if ($?) {
         $prevEap = $ErrorActionPreference
         $ErrorActionPreference = "Continue"
         try {
             gh pr merge <PR番号> --merge
             if ($LASTEXITCODE -ne 0) { throw "gh pr merge に失敗しました (exit=$LASTEXITCODE)" }
             .\tools\worktree_manager.ps1 -Action remove -IssueNumber <子Issue番号> -Force -Json
             if (-not $? -or $LASTEXITCODE -ne 0) { throw "worktree 削除に失敗しました (exit=$LASTEXITCODE)" }
             git branch -D "<作業ブランチ名>"
             if ($LASTEXITCODE -ne 0) { throw "ローカル作業ブランチ削除に失敗しました (exit=$LASTEXITCODE)" }
             # リモート作業ブランチ削除が必要な場合（未削除なら削除）
             $null = git push origin --delete "<作業ブランチ名>" 2>&1
             if ($LASTEXITCODE -ne 0) { Write-Warning "リモート作業ブランチの削除をスキップしました（既に削除済みの可能性があります / exit=$LASTEXITCODE）" }
             $null = git fetch origin "<統合ブランチ>" --prune 2>&1
             if ($LASTEXITCODE -ne 0) { throw "git fetch origin <統合ブランチ> --prune に失敗しました (exit=$LASTEXITCODE)" }
             $null = git checkout "<統合ブランチ>" 2>&1
             if ($LASTEXITCODE -ne 0) { throw "git checkout <統合ブランチ> に失敗しました (exit=$LASTEXITCODE)" }
             $null = git pull origin "<統合ブランチ>" 2>&1
             if ($LASTEXITCODE -ne 0) { throw "git pull origin <統合ブランチ> に失敗しました (exit=$LASTEXITCODE)" }
         } finally {
             $ErrorActionPreference = $prevEap
         }
     }
     ```
     - **PowerShell `$ErrorActionPreference = "Stop"` 下での注意点**: `git push origin --delete`、`git fetch`、`git checkout`、`git pull` などの Git コマンドは正常終了時にも標準エラー出力（stderr）へ進捗・削除ログを出力する。PowerShell 5.1 で `$ErrorActionPreference = "Stop"` が有効な場合、stderr への正常ログが `NativeCommandError` として例外中断する原因となるため、上記のように一時的に `$ErrorActionPreference = "Continue"` へ切り替えたうえで出力リダイレクト（`2>&1`）と終了コード（`$LASTEXITCODE`）判定により成否を制御すること。
   - **残存並列 PR への順次追従同期**: 他に稼働中またはレビュー済みの並列 PR が残っている場合、`python tools/epic_tracker.py --epic <Epic番号> --next --max-parallel 3` を再実行して `sync_branch` / `ready_to_merge` 状態を再評価し、次の 1 件に対して追従同期 → 検証 → 統合ブランチへマージを順次繰り返す。
   - 孤立ディレクトリが残っている場合は `.\tools\worktree_manager.ps1 -Action prune -Json` で整理する（既定で稼働中ワークツリーの `.agent_build/` は保護される）。
2. 子 Issue が Inner Loop の停止条件に到達し `blocked` となった場合：
   - ローカル状態へ記録する（GitHub への不要な書き込みは行わない）：
     ```powershell
     python tools/epic_tracker.py --epic <Epic番号> --mark-blocked <子Issue番号> --reason "<停止理由>"
     ```
   - 該当子 Issue に依存する後続 Issue のみを保留（`waiting_deps`）とし、独立した他タスクがある場合は継続可否を判断したうえでユーザーへエスカレーションする。
   - **Epic 停止条件**: **連続して 2 つ以上の子 Issue（または未解消の `blocked` が 2 件以上）** が `blocked` となった場合は、Outer Loop 全体を一時停止し、原因・影響範囲・再開選択肢を添えてユーザーへ報告する。

### Phase 4: Epic 統合検証・`develop` 向け統合 PR 作成・ユーザー判断待ち
1. `tools/epic_tracker.py --epic <Epic番号>` で全子 Issue が統合ブランチへ `merged` になり、次アクションが `create_integration_pr` となったことを確認する。
2. 親リポジトリで Epic 統合ブランチをチェックアウトし、最新の `origin/develop` を非破壊同期したうえで、**`develop` 比較での最終統合検証**を実行し、全 5 ゲート合格（終了コード `0`）および `.agent_build/` 非残留を確認する：
   ```powershell
   git checkout "<統合ブランチ>"
   if ($?) { git pull origin "<統合ブランチ>" }
   if ($?) { .\tools\sync_branch.ps1 -BaseBranch "develop" -Json }
   if ($?) { .\tools\verify_loop.ps1 -Full -BaseBranch develop -PreferOriginBase -JsonOut ".agent_logs\verify_epic_final.json" }
   if ($?) { git push origin "<統合ブランチ>" }
   ```
   - `sync_branch.ps1` で `CONFLICT` が発生した場合は `loop_engineering_rules.md` Section 4 の基準に従い対応し、ロジック衝突や再検証連続 2 回失敗時は `-Abort` でロールバックしてユーザーへエスカレーションする。
   - 万一最終統合検証が失敗した場合は、統合 PR を作成せず直ちに停止して原因と影響範囲をユーザーへ報告する。
3. 検証合格後、Epic 統合ブランチから **`develop` 向けの統合 PR** を作成する（本文は一時ファイルに書き出し、使用後に削除）：
   ```powershell
   gh pr create --base develop --head "<統合ブランチ>" --title "[Epic] <Epicタイトル> (#<Epic番号>)" --body-file "<一時ファイル>"
   ```
   - 統合 PR 作成後に `origin/develop` が進んで `BEHIND` / `CONFLICTING` となった場合は、`epic_tracker.py` の `sync_integration_branch` 案内に従い、上記 2 の同期・検証・Push を再実施する。
4. **ユーザーへの最終マージ判断依頼（自動マージ禁止）**:
   - **エージェントが統合 PR を `develop` へ自動マージしてはならない。**
   - 以下の情報を添えて、ユーザーへ統合 PR の確認および `develop` マージ判断を依頼する：
     - 統合 PR URL・全子 Issue と紐づく子 Issue PR の完了サマリ
     - 最終統合検証（`verify_loop.ps1 -Full -BaseBranch develop -PreferOriginBase`）の結果サマリ
     - Epic 本文の残タスクチェックリスト更新案（自動編集せず、ユーザー承認後に更新）
     - Epic 横断の振り返り・改善ループ候補（詰まったゲート、手戻り要因、ルール／スキル／検証基盤の改善提案）
   - ユーザーが統合 PR を `develop` へマージ完了した後、統合ブランチ削除および Epic Issue クローズ準備（`complete_epic`）を実施する。

---

## 完了チェックリスト
- [ ] Phase 0 で `origin/develop` から Epic 統合ブランチ（`epic/<Epic番号>-<説明>`）を作成・Push し、`tools/epic_tracker.py --epic <Epic番号> --integration-branch "<統合ブランチ>"` で初期化した
- [ ] 全子 Issue の作業ブランチ・並列ワークツリー・PR Base・`sync_branch.ps1`・`verify_loop.ps1`・直列 Merge Queue をすべて Epic 統合ブランチ対象で運用し、完了後にワークツリーと `.agent_build/` をクリーンアップした
- [ ] 全子 Issue の公式 PR レビュー（`review-pr`）を実装とは独立したコンテキストで実施した
- [ ] `blocked` 発生時の切り分けと Epic 停止条件（2 件以上 `blocked` / 統合ブランチ検証または `develop` 最終検証失敗）を順守した
- [ ] 全子 Issue 完了後、統合ブランチへの `origin/develop` 同期と `.\tools\verify_loop.ps1 -Full -BaseBranch develop -PreferOriginBase` 合格を経て `develop` 向け統合 PR を作成した
- [ ] `develop` への最終マージを自動実行せず、統合 PR サマリ・検証結果・チェックリスト更新案・改善ループ候補を添えてユーザー判断を依頼した
