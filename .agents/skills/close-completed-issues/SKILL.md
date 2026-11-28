---
name: close-completed-issues
description: オープン中のGitHub Issueを全件棚卸しし、マージ済みPR・origin/developのGit履歴・実コードベースおよび仕様書と照合して対応済みの通常Issueを安全にクローズするとともに、オープン中のEpic Issueについてはクローズせず本文の進捗記載（子Issue一覧表・残タスクチェックリスト・進行中状況・期待される動作）を最新化するスキル。完了済み未クローズIssueの確認・一括クローズや現状Issueの進捗更新を依頼された際に使用する。
---

# 完了済み Issue 監査・クローズ＆Epic 進捗更新スキル (close-completed-issues)

このスキルは、Audio Effector リポジトリでオープン中の GitHub Issue を網羅的に監査し、以下の 2 点を安全かつ確実に実施するための標準手順を定義します：
1. **すでに対応が完了しているにもかかわらずクローズされていない通常 Issue** を特定・クローズする。
2. **オープン中の Epic Issue（親 Issue）** についてはクローズせず、配下サブ Issue の完了・進行状況に合わせて**本文の進捗記載（テーブル・チェックリスト・現状サマリ・期待される動作）を最新状態へ更新する**。

本リポジトリはデフォルトブランチが `main` である一方、開発用マージ先ブランチが **`develop`（`origin/develop`）** のため、PR 本文に `Closes #<Issue番号>` を記載して `develop` へマージしても GitHub の自動クローズが発動しない場合があります。本スキルはその取りこぼし解消と Epic 記載の最新化を一貫して行います。

---

## 安全ガード（厳守事項）

1. **Epic Issue は原則クローズせず、進捗記載の更新のみ行う**:
   - `epic` ラベルが付与されている Issue、またはタイトルに `[Epic]`・`（親Issue）` を含む Issue は、ユーザーから明示的にクローズ指示がない限り **絶対にクローズしない**（配下サブ Issue がすべて完了していてもオープンを維持する）。
   - 代わりに、配下サブ Issue が完了（`CLOSED`）または進行中（統合ブランチマージ済み／OPEN PR 存在）へ進んでいる場合は、Step 5 の手順に従って **本文の進捗記載のみを最新化** する。
2. **番号の単なる言及だけでクローズしない**:
   - PR 本文やコミット履歴に `#<Issue番号>` が含まれていても、関連 Issue としての参照（例: `関連Issue: #44`）や別作業での言及に過ぎない場合がある。必ず PR の内容・差分・Issue の受け入れ条件・実コードベースを確認して完了を裏付ける。
3. **未マージ・作業中のものはクローズしない**:
   - オープン PR のみ存在する場合、Epic 統合ブランチへのみマージ済みで `origin/develop` へ未反映の場合、またはローカル作業ブランチに未マージコミットがあるだけの状態は「完了済み」とみなさずオープンを維持する。通常 Issue のクローズ判定基準は **`origin/develop` への反映（および実コード・仕様書での完了確認）** とする。
4. **判断に迷うものは保留してユーザーへ確認する**:
   - 部分実装のみ完了しているもの、別機能へ統合・不要化された疑いがあるもの等は独断でクローズせず、根拠を添えて確認を仰ぐ。

---

## ワークフロー手順

### Step 1: リモート最新化と自動監査スクリプトの実行

1. まず `origin/develop` を最新化します（ローカルの作業ブランチは切り替え不要）。
   ```powershell
   git fetch --prune
   ```
2. 同梱の読み取り専用監査スクリプト [audit_open_issues.py](./scripts/audit_open_issues.py) を実行し、全オープン Issue（Epic / 非 Epic 分類）・全マージ済み PR・オープン PR・`origin/develop` コミット履歴・直近クローズ済み Issue・**Epic 内チェックリスト不整合／テーブル未更新／進行中子 Issue** を一括取得します。
   ```powershell
   python .agents/skills/close-completed-issues/scripts/audit_open_issues.py
   ```
   - 機械処理が必要な場合は `--json` または `--json-out <一時ファイルパス>`（UTF-8 BOM なし出力）も利用できます。

---

### Step 2: 通常 Issue の詳細確認と実コードベース突合

Step 1 で抽出された **非 Epic のオープン Issue 全件** について、以下の観点で対応状況を裏付けます。

