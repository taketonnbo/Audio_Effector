---
description: AI エージェントによる自律開発ループ（ループエンジニアリング）運用時の必須ガードレール（Verifier 保護・停止条件・ロールバック・コンテキスト分離・改善ループ）
---

# ループエンジニアリング運用ルール (Loop Engineering Rules)

本プロジェクト（Audio Effector）において、AI エージェントが「実装 → 検証 → 修正 → PR 作成 → レビュー指摘対応 → 収束判定」を自律反復する **ループエンジニアリング（Agentic Loop）** を安全・高速・再現可能に運用するための必須ルールを定めます。

本ルールは既存のプロジェクトルール（[build_rules.md](build_rules.md)、[test_rules.md](test_rules.md)、[branch_operation_rules.md](branch_operation_rules.md)、[git_operation_rules.md](git_operation_rules.md)、[issue_pr_rules.md](issue_pr_rules.md)）と整合して適用されます。

---

## 1. 統合検証ハーネス（Verifier）の運用基準

自律ループの各イテレーションにおける完了判定は、単一の検証ハーネス **`tools/verify_loop.ps1`** を用いて機械的に行います。

- **途中イテレーション（高速反復）**:
  - 実装・修正の途上では、`-SkipSpecSync` や `-TestFilter "<条件>"` を用いて対象領域を高速に反復検証して構いません。
  - 検証ログや構造化結果 JSON は `.agent_logs/` 配下へ出力します（例: `-JsonOut ".agent_logs/verify_iter.json"`）。
- **最終完了ゲート（PR 作成前・レビュー指摘対応完了前）**:
  - **必ず `.\tools\verify_loop.ps1 -Full` を実行**し、隔離ビルド（エラー 0・警告 0）・全単体テスト合格・仕様書同期（未更新 Primary 0 件）・XAML バインディング等価性チェック・Python ツール単体テスト（`tools/tests`）・サードパーティライセンス記載同期チェック（`tools/check_license_sync.py --strict`、`THIRD-PARTY-NOTICES.md` 更新要否・`PublishSingleFile` LGPL リスク・`bin/` DLL 誤追跡）の全 6 ゲートが終了コード `0` で合格することを確認します。
  - 最終ゲートでの `-SkipSpecSync`・`-SkipLicenseSync`・`-TestFilter` による絞り込みは禁止します。

---

## 2. Verifier 保護（検証基準の改ざん禁止）

見かけ上の「全ゲート合格」を作り出すための検証基準の改ざんを**一切禁止**します。

- **テストの改ざん禁止**:
  - 失敗したテストケースの削除、`Skip` 属性の付与、アサーション条件の弱体化・コメントアウトを行ってはなりません。
  - テストを通す目的で、仕様に反する形へプロダクションコードや既存テストの期待値を書き換えてはなりません（[test_rules.md](test_rules.md) 準拠）。
- **検証ハーネスの改ざん禁止**:
  - タスクのスコープ外で `tools/verify_loop.ps1`、`tools/check_spec_sync.py`、`tools/check_equivalence.py`、`tools/check_license_sync.py` の判定基準（警告 0 必須、Primary 仕様書チェック、ライセンス記載同期チェック、エラー終了コード等）を緩和・無効化してはなりません。
- **静的解析・コンパイラ警告の安易な抑制禁止**:
  - 根本原因を解消せず `#pragma warning disable` や `SuppressMessage` で警告を握りつぶしてはなりません（設計上やむを得ない場合は根拠を明記してレビュー対象とします）。

---

## 3. 停止条件・エスカレーション基準

自律ループが無限反復や誤った方向への暴走に陥るのを防ぐため、以下のいずれかに該当した時点で**直ちにループを安全停止**し、ユーザーへエスカレーションします。

| 条件カテゴリ | 停止基準 | エスカレーション時に報告すべき内容 |
| :--- | :--- | :--- |
| **同一失敗の連続** | 同一原因によるビルド／テスト／静的検証の失敗が **連続 3 回** 発生した | 失敗ゲート名、エラー抜粋、立てた原因仮説、試した対策 3 件の結果、次に考えられる選択肢 |
| **イテレーション上限** | 実装ループが **最大 10 回**、または PR レビュー指摘対応ループが **最大 5 往復** に達した | 現在の到達点、残存している未解決項目、継続／スコープ分割の提案 |
| **判断の必要性** | 要件の曖昧さ、既存仕様との衝突、破壊的変更の是非、レビュー方針のトレードオフ判断が必要になった | 論点、取り得る設計案の比較（メリット・デメリット）、推奨案 |
| **環境・外部要因** | ツール障害、権限不足、ネットワーク断、ハードウェア／ドライバ依存で自動検証が継続できない | 発生エラーと影響範囲、ユーザー側での確認・対応依頼事項 |

