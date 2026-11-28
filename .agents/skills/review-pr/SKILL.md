---
name: review-pr
description: GitHub Pull Request のコードレビューおよび PR 作成前のセルフレビュー（内部レビュー）を実施し、プロジェクト規約・コミット粒度・設計書・パフォーマンス基準・テスト品質に基づいて検証し、必要に応じて GitHub PR へインラインレビューコメントを投稿するスキル。PR レビューやセルフレビューを依頼された際に使用する。
---

# PR レビュー・セルフレビュースキル (review-pr)

このスキルは、Audio Effector リポジトリにおける **GitHub Pull Request のコードレビュー** および **PR 作成前のセルフレビュー（内部レビュー）** を体系的かつ高精度に実施するための手順を定義します。

---

## 適用モード

依頼内容に応じて以下の 2 モードを使い分けます。

1. **GitHub PR レビューモード**: PR 番号・PR URL が指定された場合。差分・コミット履歴（粒度）・関連 Issue・実コードの呼び出し経路を精査し、検証結果と修正完了の条件を実装担当へ報告する。レビュー担当の作業は調査・検証・指摘までとし、PR の実装修正は実装担当が行う。投稿を依頼された場合は Step 5 に従う。マージや関連 Issue のクローズも依頼された場合は、指摘の解消と検証済み HEAD を確認してから実行し、GitHub 上の結果を報告する。
2. **PR 作成前セルフレビュー（内部レビュー）モード**: ローカル作業ブランチの PR 作成前チェックを依頼された場合。対象ベースブランチ `<BaseBranch>`（通常 Issue は `develop`、Epic 子 Issue は当該 Epic の統合ブランチ `epic/<Epic番号>-<説明>`）を基準に `git log <BaseBranch>..HEAD --stat`・`git diff <BaseBranch>...HEAD`（および未コミット差分）を対象として同じ観点で検証し、修正すべき項目を報告（または修正）する。GitHub 投稿は行わない。

---

## ワークフロー手順

### Step 1: コンテキスト・差分・コミット履歴の収集（並列実行）

#### GitHub PR レビューモードの場合
```powershell
# PR メタデータ・関連 Issue・差分・コミット一覧（数・メッセージ・変更規模）を取得
gh pr view <PR番号> --json title,body,baseRefName,headRefName,headRefOid,files,commits,comments,reviews
gh pr diff <PR番号>
gh issue view <関連Issue番号> --json title,body,comments
```

> **必須確認（コミット粒度の把握）**: `commits` の件数と `files` の合計変更行数・レイヤー数を確認し、必要に応じてローカルで `git log origin/<baseRefName>..origin/<headRefName> --stat`（または `gh api repos/<owner>/<repo>/pulls/<PR番号>/commits`）を実行して、**`baseRefName` を基準としたコミットごとの変更ファイル・行数の偏り**を把握します（Epic 子 Issue PR の場合は `baseRefName` が `epic/<Epic番号>-<説明>` となります）。

#### PR 作成前セルフレビューモードの場合
```powershell
git status
git log <BaseBranch>..HEAD --stat
git diff <BaseBranch>...HEAD
```

#### 参照すべきプロジェクト基準・設計書
変更内容に応じて該当するルール・設計書を必ず確認します（全チェック項目のマスター一覧は GitHub Wiki の `PRレビュー・セルフレビューチェックリスト` を参照）：
- **Git / PR 運用・コミット粒度**: `.agents/rules/branch_operation_rules.md`, `.agents/rules/git_operation_rules.md`, `.agents/rules/issue_pr_rules.md`
- **ビルド・テスト**: `.agents/rules/build_rules.md`, `.agents/rules/test_rules.md`
- **アーキテクチャ・規約**: `設計/詳細設計/アーキテクチャ/アーキテクチャ全体設計書.md`, `設計/詳細設計/アーキテクチャ/静的解析およびコーディング規約.md`
- **パフォーマンス**: `設計/詳細設計/パフォーマンス最適化.md`
- **UI / デザイン / バインディング**: `設計/デザインシステム/01_デザイントークン仕様書.md`, `.agents/rules/ui_spec_rules.md`, `設計/詳細設計/XAMLバインディング検証基盤.md`, および対象画面の `設計/画面設計/<画面名>/*UI仕様書.md`
- **仕様書同期・メンテナンス**: GitHub Wiki の `仕様書同期・メンテナンスルール` および `設計/spec_map.json`

---

