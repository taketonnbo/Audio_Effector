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
  - 進捗・依存関係・並列スロットトラッカー: `tools/epic_tracker.py`（状態スナップショット: `.agent_logs/epic_<番号>_state.json` ※親オーケストレーター単独書き込み）
  - ワークツリー管理ツール: `tools/worktree_manager.ps1`（`.agent_worktrees/issue-<番号>` の作成・一覧・削除・整理、最大 3 並列上限ガード）
  - ブランチ最新化・コンフリクト検知ツール: `tools/sync_branch.ps1`（非破壊マージ・コンフリクト検知 exit 2・`-DryRun`・`-Abort`）
  - 統合検証ハーネス: `tools/verify_loop.ps1`（`origin/develop` 自動優先解決・名前付き Mutex ビルド保護・ワークツリーレポート拡張）
  - Inner Loop: `run-dev-loop`（実装・検証・分割コミット・PR 作成・レビュー指摘対応・最新化同期）
  - 公式 PR レビュー: `review-pr`（※必ず実装とは独立したコンテキストで実行）
  - レビュー指摘対応: `address-pr-review`

---

## 全体フロー（2 階層ループ構成）

```mermaid
flowchart TD
    P0["Phase 0: Epic 初期化・実行計画確定 (epic_tracker.py)"] --> P1["Phase 1: 次タスク選定・ディスパッチ"]
    P1 -->|pending / implementing| INNER["Inner Loop: run-dev-loop (単一または並列 worktree)"]
    INNER -->|PR作成完了 (in_review)| P2["Phase 2: 別セッション／独立サブエージェントによる PR レビュー (review-pr)"]
    P2 -->|指摘あり (fixing_review)| FIX["run-dev-loop Phase 5: address-pr-review + verify_loop.ps1 -Full"]
    FIX -->|再レビュー依頼| P2
    P2 -->|指摘解消・承認| P3["Phase 3: develop へマージ反映・epic_tracker.py 再評価"]
    INNER -->|停止条件到達 (blocked)| BLK["blocked 記録・影響範囲切り分け"]
    BLK -->|blocked 2件以上| HALT["Outer Loop 一時停止・ユーザーへエスカレーション"]
    BLK -->|独立タスク継続可| P1
    P3 -->|未完了子 Issue あり| P1
    P3 -->|全子 Issue merged| P4["Phase 4: 最新 develop 統合検証 (verify_loop.ps1 -Full)・完了報告"]
```

---

## ワークフロー手順

### Phase 0: Epic 初期化・実行計画確定・事前コンフリクト回避チェック
1. `tools/epic_tracker.py` を実行し、Epic 本文から全子 Issue・実施順序・依存関係・並列グループ・PR マージ状態・稼働中ワークツリー・並列スロット残数を取得・可視化する：
   ```powershell
   python tools/epic_tracker.py --epic <Epic番号> --max-parallel 3
   ```
   - 状態スナップショットは `.agent_logs/epic_<Epic番号>_state.json` へ自動保存され、セッションを跨いでも `--offline` または再実行で進捗を引き継げる（このファイルの更新は親オーケストレーター専用とする）。
2. ユーザーと以下の **運用モード** を確定する（未指定の場合は安全な既定値を採用する）：
   - **最大並列数**: 既定 `3`（上限 `3`、最小 `1`）。
   - **レビュー運用モード**:
     - **半自動（既定）**: PR 作成後、人間または別セッションでの `review-pr` 実施を待つ。
     - **自動**: 実装セッションとは**完全に独立したコンテキストの新規サブエージェント**を起動して `review-pr` を実行する（実装コンテキストを共有しない）。
   - **マージ方針**:
     - **手動マージ（既定）**: レビュー完了後、ユーザーが GitHub 上で `develop` へマージする。
     - **条件付き自動マージ**: ユーザーが明示承認した場合のみ、**独立コンテキストでの `review-pr` 完了済み**、かつ未解決レビュー指摘 0 件、かつ最新 `origin/develop` 同期後の `tools/verify_loop.ps1 -Full -PreferOriginBase` 合格済みの PR を **ローカル Merge Queue で 1 件ずつ直列に** `develop` へマージする。
