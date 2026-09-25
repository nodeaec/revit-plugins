using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NodeAec.Connector.Client;
using NodeAec.Connector.Hardware;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;
using Org.BouncyCastle.Crypto.Parameters;
using Xunit;

namespace NodeAec.Connector.Tests;

public class ConnectorApiClientTests : IDisposable
{
    private readonly string _tempDir;

    public ConnectorApiClientTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NodeAecApiTests_" + Guid.NewGuid().ToString("N"));
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
    public async Task SyncMasterEntitlementsAsync_WithoutToken_FailsFast()
    {
        using var client = new ConnectorApiClient();

        var result = await client.SyncMasterEntitlementsAsync(string.Empty);

        Assert.False(result.Success);
        Assert.Contains("ausente", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SyncMasterEntitlementsAsync_OnSuccess_SavesLeaseAndCachesJwks()
    {
        // Lease realmente assinado (EdDSA + kid) com as claims que a plataforma emite:
        // a partir de M1 ele precisa passar na verificação de assinatura/scope/mid antes
        // de ser gravado.
        string mockLease = TestHelpers.CreateMasterLeaseJwt(
            "usr_1",
            HardwareId.GetMachineId(),
            DateTimeOffset.UtcNow.AddDays(30),
            new List<EntitlementItem>
            {
                new EntitlementItem { Slug = "revit-automator", Status = "active" }
            });
        var mockResponse = new
        {
            success = true,
            leaseToken = mockLease,
            expiresAt = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
            grantedCount = 1,
            totalCount = 1,
            entitlements = new[]
            {
                new
                {
                    slug = "revit-automator",
                    name = "Revit Automator",
                    type = "perpetual",
                    status = "active",
                    granted = true
                }
            }
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/license/jwks")
            {
                Assert.Equal(HttpMethod.Get, req.Method);
                return JwksResponse();
            }

            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("/account/entitlements/lease", req.RequestUri.AbsolutePath);
            Assert.NotNull(req.Headers.Authorization);
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("valid-user-jwt", req.Headers.Authorization!.Parameter);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(mockResponse))
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.SyncMasterEntitlementsAsync("valid-user-jwt");

        Assert.True(result.Success);
        // M1: a assinatura foi conferida com o JWKS servido na mesma operação.
        Assert.True(result.KeysVerified);
        Assert.True(result.JwksRefreshed);
        Assert.Null(result.VerificationWarning);
        Assert.Equal(1, result.GrantedCount);
        Assert.Single(result.Entitlements);
        Assert.Equal("revit-automator", result.Entitlements[0].Slug);
        Assert.Equal(mockLease, LeaseStorage.LoadMasterLease());
        // O JWKS precisa estar em cache para o gate verificar a assinatura offline.
        Assert.True(File.Exists(SigningKeyStore.GetJwksFilePath()));
        Assert.NotEmpty(SigningKeyStore.LoadVerificationKeys());
    }

    [Fact]
    public async Task SyncMasterEntitlementsAsync_ApiError_UsesStableCodeCopy()
    {
        // Formato real do middleware de erro da API: { error: true, status, type, code, message }.
        var errorResponse = new
        {
            error = true,
            status = 403,
            type = "Forbidden",
            code = "ACTIVATION_LIMIT_REACHED",
            message = "Seat limit reached."
        };

        var handler = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(JsonSerializer.Serialize(errorResponse))
            });

        using var httpClient = new HttpClient(handler);
        using var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.SyncMasterEntitlementsAsync("valid-user-jwt");

        Assert.False(result.Success);
        Assert.Contains("Limite de assentos simultâneos atingido", result.Message);
    }

    // ---- M1: verificação de assinatura + scope + mid ANTES de SaveMasterLease ----

    [Fact]
    public async Task SyncMasterEntitlementsAsync_TamperedLease_RejectedBeforeSaving()
    {
        // O JWKS servido traz a chave de teste; o lease vem assinado por chave forjada.
        string forgedLease = CreateForgedMasterLease(HardwareId.GetMachineId());
        using var httpClient = new HttpClient(HandlerServing(new { success = true, leaseToken = forgedLease }));
        using var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.SyncMasterEntitlementsAsync("valid-user-jwt");

        // A recusa acontece ANTES de gravar: a mensagem é de verificação (não de falha de
        // disco) e nenhum lease toca o disco — o lease anterior, se houver, permaneceria.
        Assert.False(result.Success);
        Assert.Contains("verificação de segurança", result.Message);
        Assert.Null(LeaseStorage.LoadMasterLease());
    }

