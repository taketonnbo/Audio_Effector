---
name: review-pr
description: GitHub Pull Request のコードレビューおよび PR 作成前のセルフレビュー（内部レビュー）を実施し、プロジェクト規約・設計書・パフォーマンス基準・テスト品質に基づいて検証し、必要に応じて GitHub PR へインラインレビューコメントを投稿するスキル。PR レビューやセルフレビューを依頼された際に使用する。
---

# PR レビュー・セルフレビュースキル (review-pr)

このスキルは、Audio Effector リポジトリにおける **GitHub Pull Request のコードレビュー** および **PR 作成前のセルフレビュー（内部レビュー）** を体系的かつ高精度に実施するための手順を定義します。

---

## 適用モード

依頼内容に応じて以下の 2 モードを使い分けます。

1. **GitHub PR レビューモード**: PR 番号・PR URL が指定された場合。差分・関連 Issue・実コードの呼び出し経路を精査し、検証コマンドを実行した上で GitHub PR へ総評およびインライン指摘コメントを投稿する。
2. **PR 作成前セルフレビュー（内部レビュー）モード**: ローカル作業ブランチの PR 作成前チェックを依頼された場合。`git diff develop...HEAD`（および未コミット差分）を対象に同じ観点で検証し、修正すべき項目を報告（または修正）する。GitHub 投稿は行わない。

---

## ワークフロー手順

### Step 1: コンテキストと差分の収集（並列実行）

#### GitHub PR レビューモードの場合
```powershell
# PR メタデータ・関連 Issue・差分を取得
gh pr view <PR番号> --json title,body,baseRefName,headRefName,headRefOid,files,commits,comments,reviews
gh pr diff <PR番号>
gh issue view <関連Issue番号> --json title,body,comments
```

#### PR 作成前セルフレビューモードの場合
```powershell
git status
git log develop..HEAD --oneline
git diff develop...HEAD
```

#### 参照すべきプロジェクト基準・設計書
変更内容に応じて該当するルール・設計書を必ず確認します（全チェック項目のマスター一覧は GitHub Wiki の `PRレビュー・セルフレビューチェックリスト` を参照）：
- **Git / PR 運用**: `.agents/rules/branch_operation_rules.md`, `.agents/rules/git_operation_rules.md`, `.agents/rules/issue_pr_rules.md`
- **ビルド・テスト**: `.agents/rules/build_rules.md`, `.agents/rules/test_rules.md`
- **アーキテクチャ・規約**: `設計/詳細設計/アーキテクチャ/アーキテクチャ全体設計書.md`, `設計/詳細設計/アーキテクチャ/静的解析およびコーディング規約.md`
- **パフォーマンス**: `設計/詳細設計/パフォーマンス最適化.md`
- **UI / デザイン / バインディング**: `設計/デザインシステム/01_デザイントークン仕様書.md`, `.agents/rules/ui_spec_rules.md`, `設計/詳細設計/XAMLバインディング検証基盤.md`, および対象画面の `設計/画面設計/<画面名>/*UI仕様書.md`

---

### Step 2: 周辺コードと実実行経路の深掘りトレース

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
```

> **注意**: テストがバックグラウンド実行になった場合は長時間放置せずログ進行を監視し、ハング時は速やかに中断して原因を報告します。また、未コミット変更がある状態では勝手に `git checkout` を行わないでください。

---

### Step 4: 多角的チェックと重要度分類

以下の 6 カテゴリで漏れなく検証し、検出した指摘を重要度別に分類します：

1. **Git・ブランチ・PR 規約**: Base=`develop`、ブランチ名、コミット形式、PR タイトル `[種別]`、PR 本文必須項目（`Closes #N` 等）、不要ファイル混入なし。
2. **アーキテクチャ・DI・イベント整合性**: 4 層単方向依存、DI 登録、イベント/サービス重複呼び出し、購読解除・`Dispose` 漏れ。
3. **ロジック・コーディング規約・XML コメント**: Null 安全、ソート昇降順の対称性（タイブレーカー）、`public` 要素への日本語 XML コメント（句点なし・`<param>`/`<returns>` 完備）、命名規則。
4. **パフォーマンス最適化**: `CollectionChanged` 連続発火のコアレッシング、検索デバウンス、親 `ScrollViewer` 禁止＋UI 仮想化 4 属性（`IsVirtualizing`, `VirtualizationMode="Recycling"`, `ScrollUnit="Pixel"`, `CanContentScroll`）、非表示パネルの `Visibility="Collapsed"`、高負荷エフェクト禁止、画像 `Freeze()`、安全な `Dispatcher` ガード。
5. **UI / XAML・テーマ・インタラクション**: ダーク/ライト両テーマ視認性（ハードコード色禁止、`AlwaysNeonCyanBrush` 適用）、列ヘッダーとスクロールバーによる幅ズレ、`MouseDoubleClick` の `OriginalSource` 判定、`CommandManager.InvalidateRequerySuggested()`、実データ結合。
6. **テスト品質・仕様書同期**: 4 層対称配置、`sut` 命名、AAA コメント、日本語テスト名・XML コメント、`using` による確実な破棄、STA スレッドの `InvokeShutdown()`、不足ケース（境界値・`Dispose` 後・実 View の `BindingErrorTraceListener` 検証）、仕様書との差分明記。

#### 重要度基準
- 🔴 **Critical**: マージブロッカー（クラッシュ、UI フリーズ、状態破綻・イベント多重発火、データ破損）
- 🟠 **High**: 重大な不具合・機能欠損（実アプリ上で動作しない、誤操作による意図しない挙動、明確な仕様違反）
- 🟡 **Medium**: 品質・UX・保守性改善（テーマ視認性低下、レイアウトズレ、デバウンス欠如、ソート不安定、`CanExecute` 更新漏れ）
- 🟢 **Low**: 軽微・改善提案（テスト拡充、`using` 化、文言・規約微調整）

各指摘には必ず **根拠（ファイルパス・行番号・具体的な実行経路/再現条件）** と **具体的な改善案** を記載します。

---

### Step 5: GitHub PR へのレビューコメント投稿（PR レビューモードのみ）

ユーザーから PR へのコメント投稿が求められている場合、総評（サマリーテーブル＋仕様書スコープ確認）と該当行へのインライン指摘コメントを一括で投稿します。

#### 投稿時の注意事項
1. **レビューイベント種別**: 自身の PR に対しては GitHub API の仕様上 `APPROVE` / `REQUEST_CHANGES` を送信できないため、**`event: "COMMENT"`** を使用します（他者 PR で明示指示がある場合のみ `REQUEST_CHANGES` 等を選択）。
2. **行番号の制約**: 各インラインコメントの `path`・`line`（および複数行指定時の `start_line`）は、必ず PR diff 内の追加・変更行（`RIGHT` 側）に一致させてください。`start_line` を指定する場合は **`"start_side": "RIGHT"`** も必ず付与します。
3. **文字化け防止**: PowerShell の引数エスケープ崩れを避けるため、UTF-8（BOM なし）の JSON ファイルに書き出してから `gh api --input` で送信します。

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

1. 投稿したレビューの URL（PR レビューモード時）と、ビルド・テスト・静的検証の実行結果を報告する。
2. 指摘事項を重要度別（Critical / High / Medium / Low）に簡潔に要約し、該当ファイルへのリンクを添える。
3. 仕様書とのスコープ差分（後続 Issue 化候補）があればあわせて確認事項として共有する。