---

## 4. 安全装置（ロールバック可能性とハング防止）

- **作業用ブランチの必須作成**:
  - ループ開始前に、単一 Issue の場合は最新の `develop` ブランチから、Epic 配下の子 Issue の場合は当該 Epic の統合ブランチ（`epic/<Epic番号>-<説明>`）から、[branch_operation_rules.md](branch_operation_rules.md) に従った作業用ブランチ（`<Type>/<Issue番号>-<簡潔な説明>`）を作成します。`develop` や `main` 上で直接ループを回してはなりません。
- **チェックポイントコミットとロールバック**:
  - 意味のある単位（例: ドメイン実装、ViewModel 実装、UI 実装、テスト追加）で検証が通った段階で小さくコミットし、失敗時に直前の安定状態へ安全に巻き戻せる状態を保ちます。
  - 既にリモートへプッシュ済みかつレビューコメントが紐づく PR では、履歴書き換え（`git push --force`）を行わず、追加コミットで修正を積みます。
- **ハング防止と一時ファイル管理**:
  - ビルド・テストの長時間ハングを防ぐため、[build_rules.md](build_rules.md) のタイムアウト管理・即時 `kill` ルールを厳守します。`tools/verify_loop.ps1` のタイムアウト保護（`-BuildTimeoutSeconds` / `-TestTimeoutSeconds`）および並列ビルド競合保護 Mutex（`-BuildLockTimeoutSeconds`、通常の検証での `-NoBuildLock` 指定禁止）を無効化してはなりません。
  - 実行終了後（正常・異常問わず）に `.agent_build/` が残留していないことを確認します。
  - ループ実行時のログや JSON レポートは `.gitignore` 登録済みの `.agent_logs/` 配下にのみ保存し、`git status` やコミットへ混入させてはなりません。
- **ブランチ最新化と自動コンフリクト解消基準**:
  - Push 済み・レビュー紐づき PR の履歴保全のため、`git rebase` や `git push --force` は使用せず、`tools/sync_branch.ps1` による非破壊マージ（`git merge --no-edit origin/<BaseBranch>`）を標準とします。
  - 追従対象の `-BaseBranch` は、通常 Issue では `develop`、Epic 子 Issue では当該 Epic の統合ブランチ（`epic/<Epic番号>-<説明>`）、Epic 統合ブランチの最終同期時は `develop` を指定します。
  - コンフリクト発生時（`tools/sync_branch.ps1` 終了コード `2` / `status: "CONFLICT"`）の対応境界は以下の通りとします：
    - **自動解消を許可する範囲**: `using` / `import` の独立追記、`.csproj` や `設計/spec_map.json` の独立エントリ追加、DI 登録・ファクトリ登録の独立追記、テストケースの独立追記など、意図が明確かつ相互独立なマージ衝突。
    - **即時ロールバック（`tools/sync_branch.ps1 -Abort` / `git merge --abort`）しユーザーへエスカレーションすべき範囲**: 同一メソッド・同一業務ロジックの意味的衝突、アーキテクチャや仕様判断を伴う衝突、バイナリ・生成物の衝突、または自動解消後の `tools/verify_loop.ps1 -Full` が **連続 2 回失敗** した場合。
  - コンフリクトを自動解消した後は、コミット・Push 前に **必ず `.\tools\verify_loop.ps1 -Full -BaseBranch "<BaseBranch>" -PreferOriginBase` の全 6 ゲート合格（終了コード `0`）** を再確認します。