### Step 2: 周辺コードと実実行経路の深掘りトレース

初回レビューでは、変更された状態や判定を共有する入口と、その結果を利用する箇所を先に洗い出す。関連 Issue と既存仕様から、操作前の状態・操作・期待する結果を整理し、差分が影響する組み合わせをまとめて検証する。

- 状態が次の操作に引き継がれる場合は、公開 API や UI 操作から開始し、次の遷移や終了通知まで確かめる。例えば再生キューの最終曲判定では、同じ曲が先頭と末尾にある場合の自動送りと、単曲指定で末尾に移った場合の両方が関係する。
- 既存テストがモックや内部フィールドの操作で状態を作っている場合は、通常の入口も同じ分岐を通るか確認する。不足する経路は必要に応じて隔離環境で再現し、再現用の変更は PR に含めない。
- 初回の結果は一括して報告し、各指摘に再現操作、期待値、実際の結果、修正後に守るべき既存動作を添える。再レビューでは前回確認したコミットから最新 HEAD までの差分を調べ、各指摘の完了条件と、修正で影響を受ける共有経路を再確認する。

**diff 内のコードだけを見て終わらせないこと。** 以下の「外側の結合点」を必ず実ファイルで確認します：
1. **データ供給元**: ViewModel / View が参照・表示・検索・ソートしているプロパティ（例: `Track.Genre` 等）が、実際の読み込み処理（例: `LibraryViewModel.LoadLibrary()`）で確実にセットされているか。
2. **イベント購読先・発火元**: イベント（`PlaybackRequested`, `CollectionChanged` 等）の購読先と発火元を両方追い、**二重呼び出し**（例: `SetPlaylist` の重複実行）や **$O(N^2)$ の連鎖発火**（1 件ずつの `Add` に対する毎件フル再構築）が起きないか。
3. **テーマ・スタイル定義**: XAML が参照している `DynamicResource` / `StaticResource` の実定義を `DarkTheme.xaml`・`LightTheme.xaml`・`App.xaml` の両方で確認し、ライトテーマ時のコントラスト崩れやハードコード色がないか。
4. **DI 登録とライフサイクル**: `DependencyInjection.cs`・`MainViewModel.cs` における Singleton 共有と `Dispose()` の整合性。

---

### Step 3: 自動検証コマンドの実行

レビュー対象ブランチがローカルにチェックアウトされている（または切り替え可能な）場合、以下を実行して事実確認を行います（`.agents/rules/build_rules.md` 厳守）：

```powershell
# 1. 隔離ビルド（エラー 0・警告 0 の確認）
dotnet build AudioEffector.sln -p:OutDir="$(Get-Location)\.agent_build\"
Remove-Item -Recurse -Force -Path '.agent_build' -ErrorAction SilentlyContinue

# 2. 単体テスト実行（全件合格・ハングなしを確認）
dotnet test AudioEffector.Tests\AudioEffector.Tests.csproj

# 3. XAML 変更を含む場合は静的バインディング・固定インデクサ検証
python tools/check_equivalence.py --indexer

# 4. 仕様書同期チェック（変更コードに対応する設計仕様書の更新漏れ確認）
# ※ PR レビュー時は PR の baseRefName（通常は develop、Epic 子 Issue PR は epic/<Epic番号>-<説明>）を基準に比較する
python tools/check_spec_sync.py --base origin/<baseRefName>
# ※ PR 対象ブランチをチェックアウトしていない場合は、PR の変更ファイル一覧を --files で渡して確認可能
```

> **注意**: テストがバックグラウンド実行になった場合は長時間放置せずログ進行を監視し、ハング時は速やかに中断して原因を報告します。また、未コミット変更がある状態では勝手に `git checkout` を行わないでください。

---

### Step 4: 多角的チェックと重要度分類

以下の 6 カテゴリで漏れなく検証し、検出した指摘を重要度別に分類します：