3. **事前コンフリクト回避チェック（事前スコープ分離）**:
   - 同一順序グループに複数の `pending` 子 Issue が存在する場合、並列着手前に各子 Issue の変更対象ファイルを確認する。
   - 以下の **ホットスポットファイル** に複数子 Issue の大規模変更が重複する見込みが高い場合は、無理に並列化せず直列実行へ切り替えるか、変更責務を明確に分離してから着手する：
     - `AudioEffector.sln`, `*.csproj`
     - `App.xaml.cs`, `MainViewModel.cs`
     - `設計/spec_map.json`
4. Epic 本文の依存関係に循環依存や不明な外部依存・残留ワークツリーの警告が出ている場合は、着手前に確認・整理する。

### Phase 1: 次タスク選定・並列ディスパッチ（最大 3 並列）
1. 次に実行すべきアクションを機械的に取得する：
   ```powershell
   python tools/epic_tracker.py --epic <Epic番号> --next --max-parallel 3 --json
   ```
2. `epic_tracker.py`（`compute_next_actions()`）の優先順位（`sync_branch` [コンフリクト: priority 1] > `sync_branch` [追従: priority 2] / `fix_review` [priority 2] > `ready_to_merge` [priority 3] > `review_pr` [priority 4] > `resume_implementing` [priority 5] > `start_issue` [priority 6]）と並列スロット残数（`available_slots = max(0, max_parallel - active_slots)`）に従ってディスパッチする：
   - **単一 `pending` 子 Issue の場合**:
     - 最新の `develop` ブランチから `<Type>/<子Issue番号>-<説明>` を作成し、`run-dev-loop`（Inner Loop）を実行する。
   - **同一順序グループに複数の独立した `pending` 子 Issue があり並列実行する場合（最大 `available_slots` 件、上限 3 並列）**:
     - 親オーケストレーターが `tools/worktree_manager.ps1` を用いて子 Issue ごとに `.agent_worktrees/issue-<子Issue番号>` を作成する：
       ```powershell
       .\tools\worktree_manager.ps1 -Action create -IssueNumber <子Issue番号> -BranchName "<Type>/<子Issue番号>-<説明>" -MaxParallel 3 -Json
       ```
     - 各並列ワーカーには **自担当ワークツリーパス内のみでの編集・ビルド・テスト・コミット・Push・PR 作成** を厳命し、自ワークツリー配下の `.agent_logs/verify_issue_<子Issue番号>.json` のみ出力させる（親リポジトリ直下・他ワークツリー・`.agent_logs/epic_<番号>_state.json` への書き込み禁止）。
     - 同一の作業ディレクトリ上で複数子 Issue の編集・ビルド・テストを同時実行してはならない。

### Phase 2: 別セッションレビュー連携・指摘対応
1. `in_review` 状態になった PR について、Phase 0 で確定したレビュー運用モードに従って `review-pr` を実施する：
   - **コンテキスト分離の厳守**: Outer Loop のオーケストレーター自身や実装担当セッション自身が直接 `review-pr` コメントを投稿してはならない。必ず別セッション、または実装コンテキストを引き継がない新規独立サブエージェントへ PR 番号・URL・変更概要・重点確認観点を渡して実施する。
2. レビュー結果により `fixing_review`（未解決レビュー指摘あり / `CHANGES_REQUESTED` / CI 失敗）となった PR は、実装側コンテキストで `run-dev-loop` の **Phase 5** を実行する：
   - `address-pr-review` スキルに従い全指摘の妥当性判定 → 修正 → **`.\tools\verify_loop.ps1 -Full -JsonOut ".agent_logs\verify_review.json"`** → 分割追加コミット＆通常 Push（`--force` 禁止）→ 各コメント返信を行う。
   - 再レビューが必要な場合は再び独立コンテキストへ引き継ぐ（最大 5 往復）。