- **シェル構文の互換性（Windows PowerShell 5.1 準拠）**:
  - 本環境の既定シェルは Windows PowerShell 5.1（`&&` 演算子非対応）のため、コマンド連結やスキル内手順では `&&` を使用せず、`; if ($?) { ... }` または `$LASTEXITCODE` チェックを用いて前段失敗時の連鎖実行を防止します。
  - `tools/` 配下に `.ps1` スクリプトを追加・改修する際は、以下の Windows PowerShell 5.1 固有パターンを必須順守します（`tools/tests/test_powershell_encoding.py` で機械検証）：
    - 文字コードは **UTF-8 (BOM 付き)**・改行コード **CRLF** で保存し、PS 5.1 パーサーで構文エラー 0 件であること。
    - `git diff --name-only` 等でファイルパスを取得する場合は、日本語パス（`設計/` 配下等）の 8 進エスケープを防ぐため `-c core.quotepath=false` を併用すること。
    - `[System.IO.File]::WriteAllText` や `[System.IO.Path]::GetFullPath` へ相対パス引数（`-JsonOut` 等）を渡す際は、PowerShell の `Set-Location` と .NET `CurrentDirectory` の不一致を防ぐため `$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(...)` で解決すること。

---

## 5. コンテキスト分離の原則（独立エージェントによる PR レビュー実施）

実装を担当したエージェント自身がそのまま公式の PR レビューを行うと、実装時の前提・思い込みを引きずる **確証バイアス** により見落としが発生します。これを防ぐため、以下のコンテキスト分離を徹底します。

1. **実装エージェントの責務**:
   - Issue 調査、実装、単体テスト、`tools/verify_loop.ps1 -Full` による機械検証、分割コミット、PR 作成（単一 Issue は Base: `develop`、Epic 子 Issue は Base: 当該 Epic 統合ブランチ `epic/<Epic番号>-<説明>`）までを実施します。
   - PR 作成後は、**実装時の会話履歴・前提を引き継がない独立したエージェント（新規独立サブエージェント、または別セッション）**へ PR 番号・URL・変更概要を渡して公式 PR レビュー（`review-pr`）を実行し、**実装 → 独立レビュー → 指摘対応 → 再レビュー → 収束**まで一気通貫でループを回します。
2. **公式 PR レビューは必ず独立エージェントで実施**:
   - `review-pr` スキルを用いた GitHub PR への公式レビューコメント投稿は、**実装エージェントとは独立したコンテキスト（独立サブエージェントまたは別セッション）**で行います。
   - **2 回目以降の再レビューで指摘が 0 件の場合でも、必ずレビューコメント（前回指摘の解消確認・新規指摘なし・LGTM / マージ可能判定）を PR へ投稿**します。
3. **レビュー指摘対応の責務**:
   - 独立エージェント（または人間のレビュアー）から投稿された指摘に対し、実装エージェント側で `address-pr-review` スキルに従い、妥当性判定 → 修正 → `tools/verify_loop.ps1 -Full` 再検証 → コミット＆Push → 各コメント返信を実施します。
   - 修正後は再度独立エージェントへ再レビューを依頼し、指摘 0 件のレビューコメントが得られた時点で収束（マージ可能）とします。

---

## 6. 改善ループ（メタ改善）

自律開発ループの完了時または中断時には、単発の実装で終わらせず、開発基盤そのものを継続的に改善するための振り返りを行います。

1. **振り返り観点の抽出**:
   - どのゲートで何回詰まったか（ビルド警告、テスト不安定、仕様書同期漏れ、XAML バインディング不整合、ライセンス記載同期漏れ等）
   - 手戻りやレビュー指摘の根本原因は何だったか
   - パッケージ・外部モデル・外部素材の追加／更新に伴う `THIRD-PARTY-NOTICES.md` の記載更新漏れや配布形式リスク（`PublishSingleFile` と LGPL 等）がなかったか
   - 既存ルール・スキル・検証スクリプトで事前に検知できたはずの盲点はなかったか
2. **ルール・スキル・ハーネスへの還元**:
   - 再発防止に有効な知見があれば、以下の候補を簡潔に整理してユーザーへ提案します（自動改変せず、ユーザーの合意を得て反映します）：
     - `.agents/rules/*.md` へのルール追記
     - `.agents/skills/*/SKILL.md` の手順・チェックリスト補強
     - `tools/verify_loop.ps1` や補助検証スクリプトの検知項目追加

---

## 7. Epic 運用ルール（Outer Loop ガードレール）

複数の子 Issue を束ねる Epic Issue を自律推進する外側ループ（Outer Loop: `run-epic-loop` / `tools/epic_tracker.py`）では、単一 Issue 用のルール（Section 1〜6）に加えて以下のガードレールを厳守します。

