using NodeAec.Licensing.Client;
using Xunit;

namespace NodeAec.Licensing.Tests;

public class LicenseValidationResultTests
{
    [Fact]
    public void Success_SetsValidOnlineDefaults()
    {
        var exp = DateTime.UtcNow.AddDays(30);

        var r = LicenseValidationResult.Success("NAEC-K", "prd", "commercial", exp, "Prod", "https://x");

        Assert.True(r.IsValid);
        Assert.False(r.IsOffline);
        Assert.Equal(LicenseStatus.Valid, r.Status);
        Assert.Equal("NAEC-K", r.LicenseKey);
        Assert.Equal("prd", r.ProductPublicId);
        Assert.Equal("commercial", r.LicenseType);
        Assert.Equal(exp, r.ExpiresAt);
        Assert.Equal("Prod", r.ProductName);
        Assert.Equal("https://x", r.ProductUrl);
        Assert.Null(r.ErrorMessage);
    }

    [Fact]
    public void Success_WithOfflineFlag_SetsValidOffline()
    {
        var r = LicenseValidationResult.Success("K", "p", "t", null, isOffline: true);

        Assert.True(r.IsValid);
        Assert.True(r.IsOffline);
        Assert.Equal(LicenseStatus.ValidOffline, r.Status);
    }

    [Fact]
    public void Failure_SetsInvalidWithStatusAndMessage()
    {
        var r = LicenseValidationResult.Failure(LicenseStatus.SeatLimitReached, "full");

        Assert.False(r.IsValid);
        Assert.False(r.IsOffline);
        Assert.Equal(LicenseStatus.SeatLimitReached, r.Status);
        Assert.Equal("full", r.ErrorMessage);
    }
}
