---
description: Rules for Git and GitHub workflows, especially regarding branch management and PR creation.
---

# Git Workflow Rules

When performing Git operations or interacting with GitHub (e.g., creating Pull Requests) in this workspace, you MUST ALWAYS adhere to the following rules:

1. **Pull Request Base Branch**:
   - The default development branch is `develop`.
   - When creating a Pull Request (e.g., using `gh pr create`), you **MUST ALWAYS** specify the base branch as `develop` by passing the `--base develop` argument.
   - Example: `gh pr create --base develop --title "..." --body "..."`
   - NEVER create a PR targeting `main` or merge directly into `main` unless the user explicitly requests a production release or hotfix.

2. **Feature Branching**:
   - All feature, fix, and chore branches MUST be branched off from `develop`.
   - Before creating a new branch, ensure your local `develop` branch is up to date (`git checkout develop && git pull`).

3. **Commit Messages**:
   - ALL commit messages MUST be written in **Japanese**.
   - Use standard prefix conventions like `feat:`, `fix:`, `chore:`, etc., followed by a descriptive Japanese message.
   - Example: `feat: プレイリストの右クリックメニューを実装 (#23)`

4. **Adherence to Project Rules & Guidelines**:
   - Respect and follow the project rules located in this directory (`.agents/rules/`).
   - Specifically, follow the guidelines in `branch_operation_rules.md`, `git_operation_rules.md`, and `issue_pr_rules.md` when managing branches, structuring commit messages, and authoring PR titles/bodies.
   - **Performance Checklist**: Before creating any Pull Request, you MUST review `設計/詳細設計/パフォーマンス最適化.md` to ensure your implementation does not violate the performance guidelines (e.g., UI throttling, high-load effect usage, memory freeze rules).
