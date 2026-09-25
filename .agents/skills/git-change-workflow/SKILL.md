---
name: git-change-workflow
description: Choose and apply a disciplined Git branching and commit workflow for Revit plugins implementation, refactoring, bug-fixing, documentation, and configuration. Trigger whenever determining branching strategy, creating a feature branch, staging files, or planning a series of atomic commits.
---

# Git Change Workflow for Revit Plugins

This skill defines the Git branching, staging, and commit discipline for all work in the `nodeaec/revit-plugins` repository.

---

## 🚦 1. Choose the Delivery Tier

Before editing, classify the task to determine the appropriate Git strategy:

| Tier | Typical Scope | Git Policy |
|---|---|---|
| **Fast Track** | Typo fix, single-file doc update, isolated constant edit, or minor comment adjustment. | Stay on the current branch. Make the change, validate, and leave unstaged/uncommitted unless explicitly instructed. |
| **Planned Track** | New add-in features, multi-file refactors, Ribbon UI changes, build/release script updates, or security adjustments. | Create a dedicated branch from `main` (`feat/<slug>` or `fix/<slug>`). Commit verified units atomically. |

---

## 🌿 2. Planned-Track Branching Workflow

1. **Inspect Repository State**:
   Run read-only inspection commands before editing:
   ```powershell
   git status --short
   git branch --show-current
   git log -1 --oneline
   ```
2. **Preserve User Changes**:
   Never run destructive commands (`git reset --hard`, `git clean -fd`, broad stashes) without explicit user permission.
3. **Branch Creation**:
   - Features, documentation, or tooling: `git checkout -b feat/<short-slug>`
   - Bug fixes and defect corrections: `git checkout -b fix/<short-slug>`
4. **Work in Verifiable Slices**:
   Break down the task into logical, independently verifiable slices. Do not mix unrelated cleanup into functional feature slices.

---

## 📦 3. Atomic Commit Workflow

Every commit must represent one logical, cohesive unit that compiles cleanly:

### Step 1: Validate Before Staging
Before staging, ensure the solution builds with **0 errors**:
```powershell
dotnet build NodeAec.Connector\NodeAec.Connector.sln -c Release
```

### Step 2: Explicit Staging
Stage only the relevant files for the logical slice. Never stage indiscriminately:
```powershell
# Good: explicit files
git add NodeAec.Connector/src/NodeAec.Connector/Config/ConnectorConfig.cs

# Forbidden: blind staging
git add .
git add -A
```

### Step 3: Inspect the Staged Diff
Verify that no unintended files, binaries (`bin/`, `obj/`, `release/`), or secrets were staged:
```powershell
git status --short
git diff --cached --stat
```

### Step 4: Commit with Semantic Format
Format the commit message according to the repository's `semantic-commit` skill:
```powershell
git commit -m "<type>(<scope>): <imperative summary>"
```

*Example Scopes*: `licensing`, `ribbon`, `client`, `ui`, `commands`, `config`, `release`, `deps`, `docs`.

---

## ✅ Git Workflow Checklist

- [ ] Fast track vs Planned track was chosen appropriately.
- [ ] Working branch was created with standard naming (`feat/*` or `fix/*`).
- [ ] No build artifacts (`bin/`, `obj/`, `release/`) or IDE files (`.vs/`) are staged.
- [ ] Solution compiles cleanly with **0 errors** before commit.
- [ ] Commit message strictly follows `<type>(<scope>): <summary>` (lowercase, imperative, ≤ 72 chars).
