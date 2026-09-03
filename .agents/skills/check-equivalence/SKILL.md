---
name: check-equivalence
description: リファクタリング前後におけるXAMLバインディング網羅性・ViewModel公開API・モデル/エンティティプロパティの新旧等価性およびデグレを自動検証するスキル。
---

# 新旧等価性・デグレ検証スキル (check-equivalence)

このスキルは、リファクタリング作業中または作業後に、機能欠落・UIバインディングの切断・公開APIやエンティティプロパティのデグレが発生していないかを機械的に検証する際に使用します。

---

## 1. 検証ツールの概要

本リポジトリには、新旧等価性を自動検証するPythonスクリプト `tools/check_equivalence.py` が用意されています。

### 主な検証対象
1. **XAMLバインディング網羅性検証**:
   - すべてのアクティブなXAMLファイル（`MainWindow.xaml`, `Views/*.xaml`, `Themes/*.xaml` 等）から `{Binding ...}` を自動抽出。
   - ViewModel群（`MainViewModel`, `Presentation/ViewModels/*.cs`）やModel/Entity側にバインド先プロパティ・コマンド（`ICommand`, `RelayCommand` 等）が存在しているかを検証。
2. **ViewModel / 公開API等価性検証**:
   - `MainViewModel` や各種ViewModelの `public` プロパティ、コマンド、メソッド一覧を抽出。
   - 新しい専門ViewModel（`Presentation/ViewModels/*.cs`）への機能移行カバレッジおよび未移行メンバーを可視化。
3. **モデル・エンティティプロパティ等価性検証**:
   - 旧モデル（`Models/*.cs`）と新ドメインエンティティ（`Domain/Entities/**/*.cs`）のプロパティを比較し、共通プロパティ・UI固有プロパティ・新追加プロパティの差分を分析。

---

## 2. 実行コマンド

### 全体検証（デフォルト）
```bash
python tools/check_equivalence.py
```

### 特定レイヤーのみ検証
```bash
# XAMLバインディング網羅性のみ
python tools/check_equivalence.py --xaml

# ViewModel / API等価性のみ
python tools/check_equivalence.py --vm

# モデル・エンティティ等価性のみ
python tools/check_equivalence.py --models
```

### レポート出力
```bash
# Markdownレポート出力
python tools/check_equivalence.py --report docs/equivalence_report.md

# JSON形式で出力（CI連携・パース用）
python tools/check_equivalence.py --json
```

---

## 3. リファクタリング時の検証フロー

1. **コード変更前のベースライン確認**:
   - `python tools/check_equivalence.py` を実行して現状のステータスを確認。
2. **コードの変更・移動・リファクタリング**:
   - ViewModelの分割、XAMLのリソースパス更新、モデルの移行などを実施。
3. **等価性チェックの再実行**:
   - `python tools/check_equivalence.py` を実行し、**WARN** や **FAIL** が発生していないか確認。
   - WARNが発生した場合は、バインディング名の間違い、ViewModel側のプロパティ名不一致、または未実装のコマンドがないかを修正。
4. **ビルドおよびテストの実行**:
   - `dotnet build AudioEffector/AudioEffector.csproj`
   - `dotnet test AudioEffector.Tests/AudioEffector.Tests.csproj`
