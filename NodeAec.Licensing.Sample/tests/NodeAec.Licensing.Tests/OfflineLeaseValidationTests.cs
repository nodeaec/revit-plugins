using NodeAec.Licensing.Client;
using Xunit;

namespace NodeAec.Licensing.Tests;

public class OfflineLeaseValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyToken_ReturnsUnlicensed(string? token)
    {
        using var client = TestFactory.CreateClient();

        var r = client.ValidateLeaseTokenOffline(token!);

        Assert.False(r.IsValid);
        Assert.Equal(LicenseStatus.Unlicensed, r.Status);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    public void MalformedToken_ReturnsInvalidLease(string token)
    {
        using var client = TestFactory.CreateClient();

        var r = client.ValidateLeaseTokenOffline(token);

        Assert.False(r.IsValid);
        Assert.Equal(LicenseStatus.InvalidLease, r.Status);
    }

    [Fact]
    public void UnparsablePayload_ReturnsInvalidLease()
    {
        using var client = TestFactory.CreateClient();

        var r = client.ValidateLeaseTokenOffline("a.!!!.c");

        Assert.False(r.IsValid);
        Assert.Equal(LicenseStatus.InvalidLease, r.Status);
    }

    [Fact]
    public void WrongMachine_ReturnsInvalidLease()
    {
        using var client = TestFactory.CreateClient();
        var token = JwtHelper.CreateLeaseToken(JwtHelper.ValidPayload(new string('0', 64)));

        var r = client.ValidateLeaseTokenOffline(token);

        Assert.False(r.IsValid);
        Assert.Equal(LicenseStatus.InvalidLease, r.Status);
        Assert.Contains("different machine", r.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpiredLease_ReturnsExpired()
    {
        using var client = TestFactory.CreateClient();
        var token = JwtHelper.CreateLeaseToken(
            JwtHelper.ValidPayload(client.GetMachineId(), expiresAt: DateTimeOffset.UtcNow.AddHours(-1)));

        var r = client.ValidateLeaseTokenOffline(token);

        Assert.False(r.IsValid);
        Assert.Equal(LicenseStatus.Expired, r.Status);
    }

    [Fact]
    public void ValidLease_ReturnsValidOfflineWithProduct()
    {
        using var client = TestFactory.CreateClient();
        var exp = DateTimeOffset.UtcNow.AddDays(10);
        var token = JwtHelper.CreateLeaseToken(JwtHelper.ValidPayload(client.GetMachineId(), expiresAt: exp));

        var r = client.ValidateLeaseTokenOffline(token);

        Assert.True(r.IsValid);
        Assert.True(r.IsOffline);
        Assert.Equal(LicenseStatus.ValidOffline, r.Status);
        Assert.Equal("NAEC-TEST-KEY", r.LicenseKey);
        Assert.Equal("my-product", r.ProductPublicId);
        Assert.Equal("My Product", r.ProductName);
        Assert.Equal("https://nodeaec.com.br/products/my-product", r.ProductUrl);
        Assert.Equal("commercial", r.LicenseType);
        Assert.NotNull(r.ExpiresAt);
        Assert.Equal(exp.UtcDateTime.Date, r.ExpiresAt!.Value.Date);
    }

    [Fact]
    public void ValidLease_WithoutProductName_SucceedsWithNullName()
    {
        using var client = TestFactory.CreateClient();
        var token = JwtHelper.CreateLeaseToken(
            JwtHelper.ValidPayload(client.GetMachineId(), productName: null));

        var r = client.ValidateLeaseTokenOffline(token);

        Assert.True(r.IsValid);
        Assert.Null(r.ProductName);
        Assert.Equal("https://nodeaec.com.br/products/my-product", r.ProductUrl);
    }

    [Fact]
    public void AnySignature_IsAccepted_OfflineSignatureNotVerified()
    {
        // SECURITY NOTE: locks current behavior, not desired behavior.
        // ValidateLeaseTokenOffline never touches _publicKeyPem, so a forged
        // token with correct machine + expiry validates. If Ed25519
        // verification lands, this test MUST be rewritten to expect InvalidLease.
        using var client = TestFactory.CreateClient();
        var forged = JwtHelper.CreateLeaseToken(JwtHelper.ValidPayload(client.GetMachineId()));

        var r = client.ValidateLeaseTokenOffline(forged);

        Assert.True(r.IsValid);
    }
}
