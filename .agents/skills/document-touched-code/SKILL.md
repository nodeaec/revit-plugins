---
name: document-touched-code
description: Document touched code with standard C# XML doc comments (/// <summary>, <param>, <returns>) and focused inline section comments whenever implementing a feature, refactoring, fixing bugs, or improving maintainability. Apply by default for all C# code-writing tasks unless the user explicitly requests no comments.
---

# Document Touched Code (C# & XML Documentation)

Treat documentation as an integral part of code implementation, not an afterthought. When you touch or modify C# code, leave the modified methods and classes easier to understand and maintain than you found them.

---

## 📖 C# XML Documentation Standards

In C# and .NET, XML documentation comments (`///`) power Visual Studio, Rider, and IDE IntelliSense for consumers of the public SDK and add-in components.

### 1. Document Every Public & Protected Member
Add or update XML comments on:
- All `public` and `internal` classes, structs, records, and interfaces.
- All public methods, properties, and constructors.
- Materially altered private methods where non-obvious logic resides.

### 2. Standard XML Tags
- **`<summary>`**: High-level explanation of the member's purpose, design rationale, and domain role.
- **`<param name="...">`**: Description of each parameter, its expected format, constraints, and valid ranges.
- **`<returns>`**: Explanation of the return value, including `null` semantics if nullable.
- **`<exception cref="...">`**: Document expected typed exceptions (e.g. `ArgumentNullException`, `InvalidOperationException`).

### 3. Example Pattern

```csharp
/// <summary>
/// Valida a concessão (lease) da licença ativa contra a chave pública Ed25519 da Node.aec.
/// Prioriza a verificação offline do token em cache antes de tentar sincronização com a rede.
/// </summary>
/// <param name="allowOffline">Se verdadeiro, permite validar o lease local mesmo sem conexão ativa.</param>
/// <param name="cancellationToken">Token para cancelamento da requisição HTTP.</param>
/// <returns>Resultado da validação com status da licença, cotas e eventuais mensagens de erro.</returns>
/// <exception cref="InvalidOperationException">Lançada se a chave pública Ed25519 configurada for inválida.</exception>
public async Task<LicenseValidationResult> ValidateLicenseAsync(
    bool allowOffline = true,
    CancellationToken cancellationToken = default)
{
    // ...
}
```

---

## 🔍 Inline Section Comments for Complex Logic

Use short, focused comments to explain **why** code was written, not **what** the syntax obviously does.

### Where Comments Add Real Value:
- **Platform Invariants**: Explaining why `BitmapCacheOption.OnLoad` is used (prevents file locking in Revit).
- **Criptografia & Encoding**: Detailing byte offsets, Base64Url conversions, or SHA-256 derivations.
- **Defensive Interop**: Explaining why a Revit API call is wrapped in a `try/catch` or dispatched to the STA thread.
- **AdWindows Lifecycle**: Explaining the timing of deduplication hooks during Revit initialization.

### Anti-Patterns to Avoid:
- ❌ Repeating the code: `// Set id to 1` followed by `Id = 1;`.
- ❌ Stale documentation: Leaving old XML comments after changing parameter types or method behavior.
- ❌ Commenting out dead code blocks instead of deleting them.

---

## ✅ Documentation Checklist

- [ ] Every touched public class and interface has a clear `<summary>` tag.
- [ ] Parameter names in `<param>` tags match the current method signature.
- [ ] Nullable return values (`string?`, `T?`) explain what `null` signifies in the `<returns>` tag.
- [ ] Complex multi-step operations have section comments explaining the purpose of each phase.
- [ ] No commented-out code blocks or outdated comments remain in touched files.