1. **Git・ブランチ・PR 規約・コミット粒度（必須チェック）**:
   - Base ブランチが規約に適合しているか、およびブランチ名が命名規則に従っているか：
     - **通常（単一 Issue）PR**: Base は `develop`、Head は `<Type>/<Issue番号>-<説明>`。
     - **Epic 子 Issue PR**: Base は当該 Epic の統合ブランチ（`epic/<Epic番号>-<説明>`）、Head は `<Type>/<子Issue番号>-<説明>`（※Epic 子 Issue PR に対して「Base が `develop` でない」という誤指摘をしないこと。逆に、Epic 子 Issue なのに Base が誤って `develop` になっている場合は指摘対象とする）。
     - **Epic 統合 PR**: Base は `develop`、Head は `epic/<Epic番号>-<説明>`。
   - コミットメッセージが `<Type>: <Emoji> #<Issue番号> <Subject>`（日本語・現在形・20〜30 文字目安）に従っているか。
   - **コミット粒度（巨大単一コミットの禁止）**:
     - `.agents/rules/git_operation_rules.md` および GitHub Wiki「PRレビュー・セルフレビューチェックリスト」に従い、**コミット数・各コミットの変更行数・含まれるレイヤーと種別**を必ず確認する。
     - **NG 基準（必ず指摘対象とする）**:
       - 複数レイヤー・複数責務（例: Domain / Infrastructure / ViewModel / View / DI 結合 / 単体テスト / ドキュメント等）が **1 コミットに集約**されている（`git add .` の一括コミット）。
       - 1 コミットで **500〜1,000 行超** の巨大な変更になっている。
       - 異なる変更種別（例: `feat` と `fix`、`feat` と `test`、`refactor` と機能追加）が 1 コミットに混在している。
       - コミット一覧（`git log --stat`）を見たときに変更のストーリーや意図を段階的に追えない。
     - **指摘時の必須記載**: 違反を検出した場合は **🟡 Medium 以上** として必ずレビューコメントに含め、レイヤー・責務単位の**具体的なコミット分割案**（例: Domain/Repository → ViewModel → View → DI 結合 → 単体テスト）を提示する。
   - PR タイトル `[種別]`、PR 本文必須項目（`Closes #N` / `Resolves #N`、概要、対応内容、確認事項）、不要ファイル混入なし。
2. **アーキテクチャ・DI・イベント整合性**: 4 層単方向依存、DI 登録、イベント/サービス重複呼び出し、購読解除・`Dispose` 漏れ、スレッド境界とデッドロック防止（**`lock` 保持中の同期 `_uiDispatcher.Invoke(...)` 等の UI スレッド同期呼び出し禁止**：UI スレッド側の解放・操作処理との競合によるデッドロックを避けるため、ロック解放後ディスパッチ・`BeginInvoke` 化・`volatile` 等のスレッド安全設計を確認）。
3. **ロジック・コーディング規約・XML コメント**: Null 安全、並行・状態遷移のスレッド安全性（ロック解放後ディスパッチ時の最新状態・破棄済みガードを含む）、ソート昇降順の対称性（タイブレーカー）、`public` 要素への日本語 XML コメント（句点なし・`<param>`/`<returns>` 完備）、命名規則。
4. **パフォーマンス最適化**: `CollectionChanged` 連続発火のコアレッシング、検索デバウンス、親 `ScrollViewer` 禁止＋UI 仮想化 4 属性（`IsVirtualizing`, `VirtualizationMode="Recycling"`, `ScrollUnit="Pixel"`, `CanContentScroll`）、**仮想化 `ListBox`（`ScrollViewer.CanContentScroll="True"`）で `ItemsPresenter.Margin` 等により `VirtualizingStackPanel` の利用可能高さが圧迫されビューポート縮小や自動スクロール座標ズレによるコンテナ消滅が起きないか**、非表示パネルの `Visibility="Collapsed"`、高負荷エフェクト禁止、画像 `Freeze()`、安全な `Dispatcher` ガード。
5. **UI / XAML・テーマ・インタラクション**: ダーク/ライト両テーマ視認性（ハードコード色禁止、`AlwaysNeonCyanBrush` 適用）、列ヘッダーとスクロールバーによる幅ズレ、`MouseDoubleClick` の `OriginalSource` 判定、**`RelayCommand` の `CanExecute` 条件が変わる状態遷移時に `CommandManager.InvalidateRequerySuggested()` または明示的 `CanExecuteChanged` 発火が漏れておらず実 WPF 上でボタン非活性固着しないか**、実データ結合。
6. **テスト品質・仕様書同期**: 4 層対称配置、`sut` 命名、AAA コメント、日本語テスト名・XML コメント、`using` による確実な破棄、STA スレッドの `ShutdownMode.OnExplicitShutdown` 設定および `InvokeShutdown()`、不足ケース（境界値・`Dispose` 後・実 View の `BindingErrorTraceListener` 検証・仮想化 `ListBox` の可視領域および自動スクロール検証・実 WPF ホスト上での `Button.IsEnabled` 遷移検証）、**仕様書同期（`check_spec_sync.py` で検出された主要仕様書が更新されているか、仕様・UI・永続化・アーキテクチャ変更を伴うのに `設計/` が未更新でないか、PR 本文に同期結果・更新不要理由・スコープ差分が明記されているか）**。

