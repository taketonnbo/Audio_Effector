---
name: run-dev-loop
description: Issue 着手から実装・統合検証（tools/verify_loop.ps1）・修正反復・分割コミット・PR 作成・別セッションレビュー引き継ぎ・PR レビュー指摘対応（address-pr-review）・振り返り改善ループまでを自律反復する標準開発ワークフロースキル。/goal 等と組み合わせて Issue 対応を一貫推進する際に使用する。
---

# 自律開発ループ実行スキル (run-dev-loop)

このスキルは、Audio Effector リポジトリにおいて **Issue 着手から PR 作成・PR レビュー指摘対応・改善ループまで** を、安全・高速・再現可能に自律反復するための標準ワークフローを定義します。

本スキルを実行する際は、必ず以下の前提ルールおよび連携スキルを遵守してください：
- **必須ルール**: `.agents/rules/loop_engineering_rules.md`（Verifier 保護・停止条件・ロールバック・コンテキスト分離・改善ループ）
- **関連ルール**: `.agents/rules/branch_operation_rules.md`, `git_operation_rules.md`, `build_rules.md`, `test_rules.md`, `issue_pr_rules.md`
- **連携スキル**: `create-pr-from-issue`, `address-pr-review`, `sync-specs`, `check-equivalence`（※公式 PR レビュー `review-pr` は別セッションで実施）

---

## 全体フロー

```mermaid
flowchart TD
    P0["Phase 0: 準備・Done定義固定・ブランチ作成"] --> P1["Phase 1: 実装・単体テスト追加"]
    P1 --> P2["Phase 2: 統合検証 (verify_loop.ps1)"]
    P2 -->|FAIL & 停止条件未満| P3["Phase 3: 原因分析・修正反復"]
    P3 --> P2
    P2 -->|停止条件到達| ESC["安全停止・ユーザーへエスカレーション"]
    P2 -->|PASS (Full Gate)| P4["Phase 4: 分割コミット・PR作成・別セッションレビュー引き継ぎ"]
    P4 --> EXT["別セッション / レビュアーによる PR レビュー (review-pr)"]
    EXT -->|指摘あり| P5["Phase 5: PRレビュー指摘対応ループ (address-pr-review + verify_loop.ps1 -Full)"]
    P5 -->|再レビュー依頼| EXT
    EXT -->|未解決コメント 0| P6["Phase 6: 振り返り・改善ループ"]
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
3. 最新の `develop` ブランチから命名規則（`<Type>/<Issue番号>-<簡潔な説明>`）に従って作業用ブランチを作成する。
   ```powershell
   git checkout develop
   git pull origin develop
   git checkout -b <Type>/<Issue番号>-<説明>
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
   - 実装と仕様書更新が完了したら、必ず `-Full` モードで全ゲートを実行し、終了コード `0` を確認する。
   ```powershell
   .\tools\verify_loop.ps1 -Full -JsonOut ".agent_logs\verify_final.json"
   ```
   - 検証完了後、`.agent_build/` が残留していないことを確認する。

### Phase 3: 原因分析・修正反復
- Phase 2 が失敗（終了コード非 `0`）した場合、標準出力および `.agent_logs/verify_*.json` から失敗ゲート・エラー原因を特定し、**失敗したゲートのみを的確に修正**して Phase 2 を再実行する。
- **厳守事項（`loop_engineering_rules.md`）**:
  - テスト削除・`Skip` 化・アサーション弱体化・`verify_loop.ps1` の基準緩和は**絶対に行わない**。
  - **同一原因の失敗が連続 3 回**、または**実装ループが 10 回**に達した場合は直ちに安全停止し、原因仮説・試した対策・選択肢を添えてユーザーへ報告する。
  - 要件や仕様の判断が必要な場合も無理に進めず安全停止して確認する。

### Phase 4: 分割コミット・PR 作成・別セッションレビュー引き継ぎ
1. `create-pr-from-issue` スキルおよび `git_operation_rules.md` に従い、レイヤー・責務・変更種別ごとにファイルを指定して分割コミットする（`git add .` による巨大単一コミットは禁止）。
   - コミットメッセージ形式: `<Type>: <Emoji> #<Issue番号> <現在形の日本語Subject>`
   - コミット後、`git log develop..HEAD --stat` で粒度の偏りがないか確認する。
2. リモートへプッシュし、`gh pr create --base develop --title "[種別] ..." --body-file <一時ファイル>` で PR を作成する（使用後に一時ファイルを削除）。
   - PR 本文には `Closes #<Issue番号>`、概要、対応内容、検証結果、仕様書同期結果、スコープ差分を記載する。
3. **別セッションレビューへの引き継ぎ（コンテキスト分離の原則）**:
   - 確証バイアス防止のため、**この実装セッション自身で公式の PR レビューコメント投稿（`review-pr`）を行わない**。
   - ユーザーに対し、別セッションで `review-pr` を実行できるよう以下を明示して引き継ぐ：
     - PR 番号および URL
     - 変更概要とコミット構成
     - 重点的に確認してほしい観点（仕様差分・境界ケース・パフォーマンス・UI 等）

### Phase 5: PR レビュー指摘対応ループ
別セッション（またはレビュアー）から PR にレビューコメントが投稿された後、以下のループを未解決コメントがゼロになるまで反復する（最大 5 往復）：
1. `address-pr-review` スキルの手順に従い、レビュー総評・全インラインコメント・通常コメントを取得し、コードベースと照合して妥当性を判定する（妥当 / 一部妥当・代替案 / 妥当でない・スコープ外）。
2. 妥当と判定した指摘を修正し、必要に応じて単体テスト・仕様書を更新する。
3. **`.\tools\verify_loop.ps1 -Full -JsonOut ".agent_logs\verify_review.json"` を実行**し、全ゲート合格（終了コード `0`）を確認する。
4. 対応単位ごとに分割コミットして通常プッシュ（`git push origin <ブランチ名>`、`--force` 禁止）を行う。
5. 各インラインコメントへの個別返信（判定結果・修正内容・コミットハッシュ）および総括コメントを投稿し、必要なら PR 本文を更新する。
6. `git status -sb` と `git log origin/<ブランチ名>..HEAD --oneline` が空であることを確認し、再レビューが必要な場合は再び別セッションへ引き継ぐ。

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
- [ ] 公式 PR レビューは別セッションへ引き継ぎ、コンテキスト分離を守った
- [ ] レビュー指摘対応後、全コメント返信・リモート同期・作業ツリークリーンを確認した
- [ ] 振り返りと改善候補をユーザーへ提示した
