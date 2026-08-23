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

3. **Adherence to Project Documentation**:
   - Respect and follow the project's documentation located in the `rule_docs/` directory.
   - Specifically, follow the guidelines in `rule_docs/issue_pr_rules.md` when structuring your PR body and titles.