### 7.0 Epic 統合ブランチ運用の原則と最終マージのユーザー判断必須
- **統合ブランチの先行作成・Push**:
  - Epic 着手時（Phase 0）に、まず最新の `origin/develop` から **Epic 統合用ブランチ**（`epic/<Epic番号>-<簡潔な説明>`）を作成してリモート（`origin`）へ Push します。
  - Epic 実装途上の子 Issue 変更を直接 `develop` へ流入させてはなりません。
- **子 Issue の全操作ベースを統合ブランチへ統一**:
  - Epic 配下の全子 Issue について、作業用ブランチの派生元（単一・並列 worktree とも）、PR の Base ブランチ（`gh pr create --base <統合ブランチ>`）、`tools/sync_branch.ps1 -BaseBranch "<統合ブランチ>"` の追従先、`tools/verify_loop.ps1 -BaseBranch "<統合ブランチ>" -PreferOriginBase` の比較ベース、および Section 7.4「ローカル Merge Queue（直列マージ）」のマージ先を **すべて Epic 統合ブランチ** へ統一します。
- **最終 `develop` マージの自動実行禁止（ユーザー判断必須）**:
  - 全子 Issue が Epic 統合ブランチへマージ完了した後、統合ブランチへ最新 `origin/develop` を非破壊同期し、`.\tools\verify_loop.ps1 -Full -BaseBranch develop -PreferOriginBase` の全 6 ゲート合格（Epic 全体の `origin/develop` 差分に対する `tools/check_license_sync.py` のライセンス記載更新要否チェックを含む）を確認したうえで、**`<統合ブランチ> -> develop` の統合 PR を作成**します。
  - 統合 PR 本文およびユーザーへの報告には、`THIRD-PARTY-NOTICES.md` の更新要否確認結果（更新有無または不要理由）を必ず明記します。
  - **Epic 統合ブランチ → `develop` への最終マージは自動実行を一切禁止**します。必ずユーザーによる確認・承認・手動マージを待つこと。

### 7.1 進捗追跡・セッション跨ぎ再開と状態ファイルの単一書き込み原則
- Epic 配下の各子 Issue の状態（`pending`, `waiting_deps`, `implementing`, `in_review`, `fixing_review`, `merged`, `blocked`）、統合ブランチおよび統合 PR（`<統合ブランチ> -> develop`）の状態、PR マージ可否（`CLEAN` / `BEHIND` / `CONFLICTING`）、稼働中ワークツリー、および並列スロット残数は、`tools/epic_tracker.py --epic <Epic番号> --integration-branch "<統合ブランチ>" --max-parallel 3` を用いて機械的に同期・判定します（2 回目以降は状態ファイル記録値または `epic/<Epic番号>-*` から自動解決可能）。
- **状態ファイルの単一書き込み原則**: `.agent_logs/epic_<Epic番号>_state.json` の読み書きは **Outer Loop（親オーケストレーター）の単独責務** とします。並列稼働する各ワーカーは親の Epic 状態ファイルを直接編集・上書きしてはならず、自ワークツリー配下の検証ログ（例: `.agent_logs/verify_issue_<子Issue番号>.json`）のみを出力します。
- トラッカー自体は読み取り専用（GitHub Issue/PR への書き込みを行わない）として運用し、`blocked` の記録・解除もローカル状態スナップショット上で行います。

### 7.2 最大並列数上限とブランチ・ワークツリー衛生管理
- **最大並列数上限**: 同時並列実行数は **最大 3 並列**（既定値 `3`）を上限とします。上限を超える新規ワークツリー作成および並列ディスパッチを行ってはなりません。
- **ワークツリー標準配置と一元管理**:
  - 同一順序グループに属する独立した子 Issue を並列実行する場合、**同一の作業ディレクトリ上で複数ブランチの編集・ビルド・テストを混在させてはなりません**。
  - 並列作業は必ず `.gitignore` 済みの `.agent_worktrees/issue-<子Issue番号>` を標準配置先とし、`tools/worktree_manager.ps1`（`create -BaseBranch "<統合ブランチ>"` / `list` / `remove` / `prune`）を用いて一元管理します。
  - 隔離ビルドディレクトリ（`.agent_build/`）は各作業ツリーのルート配下に独立作成し、Gate 1 ビルド工程は `tools/verify_loop.ps1` の名前付き Mutex（`Local\AudioEffector_VerifyLoop_BuildLock`）で安全に直列化します。
  - ワークツリー内での `tools/verify_loop.ps1 -BaseBranch "<統合ブランチ>"` 実行時は、`origin/<統合ブランチ>` を比較ベースとして自動優先（`-PreferOriginBase` 相当）し、親ローカルブランチの古さによる比較ズレを防ぎます。
  - 子 Issue の PR マージ完了後または中断時には、`tools/worktree_manager.ps1 -Action remove -IssueNumber <子Issue番号> -Force` で対象ワークツリーおよび `.agent_build/` を速やかに削除します（`.agent_logs/` 等の未追跡ログがワークツリー内に残るため `-Force` を併用）。また、`prune` は既定で稼働中ワークツリーの `.agent_build/` を保護します。

