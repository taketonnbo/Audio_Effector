---
name: generate-detailed-issue
description: >-
  Use this skill when the user asks you to create or update a GitHub issue. It provides instructions on the expected level of granularity, structure, and required codebase investigation for the issue description.
---

# Generate Detailed Issue

When creating or updating GitHub issues for this project, you must provide a high level of detail and granularity based on the current codebase.

## Issue Structure

Your issue description should always include the following sections (written in Japanese):

### 1. 概要 (Overview)
Provide a brief summary of what the feature, bug fix, or task is about.

### 2. 実装内容 (Implementation Details)
This is the most important section. You must investigate the codebase before writing this section. Include:
- Bullet points grouped by architectural layers or logical components (e.g., 設定データモデルの拡張, UIコンポーネントの追加, 永続化処理, アプリケーションへの反映ロジック).
- **Explicit file names**: Mention the exact files that will be modified or created in parentheses (e.g., `AppSettings.cs`, `SettingsDialog.xaml`).
- **Code symbols**: Mention classes, methods, events, or properties where applicable (e.g., `PreviewKeyDown`, `Key`, `ModifierKeys`).
- Detailed descriptions of *how* the implementation will be done (e.g., which specific events to hook, what logic to add).

### 3. 関連Issue (Related Issues)
Link to any parent, child, or related issues (e.g., `- 親Issue: #31`).

## Workflow

1. **Investigate first**: Before writing the issue, use your tools (`list_dir`, `grep_search`, `view_file`, `run_command` with `gh` CLI) to understand the current architecture and exactly where the changes will be made.
2. **Draft the content**: Prepare the markdown content following the structure above.
3. **Use GitHub CLI**: Use `run_command` with the `gh issue create` or `gh issue edit` command to submit the issue.
