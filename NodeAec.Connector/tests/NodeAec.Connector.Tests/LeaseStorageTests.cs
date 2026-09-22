using System;
using System.Collections.Generic;
using System.IO;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;
using Xunit;

namespace NodeAec.Connector.Tests;

public class LeaseStorageTests : IDisposable
{
    private readonly string _tempDir;

    public LeaseStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NodeAecTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        LeaseStorage.SetCustomBasePath(_tempDir);
    }

    public void Dispose()
    {
        LeaseStorage.SetCustomBasePath(null);
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public void SaveAndLoadMasterLease_PersistsTokenCorrectly()
    {
        string dummyJwt = "header.payload.signature";

        LeaseStorage.SaveMasterLease(dummyJwt);
        string? loaded = LeaseStorage.LoadMasterLease();

        Assert.Equal(dummyJwt, loaded);
    }

    [Fact]
    public void ClearMasterLease_RemovesFile()
    {
        LeaseStorage.SaveMasterLease("token-to-delete");
        Assert.NotNull(LeaseStorage.LoadMasterLease());

        LeaseStorage.ClearMasterLease();
        Assert.Null(LeaseStorage.LoadMasterLease());
    }

    [Fact]
    public void SaveAndLoadSession_PersistsNameEmailAndToken()
    {
        LeaseStorage.SaveSession("pablo@nodeaec.com.br", "jwt-user-token", "Pablo");

        var session = LeaseStorage.LoadSession();
        Assert.NotNull(session);
        Assert.Equal("Pablo", session.Value.Name);
        Assert.Equal("pablo@nodeaec.com.br", session.Value.Email);
        Assert.Equal("jwt-user-token", session.Value.Token);

        LeaseStorage.ClearSession();
        Assert.Null(LeaseStorage.LoadSession());
    }

    [Fact]
    public void LoadSession_LegacySessionWithPublicIdAsEmail_RecoversRealIdentityFromToken()
    {
        // Sessões antigas gravavam o id público (sub do lease) no campo "email".
        string userToken = TestHelpers.CreateUserSessionJwt("x1lwHOhSSguYA", "pablo@nodeaec.com.br", "Pablo");
        LeaseStorage.SaveSession("x1lwHOhSSguYA", userToken);

        var session = LeaseStorage.LoadSession();
        Assert.NotNull(session);
        Assert.Equal("pablo@nodeaec.com.br", session.Value.Email);
        Assert.Equal("Pablo", session.Value.Name);
        Assert.Equal(userToken, session.Value.Token);
    }

    [Fact]
    public void ParseUserSessionClaims_ExtractsIdentityFromUserToken()
    {
        string userToken = TestHelpers.CreateUserSessionJwt("x1lwHOhSSguYA", "arquiteta@escritorio.com.br", "Maria");

        var claims = LeaseStorage.ParseUserSessionClaims(userToken);

        Assert.NotNull(claims);
        Assert.Equal("x1lwHOhSSguYA", claims.Id);
        Assert.Equal("arquiteta@escritorio.com.br", claims.Email);
        Assert.Equal("Maria", claims.Name);
    }

    [Fact]
    public void ParseUserSessionClaims_ReturnsNullWithoutIdentityClaims()
    {
        // O lease mestre não tem email/nome: não deve ser tratado como sessão de usuário.
        var exp = DateTimeOffset.UtcNow.AddDays(30);
        string leaseJwt = TestHelpers.CreateMasterLeaseJwt("usr_123", "machine_abc", exp, new List<EntitlementItem>());

        Assert.Null(LeaseStorage.ParseUserSessionClaims(leaseJwt));
        Assert.Null(LeaseStorage.ParseUserSessionClaims(null));
        Assert.Null(LeaseStorage.ParseUserSessionClaims("not-a-jwt"));
    }

    [Fact]
    public void ParseJwtPayload_ExtractsClaimsSuccessfully()
    {
        var exp = DateTimeOffset.UtcNow.AddDays(30);
        var ents = new List<EntitlementItem>
        {
            new EntitlementItem
            {
                Slug = "revit-automator",
                Name = "Revit Automator",
                Type = "perpetual",
                Status = "active"
            }
        };

        string jwt = TestHelpers.CreateMasterLeaseJwt("usr_123", "machine_abc", exp, ents);
        var payload = LeaseStorage.ParseJwtPayload(jwt);

        Assert.NotNull(payload);
        Assert.Equal("usr_123", payload.Sub);
        Assert.Equal("machine_abc", payload.Mid);
        Assert.Equal("master-lease", payload.Scope);
        Assert.Single(payload.Entitlements);
        Assert.Equal("revit-automator", payload.Entitlements[0].Slug);
        Assert.False(payload.IsExpired);
    }
}