#### 重要度基準
- 🔴 **Critical**: マージブロッカー（クラッシュ、UI フリーズ、状態破綻・イベント多重発火、データ破損）
- 🟠 **High**: 重大な不具合・機能欠損（実アプリ上で動作しない・ボタン非活性固着、`lock` 保持中の同期 UI ディスパッチ等のデッドロックリスク、誤操作による意図しない挙動、明確な仕様違反）
- 🟡 **Medium**: 品質・UX・保守性・運用規約改善（**コミット粒度の過大・単一コミット集約**、**仕様・UI・永続化変更に伴う該当仕様書の未更新**、テーマ視認性低下、レイアウトズレ、デバウンス欠如、ソート不安定、軽微な `CanExecute` 更新漏れ）
- 🟢 **Low**: 軽微・改善提案（テスト拡充、`using` 化、文言・規約微調整）

各指摘には必ず **根拠（ファイルパス・行番号またはコミット SHA・具体的な実行経路/再現条件）** と **具体的な改善案** を記載します。

---

### Step 5: GitHub PR へのレビューコメント投稿（PR レビューモードのみ）

ユーザーから PR へのコメント投稿が求められている場合、総評（サマリーテーブル・コミット粒度確認結果・仕様書スコープ確認）と該当行へのインライン指摘コメントを一括で投稿します。

#### 投稿時の注意事項
1. **レビューイベント種別**: 自身の PR に対しては GitHub API の仕様上 `APPROVE` / `REQUEST_CHANGES` を送信できないため、**`event: "COMMENT"`** を使用します（他者 PR で明示指示がある場合のみ `REQUEST_CHANGES` 等を選択）。
2. **2 回目以降の再レビュー（指摘 0 件時）の必須コメント投稿**: 2 回目以降の再レビューで新たな指摘が 0 件であった場合も、**レビューコメントの投稿を省略してはなりません**。前回指摘の解消確認結果・検証済みコミット（`headRefOid`）・新規指摘なし（LGTM / マージ可能）を明記したレビュー総評コメント（`comments: []`）を必ず GitHub PR へ投稿し、監査可能な完了証跡を残します。
3. **行番号の制約**: 各インラインコメントの `path`・`line`（および複数行指定時の `start_line`）は、必ず PR diff 内の追加・変更行（`RIGHT` 側）に一致させてください。`start_line` を指定する場合は **`"start_side": "RIGHT"`** も必ず付与します。
4. **コミット粒度等の PR 全体指摘**: ファイル行に紐づかないコミット粒度・PR 運用上の指摘は、レビュー総評（`body`）に明記するか、`gh pr comment` で明確に指摘します。
5. **文字化け防止**: PowerShell の引数エスケープ崩れを避けるため、UTF-8（BOM なし）の JSON ファイルに書き出してから `gh api --input` で送信します。

#### 投稿コマンド手順
一時 JSON（例: スクラッチディレクトリ配下の `pr_review.json`）を以下の形式で作成して送信します：

```json
{
  "commit_id": "<headRefOid>",
  "event": "COMMENT",
  "body": "## PR #<番号> コードレビュー総評\n...",
  "comments": [
    {
      "path": "AudioEffector/Presentation/ViewModels/ExampleViewModel.cs",
      "start_line": 10,
      "start_side": "RIGHT",
      "line": 20,
      "side": "RIGHT",
      "body": "🔴 **[Critical] 指摘タイトル**\n\n根拠と具体的な改善案..."
    }
  ]
}
```

```powershell
# 投稿と結果検証
gh api --method POST repos/<owner>/<repo>/pulls/<PR番号>/reviews `
  --input "<jsonファイルパス>" `
  --jq '{id: .id, state: .state, html_url: .html_url}'
```

---

### Step 6: ユーザーへの報告

1. 投稿したレビューの URL（PR レビューモード時）と、ビルド・テスト・静的検証およびコミット粒度確認の実行結果を報告する。
2. 指摘事項を重要度別（Critical / High / Medium / Low）に簡潔に要約し、該当ファイルへのリンクを添える。
3. 仕様書とのスコープ差分（後続 Issue 化候補）があればあわせて確認事項として共有する。
