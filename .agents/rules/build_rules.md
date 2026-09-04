---
description: ユーザーのデバッグ実行セッションを阻害しないための安全なビルド・検証ルール
---

# ビルド・検証ルール (Build Rules)

エージェントがコードの構文チェック、コンパイル確認、テスト検証のために `dotnet build` を実行する際は、**ユーザーがエディタ上でアプリをデバッグ実行中であってもセッションを強制終了・ファイルロック競合させない** よう、以下の規則を厳守してください。

## 1. 隔離ビルド（-p:OutDir の使用）
- 通常の `dotnet build` はデフォルトで `bin\Debug\` 配下のファイルを上書きしようとするため、デバッグ実行中のプロセスロック（`MSB3026`）やデバッグ中断を引き起こします。
- エージェントがビルド検証を行う際は、**必ず専用の一時出力先ディレクトリ（`.agent_build/`）を指定** してください。
- コマンド例:
  ```powershell
  dotnet build AudioEffector.sln -p:OutDir="$(Get-Location)\.agent_build\"
  ```

## 2. 一時ディレクトリのクリーンアップ
- ビルド検証が完了した後は、作業ディレクトリをクリーンに保つため、`.agent_build` ディレクトリを削除してください。
- コマンド例:
  ```powershell
  Remove-Item -Recurse -Force -Path '.agent_build' -ErrorAction SilentlyContinue
  ```

## 3. テスト実行時の配慮
- `dotnet test` を実行する際も、ビルド成果物の競合を避けるため、同様に出力ディレクトリや `--no-build` オプションを適切に組み合わせて実行してください。