    [Fact]
    public async Task SyncMasterEntitlementsAsync_WrongMachineId_RejectedBeforeSaving()
    {
        // Assinatura e scope válidos, mas emitido para outra máquina: mid divergente.
        string leaseForAnotherMachine = TestHelpers.CreateMasterLeaseJwt(
            "usr_1",
            new string('x', 64),
            DateTimeOffset.UtcNow.AddDays(30),
            new List<EntitlementItem> { new EntitlementItem { Slug = "revit-automator", Status = "active" } });

        using var httpClient = new HttpClient(HandlerServing(new { success = true, leaseToken = leaseForAnotherMachine }));
        using var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.SyncMasterEntitlementsAsync("valid-user-jwt");

        Assert.False(result.Success);
        Assert.Contains("verificação de segurança", result.Message);
        Assert.Null(LeaseStorage.LoadMasterLease());
    }

    [Fact]
    public async Task SyncMasterEntitlementsAsync_NonMasterScope_RejectedBeforeSaving()
    {
        // Assinatura e mid válidos, mas scope de produto — não é um master lease.
        string pluginScopedLease = TestHelpers.CreateMasterLeaseJwt(
            "usr_1",
            HardwareId.GetMachineId(),
            DateTimeOffset.UtcNow.AddDays(30),
            new List<EntitlementItem> { new EntitlementItem { Slug = "revit-automator", Status = "active" } },
            scope: "plugin-license");

        using var httpClient = new HttpClient(HandlerServing(new { success = true, leaseToken = pluginScopedLease }));
        using var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.SyncMasterEntitlementsAsync("valid-user-jwt");

        Assert.False(result.Success);
        Assert.Contains("verificação de segurança", result.Message);
        Assert.Null(LeaseStorage.LoadMasterLease());
    }

    [Fact]
    public async Task SyncMasterEntitlementsAsync_JwksUnavailable_SavesButFlagsKeysUnverified()
    {
        string signedLease = TestHelpers.CreateMasterLeaseJwt(
            "usr_1",
            HardwareId.GetMachineId(),
            DateTimeOffset.UtcNow.AddDays(30),
            new List<EntitlementItem> { new EntitlementItem { Slug = "revit-automator", Status = "active" } });

        using var httpClient = new HttpClient(HandlerServing(new { success = true, leaseToken = signedLease }, jwksAvailable: false));
        using var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.SyncMasterEntitlementsAsync("valid-user-jwt");

        // Sem nenhuma chave disponível: o lease aprovado pelo servidor é salvo, mas o
        // resultado propaga "não verificado" para a UI — em vez de um sucesso enganoso
        // cuja falha só apareceria depois, de forma opaca, no gate.
        Assert.True(result.Success);
        Assert.False(result.KeysVerified);
        Assert.False(result.JwksRefreshed);
        Assert.NotNull(result.VerificationWarning);
        Assert.Contains("chaves de verificação", result.VerificationWarning!);
        Assert.Equal(signedLease, LeaseStorage.LoadMasterLease());
    }

    [Fact]
    public async Task SyncMasterEntitlementsAsync_JwksRefreshFails_VerifiesWithCachedKeys()
    {
        // Chave em cache de uma execução anterior; o refresh desta operação falha.
        TestHelpers.InstallTestSigningKey();
        string signedLease = TestHelpers.CreateMasterLeaseJwt(
            "usr_1",
            HardwareId.GetMachineId(),
            DateTimeOffset.UtcNow.AddDays(30),
            new List<EntitlementItem> { new EntitlementItem { Slug = "revit-automator", Status = "active" } });

        using var httpClient = new HttpClient(HandlerServing(new { success = true, leaseToken = signedLease }, jwksAvailable: false));
        using var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.SyncMasterEntitlementsAsync("valid-user-jwt");

        // O refresh falhou (propagado em JwksRefreshed), mas a assinatura foi conferida
        // com o cache anterior: verificado, com aviso de renovação pendente.
        Assert.True(result.Success);
        Assert.True(result.KeysVerified);
        Assert.False(result.JwksRefreshed);
        Assert.NotNull(result.VerificationWarning);
        Assert.Equal(signedLease, LeaseStorage.LoadMasterLease());
    }

