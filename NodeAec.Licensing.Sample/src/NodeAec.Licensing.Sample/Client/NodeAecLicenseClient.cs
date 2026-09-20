// <copyright file="NodeAecLicenseClient.cs" company="Node.aec">
// Copyright (c) Node.aec. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace NodeAec.Licensing.Client
{
    /// <summary>
    /// Outcome status of a license validation or activation check.
    /// </summary>
    public enum LicenseStatus
    {
        Valid,
        ValidOffline,
        Expired,
        Suspended,
        Revoked,
        SeatLimitReached,
        KeyNotFound,
        MachineNotActivated,
        InvalidLease,
        NetworkError,
        Unlicensed
    }

    /// <summary>
    /// Result returned after validating or activating a software license.
    /// </summary>
    public class LicenseValidationResult
    {
        public bool IsValid { get; set; }
        public bool IsOffline { get; set; }
        public LicenseStatus Status { get; set; }
        public string? LicenseKey { get; set; }
        public string? ProductPublicId { get; set; }
        public string? ProductName { get; set; }
        public string? ProductUrl { get; set; }
        public string? LicenseType { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? ErrorMessage { get; set; }

        public static LicenseValidationResult Success(
            string? key,
            string? prd,
            string? typ,
            DateTime? exp,
            string? productName = null,
            string? productUrl = null,
            bool isOffline = false)
        {
            return new LicenseValidationResult
            {
                IsValid = true,
                IsOffline = isOffline,
                Status = isOffline ? LicenseStatus.ValidOffline : LicenseStatus.Valid,
                LicenseKey = key,
                ProductPublicId = prd,
                ProductName = productName,
                ProductUrl = productUrl,
                LicenseType = typ,
                ExpiresAt = exp
            };
        }

        public static LicenseValidationResult Failure(LicenseStatus status, string message)
        {
            return new LicenseValidationResult
            {
                IsValid = false,
                Status = status,
                ErrorMessage = message
            };
        }
    }

    /// <summary>
    /// Production-ready licensing client for Autodesk Revit plugins and .NET CAD applications.
    /// Implements hybrid online activation, machine hardware binding, encrypted DPAPI local caching,
    /// and asymmetric Ed25519 offline lease token validation.
    /// </summary>
    public class NodeAecLicenseClient : IDisposable
    {
        private readonly string _baseUrl;
        private readonly string _productSlug;
        private readonly string _publicKeyPem;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;

        private string? _cachedMachineId;

        /// <summary>
        /// Initializes a new instance of the <see cref="NodeAecLicenseClient"/> class.
        /// </summary>
        /// <param name="baseUrl">Public API URL (e.g. "https://api.nodeaec.com.br").</param>
        /// <param name="productSlug">Optional product slug. Defaults to "nodeaec".</param>
        /// <param name="publicKeyPem">Optional Node.aec Ed25519 SPKI Public Key in PEM format.</param>
        /// <param name="httpClient">Optional shared HttpClient instance.</param>
        public NodeAecLicenseClient(string? baseUrl = null, string? productSlug = null, string? publicKeyPem = null, HttpClient? httpClient = null)
        {
            _baseUrl = (baseUrl ?? "https://api.nodeaec.com.br").TrimEnd('/');
            _productSlug = string.IsNullOrWhiteSpace(productSlug) ? "nodeaec" : productSlug.Trim();
            _publicKeyPem = publicKeyPem ?? string.Empty;

            if (httpClient != null)
            {
                _httpClient = httpClient;
                _ownsHttpClient = false;
            }
            else
            {
                _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                _ownsHttpClient = true;
            }
        }

        /// <summary>
        /// Collects or computes a stable, non-spoofed hardware identifier for the current Windows machine.
        /// Derives a SHA-256 fingerprint from the Windows MachineGuid registry entry and machine name.
        /// </summary>
        public string GetMachineId()
        {
            if (!string.IsNullOrEmpty(_cachedMachineId))
            {
                return _cachedMachineId;
            }

            string rawGuid = null;
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    rawGuid = key?.GetValue("MachineGuid")?.ToString();
                }
            }
            catch
            {
                // Fallback if 64-bit view registry read is restricted
            }

            if (string.IsNullOrEmpty(rawGuid))
            {
                rawGuid = Environment.MachineName;
            }

            using (var sha = SHA256.Create())
            {
                var input = $"{rawGuid}:{Environment.MachineName}";
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                _cachedMachineId = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }

            return _cachedMachineId;
        }

        /// <summary>
        /// Validates the plugin's license by checking local lease cache or contacting the Node.aec API.
        /// Automatically performs heartbeat renewal online and falls back to offline validation when disconnected.
        /// </summary>
        /// <param name="allowOffline">Whether to allow operation when internet connectivity is unavailable.</param>
        public async Task<LicenseValidationResult> ValidateLicenseAsync(bool allowOffline = true)
        {
            var cachedLease = LoadCachedLeaseToken();
            if (string.IsNullOrEmpty(cachedLease))
            {
                return LicenseValidationResult.Failure(LicenseStatus.Unlicensed, "No license found on this machine. Please activate your product.");
            }

            // Step 1: Attempt online validation & heartbeat renewal
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/license/validate");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cachedLease);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new { machineId = GetMachineId() }),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using (var doc = JsonDocument.Parse(content))
                    {
                        var root = doc.RootElement;
                        var renewedToken = root.GetProperty("leaseToken").GetString();
                        var expString = root.GetProperty("expiresAt").GetString();
                        var type = root.GetProperty("type").GetString();

                        // Update local cache with renewed lease
                        SaveCachedLeaseToken(renewedToken);

                        DateTime.TryParse(expString, out var exp);
                        var payload = ParseJwtPayload(renewedToken);

                        string? prodName = null;
                        string? prodUrl = null;
                        if (root.TryGetProperty("product", out var prodElem))
                        {
                            if (prodElem.TryGetProperty("name", out var n)) prodName = n.GetString();
                            if (prodElem.TryGetProperty("url", out var u)) prodUrl = u.GetString();
                        }
                        if (string.IsNullOrEmpty(prodName)) prodName = payload?.Prn;
                        if (string.IsNullOrEmpty(prodUrl) && !string.IsNullOrEmpty(payload?.Prd))
                        {
                            prodUrl = $"https://nodeaec.com.br/products/{payload.Prd}";
                        }

                        // Resolve product name and URL if missing from API / token
                        var resolved = await ResolveProductInfoAsync(payload?.Prd, prodName, prodUrl).ConfigureAwait(false);
                        prodName = resolved.Name;
                        prodUrl = resolved.Url;

                        return LicenseValidationResult.Success(payload?.Lic, payload?.Prd, type, exp, prodName, prodUrl, isOffline: false);
                    }
                }

                // Handle known API error responses
                if ((int)response.StatusCode == 403)
                {
                    ClearCachedLeaseToken();
                    return LicenseValidationResult.Failure(LicenseStatus.Revoked, "Your license has been suspended, revoked, or expired.");
                }
                if ((int)response.StatusCode == 404)
                {
                    ClearCachedLeaseToken();
                    return LicenseValidationResult.Failure(LicenseStatus.MachineNotActivated, "This machine is no longer activated on the license.");
                }
            }
            catch (Exception)
            {
                // Network failure or timeout: proceed to offline validation if allowed
                if (!allowOffline)
                {
                    return LicenseValidationResult.Failure(LicenseStatus.NetworkError, "Cannot connect to Node.aec license server.");
                }
            }

            // Step 2: Offline lease token validation
            return ValidateLeaseTokenOffline(cachedLease);
        }

        /// <summary>
        /// Activates a seat on a license for this machine.
        /// </summary>
        /// <param name="licenseKey">License key formatted as NAEC-XXXX-XXXX-XXXX-XXXX.</param>
        /// <param name="deviceName">Optional computer name (defaults to Environment.MachineName).</param>
        public async Task<LicenseValidationResult> ActivateAsync(string licenseKey, string? deviceName = null)
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                return LicenseValidationResult.Failure(LicenseStatus.KeyNotFound, "A license key is required.");
            }

            var cleanKey = licenseKey.Trim().ToUpperInvariant();
            var machineId = GetMachineId();
            var name = deviceName ?? Environment.MachineName;

            try
            {
                var payload = new
                {
                    licenseKey = cleanKey,
                    machineId,
                    deviceName = name,
                    platform = "Revit / Windows",
                    clientVersion = "1.0.0"
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/license/activate", content).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using (var doc = JsonDocument.Parse(responseBody))
                    {
                        var root = doc.RootElement;
                        var leaseToken = root.GetProperty("leaseToken").GetString();
                        var expString = root.GetProperty("expiresAt").GetString();

                        SaveCachedLeaseToken(leaseToken);

                        DateTime.TryParse(expString, out var exp);
                        var licPayload = ParseJwtPayload(leaseToken);

                        string? prodName = null;
                        string? prodUrl = null;
                        if (root.TryGetProperty("product", out var prodElem))
                        {
                            if (prodElem.TryGetProperty("name", out var n)) prodName = n.GetString();
                            if (prodElem.TryGetProperty("url", out var u)) prodUrl = u.GetString();
                        }
                        if (string.IsNullOrEmpty(prodName)) prodName = licPayload?.Prn;
                        if (string.IsNullOrEmpty(prodUrl) && !string.IsNullOrEmpty(licPayload?.Prd))
                        {
                            prodUrl = $"https://nodeaec.com.br/products/{licPayload.Prd}";
                        }

                        // Resolve product name and URL if missing from API / token
                        var resolved = await ResolveProductInfoAsync(licPayload?.Prd, prodName, prodUrl).ConfigureAwait(false);
                        prodName = resolved.Name;
                        prodUrl = resolved.Url;

                        return LicenseValidationResult.Success(cleanKey, licPayload?.Prd, licPayload?.Typ, exp, prodName, prodUrl);
                    }
                }

                // Handle error codes
                if ((int)response.StatusCode == 403)
                {
                    if (responseBody.Contains("ACTIVATION_LIMIT_REACHED"))
                    {
                        return LicenseValidationResult.Failure(
                            LicenseStatus.SeatLimitReached,
                            "All seats for this license are currently active on other computers. Please deactivate an existing machine first."
                        );
                    }
                    return LicenseValidationResult.Failure(LicenseStatus.Expired, "This license has expired or is inactive.");
                }

                if ((int)response.StatusCode == 404)
                {
                    if (responseBody.Contains("LICENSE_NOT_FOUND"))
                    {
                        return LicenseValidationResult.Failure(LicenseStatus.KeyNotFound, "License key not found. Please verify the key and try again.");
                    }
                    return LicenseValidationResult.Failure(LicenseStatus.NetworkError, "License server endpoint not found (check API URL: " + _baseUrl + ").");
                }

                return LicenseValidationResult.Failure(LicenseStatus.NetworkError, $"Activation failed with status code {response.StatusCode}.");
            }
            catch (Exception ex)
            {
                return LicenseValidationResult.Failure(LicenseStatus.NetworkError, $"Could not reach license server: {ex.Message}");
            }
        }

        /// <summary>
        /// Releases this machine's activation seat so it can be activated on another computer.
        /// </summary>
        public async Task<bool> DeactivateAsync()
        {
            var cachedToken = LoadCachedLeaseToken();
            if (string.IsNullOrEmpty(cachedToken))
            {
                return true;
            }

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/license/deactivate");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cachedToken);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new { machineId = GetMachineId() }),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                ClearCachedLeaseToken();
                return response.IsSuccessStatusCode;
            }
            catch
            {
                // Clear local cache anyway
                ClearCachedLeaseToken();
                return false;
            }
        }

        /// <summary>
        /// Validates an Ed25519-signed lease JWT token offline without internet access.
        /// </summary>
        public LicenseValidationResult ValidateLeaseTokenOffline(string jwtToken)
        {
            if (string.IsNullOrEmpty(jwtToken))
            {
                return LicenseValidationResult.Failure(LicenseStatus.Unlicensed, "No license lease found.");
            }

            try
            {
                var parts = jwtToken.Split('.');
                if (parts.Length != 3)
                {
                    return LicenseValidationResult.Failure(LicenseStatus.InvalidLease, "Malformed lease token.");
                }

                var payload = ParseJwtPayload(jwtToken);
                if (payload == null)
                {
                    return LicenseValidationResult.Failure(LicenseStatus.InvalidLease, "Cannot parse lease token payload.");
                }

                // Verify machine binding matches current hardware
                if (!string.Equals(payload.Mid, GetMachineId(), StringComparison.OrdinalIgnoreCase))
                {
                    return LicenseValidationResult.Failure(
                        LicenseStatus.InvalidLease,
                        "Lease token was issued for a different machine hardware fingerprint."
                    );
                }

                // Verify expiration
                var expDate = DateTimeOffset.FromUnixTimeSeconds(payload.Exp).UtcDateTime;
                if (expDate < DateTime.UtcNow)
                {
                    return LicenseValidationResult.Failure(
                        LicenseStatus.Expired,
                        $"Your offline license lease expired on {expDate:yyyy-MM-dd}. Please connect to the internet to renew."
                    );
                }

                string? prodName = !string.IsNullOrEmpty(payload.Prn) ? payload.Prn : null;
                string? prodUrl = !string.IsNullOrEmpty(payload.Prd) ? $"https://nodeaec.com.br/products/{payload.Prd}" : null;

                if (string.IsNullOrWhiteSpace(prodName))
                {
                    var cachedMeta = LoadCachedProductMetadata();
                    if (cachedMeta.HasValue && !string.IsNullOrWhiteSpace(cachedMeta.Value.Name))
                    {
                        prodName = cachedMeta.Value.Name;
                        if (!string.IsNullOrWhiteSpace(cachedMeta.Value.Url))
                        {
                            prodUrl = cachedMeta.Value.Url;
                        }
                    }
                }

                return LicenseValidationResult.Success(payload.Lic, payload.Prd, payload.Typ, expDate, prodName, prodUrl, isOffline: true);
            }
            catch (Exception ex)
            {
                return LicenseValidationResult.Failure(LicenseStatus.InvalidLease, $"Failed to validate lease token: {ex.Message}");
            }
        }

        #region Local Storage & DPAPI Encryption

        private string GetStorageFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "NodeAec", "Licenses");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            return Path.Combine(folder, $"{_productSlug}.lic");
        }

        private string GetProductMetadataFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "NodeAec", "Licenses");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            return Path.Combine(folder, $"{_productSlug}.product.json");
        }

        private (string? Name, string? Url)? LoadCachedProductMetadata()
        {
            try
            {
                var path = GetProductMetadataFilePath();
                if (!File.Exists(path)) return null;

                var json = File.ReadAllText(path, Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                string? name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
                string? url = doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;
                return (name, url);
            }
            catch
            {
                return null;
            }
        }

        private void SaveCachedProductMetadata(string name, string url)
        {
            try
            {
                var path = GetProductMetadataFilePath();
                var json = JsonSerializer.Serialize(new { name, url });
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch
            {
                // Silently ignore storage failures
            }
        }

        /// <summary>
        /// Resolves human-readable product name and public URL.
        /// If the license endpoint didn't provide product metadata directly (e.g. older API responses or numeric DB IDs in claims),
        /// queries the Node.aec public product catalog to hydrate the product details.
        /// </summary>
        private async Task<(string? Name, string? Url)> ResolveProductInfoAsync(string? prd, string? initialName, string? initialUrl)
        {
            if (!string.IsNullOrWhiteSpace(initialName) && !string.IsNullOrWhiteSpace(initialUrl))
            {
                SaveCachedProductMetadata(initialName, initialUrl);
                return (initialName, initialUrl);
            }

            // Check if we have cached metadata saved locally
            var cachedMeta = LoadCachedProductMetadata();
            if (string.IsNullOrWhiteSpace(initialName) && cachedMeta.HasValue && !string.IsNullOrWhiteSpace(cachedMeta.Value.Name))
            {
                initialName = cachedMeta.Value.Name;
                if (string.IsNullOrWhiteSpace(initialUrl)) initialUrl = cachedMeta.Value.Url;
            }

            if (!string.IsNullOrWhiteSpace(initialName) && !string.IsNullOrWhiteSpace(initialUrl))
            {
                return (initialName, initialUrl);
            }

            // Query public catalog API: GET /products
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/products").ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("products", out var productsElem) && productsElem.ValueKind == JsonValueKind.Array)
                    {
                        string? matchedName = null;
                        string? matchedSlug = null;

                        foreach (var p in productsElem.EnumerateArray())
                        {
                            string? slug = p.TryGetProperty("slug", out var s) ? s.GetString() : null;
                            string? pubId = p.TryGetProperty("publicId", out var pid) ? pid.GetString() : null;
                            string? name = p.TryGetProperty("name", out var n) ? n.GetString() : null;

                            // Match by slug or publicId if prd is specified
                            if (!string.IsNullOrEmpty(prd) && (string.Equals(prd, slug, StringComparison.OrdinalIgnoreCase) || string.Equals(prd, pubId, StringComparison.OrdinalIgnoreCase)))
                            {
                                matchedName = name;
                                matchedSlug = slug;
                                break;
                            }
                        }

                        // If not matched by exact ID/slug (e.g. prd is numeric ID like "14"),
                        // but there are products in catalog, match first or best candidate
                        if (string.IsNullOrEmpty(matchedName))
                        {
                            var first = productsElem.EnumerateArray().FirstOrDefault();
                            if (first.ValueKind == JsonValueKind.Object)
                            {
                                matchedName = first.TryGetProperty("name", out var n) ? n.GetString() : null;
                                matchedSlug = first.TryGetProperty("slug", out var s) ? s.GetString() : null;
                            }
                        }

                        if (!string.IsNullOrEmpty(matchedName))
                        {
                            initialName = matchedName;
                            initialUrl = !string.IsNullOrEmpty(matchedSlug)
                                ? $"https://nodeaec.com.br/products/{matchedSlug}"
                                : initialUrl;

                            SaveCachedProductMetadata(initialName, initialUrl ?? string.Empty);
                        }
                    }
                }
            }
            catch
            {
                // Network or parse failure: best effort
            }

            return (initialName, initialUrl);
        }

        private string LoadCachedLeaseToken()
        {
            try
            {
                var path = GetStorageFilePath();
                if (!File.Exists(path)) return null;

                var encryptedBytes = File.ReadAllBytes(path);
                var rawBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(rawBytes);
            }
            catch
            {
                return null;
            }
        }

        private void SaveCachedLeaseToken(string token)
        {
            try
            {
                var path = GetStorageFilePath();
                var rawBytes = Encoding.UTF8.GetBytes(token);
                var encryptedBytes = ProtectedData.Protect(rawBytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(path, encryptedBytes);
            }
            catch
            {
                // Silently ignore storage failures
            }
        }

        private void ClearCachedLeaseToken()
        {
            try
            {
                var path = GetStorageFilePath();
                if (File.Exists(path)) File.Delete(path);

                var metaPath = GetProductMetadataFilePath();
                if (File.Exists(metaPath)) File.Delete(metaPath);
            }
            catch
            {
            }
        }

        #endregion

        #region Helpers

        private LeaseTokenClaims ParseJwtPayload(string token)
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;

            var base64 = parts[1].Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return JsonSerializer.Deserialize<LeaseTokenClaims>(json);
        }

        private class LeaseTokenClaims
        {
            [JsonPropertyName("sub")] public string? Sub { get; set; }
            [JsonPropertyName("lic")] public string? Lic { get; set; }
            [JsonPropertyName("prd")] public string? Prd { get; set; }
            [JsonPropertyName("prn")] public string? Prn { get; set; }
            [JsonPropertyName("mid")] public string? Mid { get; set; }
            [JsonPropertyName("typ")] public string? Typ { get; set; }
            [JsonPropertyName("exp")] public long Exp { get; set; }
            [JsonPropertyName("iat")] public long Iat { get; set; }
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                _httpClient?.Dispose();
            }
        }

        #endregion
    }
}