### 7.3 Epic 進行時のコンテキスト分離徹底
- Epic のオーケストレーション中であっても、Section 5（コンテキスト分離の原則）を一切緩和してはなりません。
- Outer Loop のオーケストレーターおよび子 Issue の実装セッションは、担当した PR に対して直接 `review-pr` を実行・コメント投稿してはなりません。
- 自動レビュー運用モードを選択した場合でも、必ず **実装時の会話履歴・前提を引き継がない独立セッションまたは新規独立サブエージェント** を起動して `review-pr` を実施します。

### 7.4 マージ直列化（ローカル Merge Queue）
- 子 Issue の実装・検証および独立エージェントによる PR レビューは **最大 3 並列** で並行推進して構いませんが、**Epic 統合ブランチ（`epic/<Epic番号>-<説明>`）へのマージは必ず 1 件ずつ直列** で実施します。
- 各子 Issue PR のマージ直前には以下の順序を厳守し、複数 PR の同時マージによる論理コンフリクト（統合ブランチ破損）を防止します：
  1. 対象ワークツリー上で `.\tools\sync_branch.ps1 -BaseBranch "<統合ブランチ>" -Json` を実行し、最新の `origin/<統合ブランチ>` を非破壊マージで取り込む（`CONFLICT` 時は Section 4 の自動解消基準または `-Abort` エスカレーションに従う）。
  2. 取り込みが発生した（`MERGED`）場合、対象ワークツリー上で `.\tools\verify_loop.ps1 -Full -BaseBranch "<統合ブランチ>" -PreferOriginBase` の全 6 ゲート合格を確認し、マージコミットを通常 Push する。
  3. PR を **Epic 統合ブランチ** へマージし、直ちに `tools/worktree_manager.ps1 -Action remove -IssueNumber <子Issue番号> -Force` でワークツリーを削除し、親リポジトリの統合ブランチを最新化する。
  4. 残存する他並列 PR に対して次のマージキュー順番で 1〜3 を順次繰り返す。
- 全子 Issue のマージ完了後、Epic 統合ブランチから `develop` への最終反映は統合 PR 経由とし、Section 7.0 に従い **自動マージを行わずユーザー判断を待つ** こと。

### 7.5 Epic レベルの停止条件・エスカレーション基準
Inner Loop 単体の停止条件（Section 3）に加え、以下のいずれかに該当した時点で、該当する停止範囲に従い **Outer Loop 全体の一時停止** または **依存後続 Issue の部分保留** を行い、ユーザーへエスカレーションします。

| 条件カテゴリ | Epic 停止基準 | 停止範囲 | エスカレーション時に報告すべき内容 |
| :--- | :--- | :--- | :--- |
| **連続・複数ブロック** | 連続して **2 つ以上の子 Issue**（または未解消の `blocked` が累計 2 件以上）が `blocked` となった | Outer Loop 全体を一時停止 | 各 `blocked` 子 Issue の番号・停止理由、依存する後続 Issue の影響範囲、再開・方針変更の選択肢 |
| **統合検証失敗** | 子 Issue マージ後の Epic 統合ブランチ検証、または最終統合 PR 作成前・更新時の `origin/develop` 同期および `tools/verify_loop.ps1 -Full -BaseBranch develop -PreferOriginBase` が失敗した | Outer Loop 全体を一時停止 | 失敗ゲート名、エラー抜粋、直前にマージされた PR や `origin/develop` 差分との関連性、ロールバック／修正方針の提案 |
| **単一子 Issue のブロック** | 1 件の子 Issue が `blocked` となった | 依存する後続 Issue のみ保留（独立タスクは継続可否を判断） | 依存する後続 Issue の影響範囲、独立タスクの継続可否、該当 Issue の判断事項 |