    [Fact]
    public async Task ValidateHeartbeatAsync_TamperedRenewal_RejectedBeforeSaving()
    {
        string forgedRenewal = CreateForgedMasterLease(HardwareId.GetMachineId());
        using var httpClient = new HttpClient(HandlerServing(
            new { success = true, valid = true, scope = "master-lease", leaseToken = forgedRenewal }));
        using var client = new ConnectorApiClient("https://api.test", httpClient);

        // Token de entrada via parâmetro: o arrange não grava nada em disco.
        var result = await client.ValidateHeartbeatAsync("lease-existente.nao-gravado.token");

        Assert.False(result.Success);
        Assert.Contains("verificação de segurança", result.Message);
        Assert.Null(LeaseStorage.LoadMasterLease());
    }

    [Fact]
    public async Task ValidateHeartbeatAsync_UnmappedCode_FallsBackToServerMessage()
    {
        var errorResponse = new
        {
            error = true,
            status = 403,
            type = "Forbidden",
            code = "SOME_FUTURE_CODE",
            message = "Mensagem específica do servidor."
        };

        var handler = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(JsonSerializer.Serialize(errorResponse))
            });

        using var httpClient = new HttpClient(handler);
        using var client = new ConnectorApiClient("https://api.test", httpClient);
        LeaseStorage.SaveMasterLease("hdr.payload.sig");

        var result = await client.ValidateHeartbeatAsync();

        Assert.False(result.Success);
        Assert.Contains("Mensagem específica do servidor.", result.Message);
    }

    [Fact]
    public async Task ValidateHeartbeatAsync_NonJsonBody_FallsBackToHttpStatus()
    {
        var handler = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("<html>proxy error</html>")
            });

        using var httpClient = new HttpClient(handler);
        using var client = new ConnectorApiClient("https://api.test", httpClient);
        LeaseStorage.SaveMasterLease("hdr.payload.sig");

        var result = await client.ValidateHeartbeatAsync();

        Assert.False(result.Success);
        Assert.Contains("Servidor retornou código 502", result.Message);
    }

    [Fact]
    public async Task ValidateHeartbeatAsync_ValidFalse_ReturnsDomainFailure()
    {
        var response = new
        {
            success = false,
            valid = false,
            scope = "master-lease"
        };

        var handler = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response))
            });

        using var httpClient = new HttpClient(handler);
        using var client = new ConnectorApiClient("https://api.test", httpClient);
        LeaseStorage.SaveMasterLease("hdr.payload.sig");

        var result = await client.ValidateHeartbeatAsync();

        Assert.False(result.Success);
        Assert.Contains("não está mais válida", result.Message);
    }

    [Fact]
    public async Task ValidateHeartbeatAsync_UsesFreshEntitlementsAndSavesRenewedToken()
    {
        // Lease renovado realmente assinado — em M1 ele precisa passar na verificação
        // (assinatura + scope + mid) antes de sobrescrever o lease local.
        string renewedLease = TestHelpers.CreateMasterLeaseJwt(
            "usr_1",
            HardwareId.GetMachineId(),
            DateTimeOffset.UtcNow.AddDays(30),
            new List<EntitlementItem>
            {
                new EntitlementItem { Slug = "revit-automator", Status = "active" },
                new EntitlementItem { Slug = "parametric-doors", Status = "seat_released" }
            });
        var response = new
        {
            success = true,
            valid = true,
            scope = "master-lease",
            leaseToken = renewedLease,
            expiresAt = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
            entitlements = new[]
            {
                new { slug = "revit-automator", name = "Revit Automator", type = "perpetual", status = "active" },
                new { slug = "parametric-doors", name = "Portas", type = "subscription", status = "seat_released" }
            }
        };

        var handler = new MockHttpMessageHandler(req =>
            req.RequestUri!.AbsolutePath == "/license/jwks"
                ? JwksResponse()
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(response))
                });

        using var httpClient = new HttpClient(handler);
        using var client = new ConnectorApiClient("https://api.test", httpClient);
        LeaseStorage.SaveMasterLease("old.payload.sig");

        var result = await client.ValidateHeartbeatAsync();

        Assert.True(result.Success);
        Assert.True(result.KeysVerified);   // renovação verificada com o JWKS servido
        Assert.True(result.JwksRefreshed);
        Assert.Equal(2, result.Entitlements.Count);
        Assert.Equal(1, result.GrantedCount);   // apenas `active` conta como liberado
        Assert.Equal(2, result.TotalCount);     // status frescos vêm da resposta, não do token
        Assert.Equal("seat_released", result.Entitlements[1].Status);
        Assert.Equal(renewedLease, LeaseStorage.LoadMasterLease());
    }

    [Fact]
    public async Task ActivateKeyAsync_OnActivationLimitReached_ReturnsFriendlyMessage()
    {
        var errorResponse = new
        {
            error = true,
            status = 403,
            type = "Forbidden",
            code = "ACTIVATION_LIMIT_REACHED",
            message = "Seat limit reached."
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal("/license/activate", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(JsonSerializer.Serialize(errorResponse))
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.ActivateKeyAsync("NAEC-KEY1-KEY2-KEY3-KEY4");

        Assert.False(result.Success);
        Assert.Contains("Limite de assentos simultâneos atingido", result.Message);
    }

    [Fact]
    public async Task ActivateKeyAsync_DoesNotOverwriteMasterLease()
    {
        // Lease mestre com 3 produtos que já funciona nesta máquina.
        string masterLease = TestHelpers.CreateMasterLeaseJwt(
            "usr_1",
            HardwareId.GetMachineId(),
            DateTimeOffset.UtcNow.AddDays(20),
            new List<EntitlementItem>
            {
                new EntitlementItem { Slug = "revit-automator", Status = "active" },
                new EntitlementItem { Slug = "parametric-doors", Status = "active" },
                new EntitlementItem { Slug = "structural-lintels", Status = "active" },
            });
        LeaseStorage.SaveMasterLease(masterLease);

        // /license/activate devolve um lease de produto único, SEM claim `entitlements`.
        var activationResponse = new
        {
            success = true,
            leaseToken = "single-product.lease.token",
            expiresAt = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
            type = "subscription",
            status = "active",
            product = new { name = "Portas Paramétricas", slug = "parametric-doors" }
        };

        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(activationResponse))
        });

        using var httpClient = new HttpClient(handler);
        using var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.ActivateKeyAsync("NAEC-KEY1-KEY2-KEY3-KEY4");

        Assert.True(result.Success);
        Assert.Contains("Chave ativada", result.Message);
        // Regressão P0: o lease mestre nunca é substituído por um token de produto único.
        Assert.Equal(masterLease, LeaseStorage.LoadMasterLease());
    }

    /// <summary>
    /// Handler que serve o JWKS real da chave de teste em <c>/license/jwks</c> e responde
    /// <paramref name="apiResponse"/> em qualquer outro endpoint. Com
    /// <paramref name="jwksAvailable"/> = <c>false</c>, o refresh do JWKS falha (500).
    /// </summary>
    private static MockHttpMessageHandler HandlerServing(object apiResponse, bool jwksAvailable = true)
    {
        return new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/license/jwks")
            {
                return jwksAvailable
                    ? JwksResponse()
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("jwks indisponível")
                    };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(apiResponse))
            };
        });
    }

    /// <summary>
    /// Monta um lease master com estrutura perfeita, mas assinado por uma chave FORJADA
    /// (não publicada no JWKS): a assinatura jamais poderá conferir.
    /// </summary>
    private static string CreateForgedMasterLease(string mid, string scope = "master-lease")
    {
        byte[] attackerSeed = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes("attacker-seed"));

        return TestHelpers.CreateSignedJwt(
            new
            {
                iss = "node-aec",
                scope,
                mid,
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                exp = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
            },
            new Ed25519PrivateKeyParameters(attackerSeed, 0));
    }

    private static HttpResponseMessage JwksResponse()
    {
        string x = TestHelpers.EncodeBase64Url(TestHelpers.Rfc8032TestPublicKey);
        string jwks = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new { kty = "OKP", crv = "Ed25519", x, kid = TestHelpers.TestKeyId, use = "sig", alg = "EdDSA" }
            }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jwks)
        };
    }
}
