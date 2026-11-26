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
  - 進捗・依存関係トラッカー: `tools/epic_tracker.py`（状態スナップショット: `.agent_logs/epic_<番号>_state.json`）
  - 統合検証ハーネス: `tools/verify_loop.ps1`
  - Inner Loop: `run-dev-loop`（実装・検証・分割コミット・PR 作成・レビュー指摘対応）
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

### Phase 0: Epic 初期化・実行計画確定
1. `tools/epic_tracker.py` を実行し、Epic 本文から全子 Issue・実施順序・依存関係・並列グループ・現在のライブ状態を取得・可視化する：
   ```powershell
   python tools/epic_tracker.py --epic <Epic番号>
   ```
   - 状態スナップショットは `.agent_logs/epic_<Epic番号>_state.json` へ自動保存され、セッションを跨いでも `--offline` または再実行で進捗を引き継げる。
2. ユーザーと以下の **運用モード** を確定する（未指定の場合は安全な既定値を採用する）：
   - **レビュー運用モード**:
     - **半自動（既定）**: PR 作成後、人間または別セッションでの `review-pr` 実施を待つ。
     - **自動**: 実装セッションとは**完全に独立したコンテキストの新規サブエージェント**を起動して `review-pr` を実行する（実装コンテキストを共有しない）。
   - **マージ方針**:
     - **手動マージ（既定）**: レビュー完了後、ユーザーが GitHub 上で `develop` へマージする。
     - **条件付き自動マージ**: ユーザーが明示承認した場合のみ、未解決レビュー指摘 0 件かつ `tools/verify_loop.ps1 -Full` 合格済みの PR を `develop` へマージする。
3. Epic 本文の依存関係に循環依存や不明な外部依存の警告が出ている場合は、着手前にユーザーへ確認する。

### Phase 1: 次タスク選定・ディスパッチ
1. 次に実行すべきアクションを機械的に取得する：
   ```powershell
   python tools/epic_tracker.py --epic <Epic番号> --next --json
   ```
2. 優先順位（`fixing_review` > `in_review` > `implementing` > `pending`）に従ってディスパッチする：
   - **単一 `pending` 子 Issue の場合**:
     - 最新の `develop` ブランチから `<Type>/<子Issue番号>-<説明>` を作成し、`run-dev-loop`（Inner Loop）を実行する。
   - **同一順序グループに複数の独立した `pending` 子 Issue があり並列実行する場合**:
     - ワークスペース競合および `.agent_build/` 競合を防ぐため、必ず **`git worktree` またはワークスペース分離機能（`branch` / `share`）** を用いて子 Issue ごとに独立した作業ツリーを割り当てる。
     - 各作業ツリー内で独立に `run-dev-loop`（および `.\tools\verify_loop.ps1 -Full`）を実行する。
     - 同一の作業ディレクトリ上で複数子 Issue の編集・ビルド・テストを同時実行してはならない。

### Phase 2: 別セッションレビュー連携・指摘対応
1. `in_review` 状態になった PR について、Phase 0 で確定したレビュー運用モードに従って `review-pr` を実施する：
   - **コンテキスト分離の厳守**: Outer Loop のオーケストレーター自身や実装担当セッション自身が直接 `review-pr` コメントを投稿してはならない。必ず別セッション、または実装コンテキストを引き継がない新規独立サブエージェントへ PR 番号・URL・変更概要・重点確認観点を渡して実施する。
2. レビュー結果により `fixing_review`（未解決レビュー指摘あり / `CHANGES_REQUESTED` / CI 失敗）となった PR は、実装側コンテキストで `run-dev-loop` の **Phase 5** を実行する：
   - `address-pr-review` スキルに従い全指摘の妥当性判定 → 修正 → **`.\tools\verify_loop.ps1 -Full -JsonOut ".agent_logs\verify_review.json"`** → 分割追加コミット＆通常 Push（`--force` 禁止）→ 各コメント返信を行う。
   - 再レビューが必要な場合は再び独立コンテキストへ引き継ぐ（最大 5 往復）。

### Phase 3: マージ反映・次サイクル遷移
1. 子 Issue の PR が `develop` へマージされたら、以下を実施する：
   - ローカルの `develop` を最新化する（`git checkout develop; git pull origin develop`）。
   - 並列実行で作成した一時ワークツリーや不要な `.agent_build/` が残っていれば必ずクリーンアップする（`git worktree remove <path>; git worktree prune`）。
   - `python tools/epic_tracker.py --epic <Epic番号>` を再実行して依存解消された次の子 Issue を確認し、Phase 1 へ戻る。
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
