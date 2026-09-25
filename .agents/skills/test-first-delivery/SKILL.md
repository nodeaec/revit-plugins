---
name: test-first-delivery
description: Deliver behavior changes, bug fixes, refactors, and feature work in C# and Revit plugins with test-driven discipline. Trigger whenever implementing business logic, fixing bugs, refactoring licensing or client code, writing unit tests, or when asked to "write tests", "test-first", "TDD", "run unit tests", or "verify behavior".
---

# Test-First Delivery for C# & Revit Add-ins

This skill defines the test-driven development (TDD) and verification process for Autodesk Revit plugins and Node.aec libraries.

---

## 🎯 The Core Architecture for Testability

Testing inside a live Autodesk Revit session is slow, UI-bound, and difficult to automate in CI. Therefore, test-first delivery requires **separating headless business logic from Revit UI and Document APIs**:

```text
┌─────────────────────────────────────────────────────────────┐
│                 Headless Business Domain                    │
│   (NodeAecLicenseClient, Cryptography, DPAPI, DTOs, JSON)   │
│   100% Automated Unit Testing via 'dotnet test'             │
└──────────────────────────────┬──────────────────────────────┘
                               │ Injected into
┌──────────────────────────────▼──────────────────────────────┐
│                    Revit UI & Adapter Layer                 │
│         (App.cs, ManageLicenseCommand, RibbonHelper)        │
│    Verified via compilation, AdWindows hooks & manual UI    │
└─────────────────────────────────────────────────────────────┘
```

---

## 🔄 The IV-TDD Workflow (Independent Verification)

### Phase 0: Define the Behavioral Contract
Before writing code, define the observable requirements and edge cases:
- *Example*: "Token with expired `exp` timestamp must return `IsValid = false` and `ErrorMessage = 'Licença expirada'`."
- *Example*: "Machine identity hash must produce the identical 64-character hex string for the same `MachineGuid` + `MachineName`."

### Phase 1: Author the Unit Tests First
Create or update tests in a test project (e.g. `NodeAec.Connector.Tests`) targeting .NET 8 using xUnit, NUnit, or MSTest:

```csharp
[Fact]
public void ValidateToken_WithTamperedSignature_ReturnsInvalid()
{
    // Arrange
    var client = LicenseConfig.CreateClient();
    string tamperedToken = "header.tampered_payload.signature";

    // Act
    var result = client.ValidateLeaseTokenOffline(tamperedToken);

    // Assert
    Assert.False(result.IsValid);
    Assert.Contains("Assinatura inválida", result.ErrorMessage);
}
```

### Phase 2: Run Tests to Verify Failure (Red)
Run the tests using the .NET CLI:
```powershell
dotnet test NodeAec.Connector/tests/NodeAec.Connector.Tests/NodeAec.Connector.Tests.csproj
```
Verify that the test fails for the expected reason, not due to compilation errors.

### Phase 3: Implement Minimum Code (Green)
Write the simplest, most readable C# code that satisfies the test contract. Adhere strictly to KISS and YAGNI.

### Phase 4: Refactor with Clean Code Standards
Clean up code, add XML doc comments, flatten nested conditionals, and ensure no dead code remains. Re-run `dotnet test` to confirm everything stays green.

---

## 🔍 What to Cover with Unit Tests

1. **Cryptographic Verification**:
   - Valid Ed25519 signature passes.
   - Corrupted signature or altered payload fails.
   - Malformed base64url strings do not throw unhandled exceptions.
2. **Offline Lease Caching**:
   - Cache reading when file exists.
   - Graceful fallback when cache file is missing or unreadable.
   - Expiration boundary testing (token valid 1 second before `exp`, invalid 1 second after).
3. **Machine Identity & DPAPI**:
   - Round-trip encryption and decryption with `ProtectedData`.
   - Consistent SHA-256 hash generation.
4. **Input Sanitization**:
   - Formatting and cleaning of user-entered license keys (`NAEC-XXXX-...`).

---

## ✅ Test Delivery Checklist

- [ ] Behavioral contract was specified before writing implementation.
- [ ] Headless logic is isolated from Revit API dependencies to enable CLI testing.
- [ ] Tests run and pass cleanly via `dotnet test` (**0 failures**).
- [ ] Edge cases (null inputs, corrupted tokens, expired timestamps) are covered.
- [ ] All touched code is documented with XML comments.