1. **PR / コミット一致候補の精査（`[MERGED_PR]` / `[GIT]` 付き）**:
   - 該当 PR の詳細（`gh pr view <PR番号> --json number,title,body,mergedAt,baseRefName`）と Issue 本文（`gh issue view <Issue番号>`）を照合する。
   - `Closes/Fixes/Resolves #<番号>` または PR タイトル・ブランチ名・差分が Issue の目的および受け入れ条件を満たしており、かつ `origin/develop` へ反映済みであることを確認する（Epic 統合ブランチへのマージのみで `origin/develop` 未反映の場合はオープン維持）。
   - 単なる関連 Issue 言及・別タスクの参照であれば「未対応」へ戻す。
2. **一致なしの通常 Issue の実コード突合（取りこぼし防止）**:
   - PR 番号や Issue 番号を記載せずに別 PR へ同梱・直接コミットされて完了しているケースを見落とさないよう、各通常 Issue の本文に記載された対象ファイル・クラス・プロパティ・設定項目・UI 要素・仕様書パスを実コードベース（`AudioEffector*`・`設計/`・`.agents/`）と照合する。
   - 例:
     - 設定系 Issue → `AudioEffector.Domain/Entities/AppSettings.cs`、`AudioEffector/Presentation/ViewModels/SettingsViewModel.cs`、`SettingsDialog.xaml`
     - 画面・UI 系 Issue → 該当 `Views/*.xaml`・`*.xaml.cs`・`ViewModels/*.cs`
     - 仕様書・スキル改善 Issue → `設計/`・`.agents/` 配下の対象ファイル
3. **ローカル作業中ブランチとの区別**:
   - 必要に応じて `git status -sb` やローカルブランチの `origin/develop..HEAD` 差分を確認し、ブランチ作成直後や未マージ作業中の Issue を完了済みと誤認しないようにする。

---

### Step 3: 判定区分の確定

すべてのオープン Issue を以下の 4 区分に整理します。

| 区分 | 判定基準 | アクション |
| :--- | :--- | :--- |
| **A. クローズ対象（完了確定）** | `origin/develop` に反映済みで、実コード／仕様書上も Issue の受け入れ条件を満たす通常 Issue | Step 4 で根拠コメント付きクローズ |
| **B. 未対応（オープン維持）** | 未着手・作業中・統合ブランチ未統合・未マージの通常 Issue | クローズしない |
| **C. 要確認（保留）** | 部分対応・仕様変更による不要化疑いなど判断が必要な通常 Issue | クローズせずユーザーへ確認 |
| **D. Epic Issue（クローズ対象外・進捗更新対象）** | `epic` ラベル付き、または `[Epic]` / `（親Issue）` の親 Issue | 明示指示がない限りクローズしない。進捗が進んでいる場合は Step 5 で本文記載を更新 |

---

### Step 4: 完了済み通常 Issue のクローズ実行（文字化け防止必須）

区分 **A（クローズ対象）** が 1 件以上ある場合のみ、Windows PowerShell の文字化けを防止したうえで根拠コメントを添えてクローズします。区分 A が 0 件の場合は本ステップをスキップし、Step 5 へ進みます。

```powershell
chcp 65001 > $null
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

gh issue close <Issue番号> --reason completed --comment "PR #<PR番号>（`origin/develop` マージ済み）および実コードベースにて対応完了を確認したためクローズします。"
```

- 複数行の日本語コメントや確実な UTF-8 送信が必要な場合は、`gh api --method POST repos/:owner/:repo/issues/<Issue番号>/comments --input -`（UTF-8 JSON）または作業外の一時ファイルを用いた `gh issue comment <Issue番号> --body-file <一時ファイルパス>` を実行したあと `gh issue close <Issue番号> --reason completed` を実行し、一時ファイルを確実に削除してください。

---

### Step 5: オープン中 Epic Issue の進捗記載・チェックリスト更新（クローズ禁止）

Step 1 の監査結果および Step 4 で新たにクローズした子 Issue を踏まえ、すべてのオープン中 **Epic Issue（区分 D）** の本文を精査し、タスクが進んでいる Epic について記載を最新化します（**Epic 自体は絶対にクローズしない**）。

