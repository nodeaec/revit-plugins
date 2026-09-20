---
name: code-review
description: Review changes in the Autodesk Revit plugins repository along two axes — Standards (Revit API conventions, Ribbon guard, clean code, security, build isolation) and Spec (functional requirements and edge cases). Trigger whenever asked to "review the code", "code review", "audit diff", "check PR", "verify fixes", or before merging significant changes.
---

# Code Review for Revit Plugins

This skill executes a rigorous two-axis review of Git changes or work-in-progress code in the `nodeaec/revit-plugins` repository:

1. **Standards Axis**: Does the code conform to Revit API guidelines, clean C# standards, security invariants, and build isolation rules?
2. **Spec Axis**: Does the code faithfully and completely implement the originating requirement without regressions or scope creep?

---

## 🔍 The Standards Review Axis (Revit & C# Invariants)

Evaluate the diff against these strict repository standards:

### 1. Ribbon & UI Ergonomics
- [ ] **Tab Name**: Are all commands placed on the **`Node.aec`** tab? Never allow fragmented or custom plugin tabs.
- [ ] **Defensive Acquisition**: Does ribbon setup check if panels or buttons already exist before creating them to avoid `ArgumentException` on add-in reload?
- [ ] **Icon File Locking**: Are `BitmapImage` assets loaded with `BitmapCacheOption.OnLoad` and `.Freeze()` to avoid locking PNG files on disk?
- [ ] **STA Thread**: Are all WPF windows and `TaskDialog` instances executed strictly on Revit's main UI STA thread?

### 2. Dependency Isolation & Build Hygiene
- [ ] **Revit API Assemblies**: Do references to `RevitAPI.dll`, `RevitAPIUI.dll`, and `AdWindows.dll` have `<Private>false</Private>`?
- [ ] **NuGet Exclusion**: Do NuGet references declare `PrivateAssets="all"` and `ExcludeAssets="runtime"` so `<CopyLocalLockFileAssemblies>` doesn't leak Revit DLLs into the output?
- [ ] **Compilation**: Does the build execute with **0 errors**?
- [ ] **Packaging Filter**: Does `release.ps1` explicitly exclude `RevitAPI*`, `AdWindows.dll`, and `UIFramework*` from the release payload?

### 3. Security & Licensing Invariants
- [ ] **No Private Keys**: Is the code strictly using the public SPKI Ed25519 key?
- [ ] **DPAPI Encryption**: Are cached lease tokens encrypted with `ProtectedData.Protect`?
- [ ] **Fail Closed**: Do protected commands deny execution when a license is invalid or expired?
- [ ] **Production API**: Does network communication target `https://api.nodeaec.com.br` without local dev fallbacks?

### 4. C# Code Quality & Documentation
- [ ] **Clean OOP**: Is business logic decoupled from the Revit API?
- [ ] **XML Docs**: Are all touched public classes, methods, and properties documented with XML comments?
- [ ] **Zero Dead Code**: Are commented-out blocks, temporary debug prints, and unused variables deleted?

---

## 🎯 The Spec Review Axis (Functional Integrity)

Evaluate whether the code solves the actual user problem:

1. **Requirements Coverage**: Are all requested features, options, and behaviors implemented?
2. **Edge Cases**: What happens on network timeout, corrupted token file, missing Revit API path, or concurrent document switches?
3. **Scope Creep**: Did the change introduce unnecessary abstractions, unrequested features, or breaking changes?
4. **Self-Consistency**: Does the diff match the commit message and documentation claims?

---

## 📋 Reporting Format

Report findings in this structured format:

```text
location (file:line) · category (standards violation | bug | missing edge case) · severity (high | medium | low)
Evidence: quote the exact lines of code.
Impact: what breaks (user experience, stability, maintainability, security).
Recommended Fix: exact drop-in replacement or structural adjustment.
```

### Concluding Verdict:
- **PASS**: Meets all standards and spec requirements with 0 high/medium issues.
- **PASS WITH EDITS**: Minor non-blocking nits or documentation updates needed.
- **REWORK**: Critical defects, security violations, build failures, or broken Revit invariants.
