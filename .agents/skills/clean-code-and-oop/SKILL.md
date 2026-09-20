---
name: clean-code-and-oop
description: Authoritative engineering standards for writing Clean Code, Object-Oriented Programming (OOP) architectures, SOLID design principles, and safe refactoring in C# (.NET 8) and Autodesk Revit plugins. Trigger automatically whenever creating new features, writing classes, designing domain entities, refactoring existing code, restructuring methods, reviewing code quality, or simplifying complex logic.
---

# Clean Code & OOP Standards for C# / Revit Plugins

This skill defines the authoritative engineering standards for writing clean, object-oriented, and maintainable C# code across all Autodesk Revit add-ins and Node.aec tools.

---

## 1. Core Engineering Principles

```text
┌─────────────────────────────────────────────────────────────────────────────┐
│                           The Pragmatic Foundation                          │
├─────────────────────────────────────────────────────────────────────────────┤
│  KISS: Choose the most direct, readable solution over cleverness.           │
│  YAGNI: Code strictly for current requirements, never future guesses.       │
│  DRY & Rule of Three: Copy twice, abstract on the 3rd occurrence.           │
│  SOLID: Maintain single-responsibility classes and stable interfaces.       │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. C# 12 & .NET 8 Clean Code Standards

### 1. Naming & Style Conventions
- **PascalCase**: Classes, records, structs, interfaces, methods, properties, public constants, and namespaces.
- **camelCase**: Method parameters and local variables.
- **Interface Prefix**: Always prefix interfaces with `I` (e.g. `ILicenseClient`, `IExternalCommand`).
- **Domain-Driven Naming**: Use intention-revealing domain nouns (`LicenseLease`, `MachineIdentity`). Avoid vague suffixes like `Manager`, `Data`, `Info`, `Processor` unless required by external APIs.
- **Boolean Predicates**: Prefix booleans with descriptive verbs (`isValid`, `hasExpired`, `canExecute`).

### 2. Method & Function Hygiene
- **Single Responsibility (SRP)**: Each method must perform one focused task at a consistent level of abstraction. Keep methods under ~30-40 lines whenever practical.
- **Bounded Parameters**: Aim for 0–3 parameters. If 4 or more related parameters are needed, group them into a cohesive parameter object, DTO, or `record`.
- **Command-Query Separation (CQS)**:
  - Methods that mutate state (e.g. `SaveCachedLeaseToken`) should perform the action without returning complex query objects with hidden side-effects.
  - Methods that query state (e.g. `ValidateLicenseAsync`) should be side-effect free.
- **Guard Clauses & Early Returns**:
  - Handle validations, null checks, and edge cases at the top of the method.
  - Eliminate nested `if/else` ladders (the arrow anti-pattern).

### 3. Null Safety & Modern Language Features
- **Nullable Reference Types**: Project has `<Nullable>enable</Nullable>`. Treat warnings seriously:
  - Use `?` for genuinely optional values (`string? productSlug`).
  - Use null-coalescing (`??`, `??=`) and null-conditional (`?.`) operators.
  - Never suppress nullability with the null-forgiving operator `!` unless proving null-safety is impossible due to external interop.

---

## 3. Revit Add-in Architecture & Decoupling

In Revit plugin development, mixing UI, Revit document transactions, and business logic into monolithic classes creates fragile, unmaintainable code.

### 1. The Three-Layer Separation
1. **Entry & Ribbon Layer (`App.cs`)**:
   - Sole responsibility: register the `Node.aec` ribbon tab, create panels, register push buttons, and hook application lifecycle events.
   - Never place heavy business logic or blocking network calls in `App.cs`.
2. **Command Coordinator Layer (`IExternalCommand`)**:
   - Acts as an entry orchestrator: checks license gating, starts Revit `Transaction` when writing to `Document`, and invokes domain services or UI windows.
   - Keep `Execute()` thin and readable.
3. **Domain & Client Layer (`Client/`, `Model/`)**:
   - Independent of the Revit API wherever possible.
   - `NodeAecLicenseClient` handles HTTP, Ed25519 cryptography, and DPAPI persistence without referencing `Autodesk.Revit.UI`. This enables isolated unit testing.

### 2. Encapsulation & Immutability
- Make class properties read-only or private-set by default: `{ get; init; }` or `{ get; private set; }`.
- Never expose mutable collections (`List<T>`); expose `IReadOnlyList<T>` or `IEnumerable<T>`.

---

## ✅ Clean Code Checklist

- [ ] Method names express clear intent with active verbs.
- [ ] Methods use early returns to exit fast on invalid input.
- [ ] No deeply nested `if/else` blocks (> 2 levels deep).
- [ ] Revit UI is strictly separated from domain/licensing logic.
- [ ] Nullable warnings are resolved with safe checks, not bypassed with `!`.
- [ ] Dead code, commented blocks, and temporary debug prints are completely removed.
- [ ] Classes adhere to Single Responsibility and composition over inheritance.