1. **更新対象の抽出観点**:
   - **残タスクチェックリストの未チェック残留（`uncheckedClosedChildren`）**: 既に `CLOSED`（Step 4 で今回クローズした分を含む）となっている子 Issue が `- [ ] #<番号>` のまま残っている場合 → `- [x] #<番号>` へ更新する。
   - **子 Issue 一覧テーブルのステータス陳腐化（`staleTableClosedChildren`）**: テーブル内で `未着手` / `⬜ 未着手` / `進行中` 等のまま残っているが該当子 Issue が `CLOSED` 済みの場合 → 既存表の表記スタイルに合わせて `完了` / `✅ 完了` へ更新する（依存列に書かれた前提 Issue 番号は誤更新しないこと）。
   - **進行中子 Issue の進捗反映（`inProgressChildren`）**: オープン中の子 Issue に統合ブランチマージ済み PR（`origin/develop` 未反映）や OPEN PR が紐づいている場合、テーブルの状態列を「`進行中（統合ブランチマージ済: PR #<番号>）`」「`進行中（PR #<番号>）`」等へ更新する（**チェックリストの `[ ]` は `origin/develop` へ反映・クローズされるまで `[x]` にしないこと**）。
   - **現状サマリ・期待される動作・更新対象仕様書の整合**:
     - 「現状の実装状況」「残タスクは〜」等の記載が古くなっている場合は最新件数・内容へ整合させる。
     - 期待される動作（Expected Behavior）や更新対象仕様書（Target Specs）のうち、紐づく子 Issue がすべて完了し実ファイル・実装も完了している項目のみ `[x]` へ更新する（未完了子 Issue を含む複合項目は `[ ]` のまま維持）。
     - テーブルに存在するが完了済み子 Issue がチェックリストから漏れている場合は、整合性を保つため追記・チェック済みとする。
2. **安全な更新手順（文字化け防止・既存構成の保全）**:
   - 本文全体を作り直さず、必ず現在の本文を `gh issue view <Epic番号> --json body` で取得し、**該当行のみ部分置換**して既存の見出し・背景・関連 Issue・未完了タスクを保全する。
   - Windows PowerShell の引数文字化けを防ぐため、更新は **UTF-8 JSON を標準入力で渡す `gh api --method PATCH`**（またはリポジトリ外一時ファイル＋`gh issue edit <Epic番号> --body-file <一時ファイル>`）を使用する（一時ファイルは使用後ただちに削除すること）。
   ```python
   # 例: UTF-8 JSON で安全に Epic 本文のみ更新（state は変更しない）
   subprocess.run(
       ["gh", "api", "--method", "PATCH", f"repos/{owner}/{repo}/issues/{epic_num}", "--input", "-"],
       input=json.dumps({"body": updated_body}, ensure_ascii=False).encode("utf-8"),
       check=True,
   )
   ```

---

### Step 6: 更新後検証と完了報告

1. **クローズおよび Epic 更新結果の検証**:
   - クローズした通常 Issue（該当時）:
     ```powershell
     gh issue view <Issue番号> --json number,state,closedAt,comments
     ```
     - `state` が `"CLOSED"` になっていること、および投稿した日本語コメントに文字化けがないことを確認する。
   - Epic 更新の整合性再確認:
     ```powershell
     python .agents/skills/close-completed-issues/scripts/audit_open_issues.py
     ```
     - 全 Epic が **引き続き `OPEN` を維持** していること、および `unchecked but closed`・`table stale (closed)` の不整合が解消（0 件）されていることを確認する。
   - リポジトリ内に一時ファイルが残っていないこと（`git status -sb`）を確認する。

2. **ユーザーへの完了報告フォーマット**:
   - **結論**: 監査したオープン Issue 総数（Epic / 通常内訳）、今回クローズした通常 Issue 件数、および本文進捗を更新した Epic 件数。
   - **クローズした通常 Issue 一覧**（該当時）: Issue 番号・タイトル・根拠（PR 番号 / コミットハッシュ / 確認済みファイル）。
   - **進捗記載を更新した Epic Issue 一覧**（該当時・すべてオープン維持）: Epic 番号・更新内容（テーブル・チェックリスト・進行中表記・期待動作等の差分要約）。
   - **オープン維持した通常 Issue・未変更 Epic の確認内訳**: 進行中（統合ブランチマージ済／OPEN PR）・未着手別の要約と根拠。
   - **要確認（保留）Issue**（該当時）: 保留理由と判断に必要な選択肢。

---

## 完了チェックリスト

- [ ] `git fetch --prune` で最新の `origin/develop` を基準に監査した
- [ ] 全オープン Issue（Epic / 通常）を漏れなく取得し、Epic Issue を誤ってクローズしていない
- [ ] PR の言及だけでなく、`origin/develop` へのマージ状態および実コードベース／仕様書と照合して完了を裏付けた
- [ ] クローズ実行時に UTF-8 で根拠コメントを記載し、`gh issue view` で `CLOSED` 状態・文字化けなしを確認した
- [ ] タスクが進んでいるオープン中 Epic Issue の本文（テーブル・チェックリスト・進行中ステータス・現状サマリ・期待動作）を UTF-8 で安全に更新し、`audit_open_issues.py` 再実行で不整合 0 件・全 Epic オープン維持を確認した
- [ ] リポジトリ内に一時ファイルを残さず、監査・更新内訳と結果をユーザーへ簡潔に報告した