### Phase 3: ローカル Merge Queue（直列マージ）・追従同期・次サイクル遷移
1. 承認済み（`ready_to_merge`）の PR は、複数同時マージを行わず **必ず 1 件ずつ直列** で以下の手順を実施する：
   - **マージ前追従同期・再検証**: 対象 PR のワークツリー上で `.\tools\sync_branch.ps1 -Json` を実行する。`MERGED`（先行 PR 取り込みあり）となった場合は、`.\tools\verify_loop.ps1 -Full -PreferOriginBase` の全 5 ゲート合格を確認したうえで通常 Push する（`CONFLICT` 時は `loop_engineering_rules.md` Section 4 の自動解消基準または `-Abort` エスカレーションを適用）。
   - **マージ実行とワークツリー後始末**: PR を `develop` へマージしたら、**必ず親リポジトリルートへ戻ったうえで**以下を実行する（対象ワークツリー配下にカレントディレクトリがあるままだと削除時のディレクトリロックや `develop` チェックアウト競合で失敗するため）：
     ```powershell
     Set-Location -LiteralPath "<親リポジトリルート>"
     if ($?) { .\tools\worktree_manager.ps1 -Action remove -IssueNumber <子Issue番号> -Force -Json }
     if ($?) { git checkout develop }
     if ($?) { git pull origin develop }
     ```
   - **残存並列 PR への順次追従同期**: 他に稼働中またはレビュー済みの並列 PR が残っている場合、`python tools/epic_tracker.py --epic <Epic番号> --next --max-parallel 3` を再実行して `sync_branch` / `ready_to_merge` 状態を再評価し、次の 1 件に対して追従同期 → 検証 → マージを順次繰り返す。
   - 孤立ディレクトリが残っている場合は `.\tools\worktree_manager.ps1 -Action prune -Json` で整理する（既定で稼働中ワークツリーの `.agent_build/` は保護される）。
2. 子 Issue が Inner Loop の停止条件に到達し `blocked` となった場合：
   - ローカル状態へ記録する（GitHub への不要な書き込みは行わない）：
     ```powershell
     python tools/epic_tracker.py --epic <Epic番号> --mark-blocked <子Issue番号> --reason "<停止理由>"
     ```
   - 該当子 Issue に依存する後続 Issue のみを保留（`waiting_deps`）とし、独立した他タスクがある場合は継続可否を判断したうえでユーザーへエスカレーションする。
   - **Epic 停止条件**: **連続して 2 つ以上の子 Issue（または未解消の `blocked` が 2 件以上）** が `blocked` となった場合は、Outer Loop 全体を一時停止し、原因・影響範囲・再開選択肢を添えてユーザーへ報告する。

### Phase 4: Epic 完了検証・クローズ準備
1. `tools/epic_tracker.py --epic <Epic番号>` で全子 Issue が `merged` になったことを確認する。
2. 最新の `develop` ブランチ上で最終統合検証を実行し、全ゲート合格（終了コード `0`）および `.agent_build/` 非残留を確認する：
   ```powershell
   .\tools\verify_loop.ps1 -Full -JsonOut ".agent_logs\verify_epic_final.json"
   ```
   - 万一 `develop` 統合検証が失敗した場合は、Epic を完了扱いにせず直ちに停止して原因と影響範囲をユーザーへ報告する。
3. 完了報告として以下をユーザーへ提示する：
   - 全子 Issue と紐づく PR の完了サマリ
   - Epic 本文の残タスクチェックリスト更新内容（自動編集せず、更新案提示またはユーザー承認後に更新）
   - Epic 横断の振り返り・改善ループ候補（詰まったゲート、手戻り要因、ルール／スキル／検証基盤の改善提案）

---

## 完了チェックリスト
- [ ] Phase 0 で `tools/epic_tracker.py --epic <Epic番号>` により子 Issue・依存関係・運用モードを確定した
- [ ] 並列実行時に独立ワークツリーで隔離し、完了後にワークツリーと `.agent_build/` をクリーンアップした
- [ ] 全子 Issue の公式 PR レビュー（`review-pr`）を実装とは独立したコンテキストで実施した
- [ ] `blocked` 発生時の切り分けと Epic 停止条件（2 件以上 `blocked` / `develop` 統合検証失敗）を順守した
- [ ] 全子 Issue マージ後、最新 `develop` で `.\tools\verify_loop.ps1 -Full` が終了コード `0` で合格した
- [ ] Epic 完了サマリ・チェックリスト更新案・改善ループ候補をユーザーへ提示した
