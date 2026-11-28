---
name: close-completed-issues
description: オープン中のGitHub Issueを全件棚卸しし、マージ済みPR・origin/developのGit履歴・実コードベースおよび仕様書と照合して対応済みの通常Issueを安全にクローズするスキル。Epic Issueは明示指示がない限りクローズ対象外とし、完了済み未クローズIssueの確認・一括クローズを依頼された際に使用する。
---

# 完了済み Issue 監査・クローズスキル (close-completed-issues)

このスキルは、Audio Effector リポジトリでオープン中の GitHub Issue を網羅的に監査し、**すでに対応が完了しているにもかかわらずクローズされていない通常 Issue** を特定・クローズするための標準手順を定義します。

本リポジトリはデフォルトブランチが `main` である一方、開発用マージ先ブランチが **`develop`（`origin/develop`）** のため、PR 本文に `Closes #<Issue番号>` を記載して `develop` へマージしても GitHub の自動クローズが発動しない場合があります。本スキルはその取りこぼしを安全かつ確実に解消します。

---

## 安全ガード（厳守事項）

1. **Epic Issue は原則クローズしない**:
   - `epic` ラベルが付与されている Issue、またはタイトルに `[Epic]`・`（親Issue）` を含む Issue は、ユーザーから明示的にクローズ指示がない限り **絶対にクローズしない**（配下サブ Issue がすべて完了していてもオープンを維持する）。
2. **番号の単なる言及だけでクローズしない**:
   - PR 本文やコミット履歴に `#<Issue番号>` が含まれていても、関連 Issue としての参照（例: `関連Issue: #44`）や別作業での言及に過ぎない場合がある。必ず PR の内容・差分・Issue の受け入れ条件・実コードベースを確認して完了を裏付ける。
3. **未マージ・作業中のものはクローズしない**:
   - オープン PR のみ存在する場合や、ローカル作業ブランチに未マージコミットがあるだけの状態は「完了済み」とみなさずオープンを維持する。判定基準は **`origin/develop` への反映（および実コード・仕様書での完了確認）** とする。
4. **判断に迷うものは保留してユーザーへ確認する**:
   - 部分実装のみ完了しているもの、別機能へ統合・不要化された疑いがあるもの等は独断でクローズせず、根拠を添えて確認を仰ぐ。

---

## ワークフロー手順

### Step 1: リモート最新化と自動監査スクリプトの実行

1. まず `origin/develop` を最新化します（ローカルの作業ブランチは切り替え不要）。
   ```powershell
   git fetch --prune
   ```
2. 同梱の読み取り専用監査スクリプト [audit_open_issues.py](./scripts/audit_open_issues.py) を実行し、全オープン Issue（Epic / 非 Epic 分類）・全マージ済み PR・オープン PR・`origin/develop` コミット履歴・直近クローズ済み Issue・Epic 内チェックリスト不整合を一括取得します。
   ```powershell
   python .agents/skills/close-completed-issues/scripts/audit_open_issues.py
   ```
   - 機械処理が必要な場合は `--json` または `--json-out <一時ファイルパス>`（UTF-8 BOM なし出力）も利用できます。

---

### Step 2: 通常 Issue の詳細確認と実コードベース突合

Step 1 で抽出された **非 Epic のオープン Issue 全件** について、以下の観点で対応状況を裏付けます。

1. **PR / コミット一致候補の精査（`[MERGED_PR]` / `[GIT]` 付き）**:
   - 該当 PR の詳細（`gh pr view <PR番号> --json number,title,body,mergedAt,baseRefName`）と Issue 本文（`gh issue view <Issue番号>`）を照合する。
   - `Closes/Fixes/Resolves #<番号>` または PR タイトル・ブランチ名・差分が Issue の目的および受け入れ条件を満たしていることを確認する。
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
| **B. 未対応（オープン維持）** | 未着手・作業中・未マージの通常 Issue | クローズしない |
| **C. 要確認（保留）** | 部分対応・仕様変更による不要化疑いなど判断が必要な通常 Issue | クローズせずユーザーへ確認 |
| **D. 対象外（Epic Issue）** | `epic` ラベル付き、または `[Epic]` / `（親Issue）` の親 Issue | 明示指示がない限りクローズしない |

---

### Step 4: 完了済み Issue のクローズ実行（文字化け防止必須）

区分 **A（クローズ対象）** が 1 件以上ある場合のみ、Windows PowerShell の文字化けを防止したうえで根拠コメントを添えてクローズします。区分 A が 0 件の場合は本ステップをスキップし、Step 5 の報告へ進みます。

```powershell
chcp 65001 > $null
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

gh issue close <Issue番号> --reason completed --comment "PR #<PR番号>（`origin/develop` マージ済み）および実コードベースにて対応完了を確認したためクローズします。"
```

- 複数行の日本語コメントを投稿する場合は、UTF-8 の一時ファイルを作成し `gh issue comment <Issue番号> --body-file <一時ファイルパス>` を実行したあと `gh issue close <Issue番号> --reason completed` を実行し、一時ファイルを確実に削除してください（リポジトリ内を汚さないよう作業外の一時領域を使用すること）。

---

### Step 5: クローズ後検証と完了報告

1. **クローズ結果の検証（1 件以上クローズした場合）**:
   ```powershell
   gh issue view <Issue番号> --json number,state,closedAt,comments
   ```
   - `state` が `"CLOSED"` になっていること、および投稿した日本語コメントに文字化けがないことを確認する。
   - リポジトリ内に一時ファイルが残っていないこと（`git status -sb`）を確認する。

2. **ユーザーへの完了報告フォーマット**:
   - **結論**: 監査したオープン Issue 総数（Epic / 通常内訳）と、今回クローズした件数（0 件の場合はその旨と直近クローズ済み Issue の状況）。
   - **クローズした Issue 一覧**（該当時）: Issue 番号・タイトル・根拠（PR 番号 / コミットハッシュ / 確認済みファイル）。
   - **オープン維持した通常 Issue の確認内訳**: カテゴリ別または一覧で未対応の根拠となる実ファイル参照を簡潔に提示。
   - **クローズ対象外とした Epic Issue 一覧**: オープン維持した Epic 番号一覧（参考情報として、配下サブ Issue が全完了の Epic や、本文チェックリスト上でクローズ済みサブ Issue が `[ ]` のまま残っている項目があれば併せて補足）。
   - **要確認（保留）Issue**（該当時）: 保留理由と判断に必要な選択肢。

---

## 完了チェックリスト

- [ ] `git fetch --prune` で最新の `origin/develop` を基準に監査した
- [ ] 全オープン Issue（Epic / 通常）を漏れなく取得し、Epic Issue を誤ってクローズしていない
- [ ] PR の言及だけでなく、マージ状態および実コードベース／仕様書と照合して完了を裏付けた
- [ ] クローズ実行時に UTF-8 で根拠コメントを記載し、`gh issue view` で `CLOSED` 状態・文字化けなしを確認した
- [ ] リポジトリ内に一時ファイルを残さず、監査内訳と結果をユーザーへ簡潔に報告した
