---
name: semantic-commit
description: Generate precise Semantic Commit messages for Git changes in the repository. Trigger whenever preparing a commit, writing a commit message, or when asked to "commit these changes", "create a commit", "write commit message", or "format commit".
---

# Semantic Commit

Generate exactly one commit message using the Semantic Commit format. Keep it short, direct, and specific to the staged changes.

---

## 📝 Required Format

`<type>(<scope>): <short summary>`

- Use lowercase for `type` and `scope`.
- Keep the summary in imperative mood (e.g. `add`, `fix`, `update`, `remove`).
- Keep the full line at 72 characters or less when practical.
- Never omit the scope.
- Do not add emojis.
- Do not add a body or footer unless explicitly requested.

### ⚙️ Execution Modes
- **Generate Message Mode** (e.g. "write commit message", "suggest a commit"): Output ONLY the final commit message line and nothing else.
- **Commit Execution Mode** (e.g. "commit these changes", "create commit"): Inspect staged changes (`git status`, `git diff --cached`), compose the formatted message, and execute `git commit -m "<type>(<scope>): <short summary>"`. Stage specific files with `git add` if requested.

---

## 🏷️ Types

Use the highest matching category in this order:

1. `fix`: correct broken behavior, bad output, crashes, incorrect state, security issues, or invalid validation.
2. `feat`: add user-facing behavior, supported options, workflows, UI commands, or capabilities.
3. `refactor`: restructure implementation without changing observable behavior.
4. `test`: add or update tests, test fixtures, or test tooling.
5. `docs`: change documentation, prompts, instructions, comments, or examples.
6. `style`: formatting, whitespace, lint-only style, spelling, or naming without behavior changes.
7. `chore`: dependencies, build tooling, CI, scripts, repository maintenance, or packaging scripts.

---

## 🎯 Scopes for Revit Plugins

Infer scope from staged paths and primary purpose:

- `licensing`: license client, crypto, DPAPI, lease validation, activation.
- `ribbon`: Revit ribbon tabs, panels, push buttons, icons, AdWindows deduplication.
- `client`: HTTP API communication, JSON payloads, catalog resolution.
- `ui`: WPF windows, dialogs, status controls, view models.
- `commands`: `IExternalCommand` implementations and commercial gating.
- `config`: `LicenseConfig`, environment variables, endpoints.
- `release`: packaging scripts (`release.ps1`), `.addin` manifests, staging.
- `deps`: NuGet packages, assembly references, .NET runtime targets.
- `docs`: README files, agent guides, prompts.
- `repo`: repository-wide maintenance or changes affecting multiple areas.

---

## 💡 Examples

- `feat(licensing): add automatic product catalog resolution`
- `fix(ribbon): deduplicate node.aec tab on addin reload`
- `docs(readme): add ai agent prompt for licensing integration`
- `refactor(client): simplify ed25519 lease token signature verification`
- `chore(release): package protecteddata dll in stage output`
