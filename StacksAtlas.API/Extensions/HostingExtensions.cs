using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;
using System.Text.Json;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace StacksAtlas.API.Extensions;

public static class HostingExtensions
{
    public static void ConfigureStacksAtlasHost(this IHostBuilder host)
    {
        if (PortableMode.IsEnabled)
        {
            return;
        }

        if (WindowsServiceHelpers.IsWindowsService())
        {
            host.UseContentRoot(AppContext.BaseDirectory);
        }

        if (OperatingSystem.IsWindows())
        {
            host.UseWindowsService();
        }
    }

    public static void ConfigureStacksAtlasKestrel(this WebHostBuilderContext context, Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions options)
    {
        // 1. Certificate Setup
        using var loggerFactory = LoggerFactory.Create(logging => { /* Silent */ });
        var certLogger = loggerFactory.CreateLogger<CertificateManager>();
        var certManager = new CertificateManager(certLogger);

        options.ConfigureHttpsDefaults(httpOptions =>
        {
            httpOptions.ServerCertificate = certManager.GetOrCreateCertificate();
        });

        // macOS Secure Transport mishandles TLS close_notify during HTTP/2 teardown
        // (dotnet/runtime#121272)  -  surfaces as Win32Exception(14) "Bad address" in logs.
        if (OperatingSystem.IsMacOS())
        {
            options.ConfigureEndpointDefaults(listenOptions => listenOptions.Protocols = HttpProtocols.Http1);
        }

        // 2. Port Selection
        var args = Environment.GetCommandLineArgs();
        if (args.Any(a => a.StartsWith("--urls", StringComparison.OrdinalIgnoreCase))) return;

        var systemSettingsPath = StacksAtlas.Core.Helpers.PlatformPaths.GetSystemSettingsPath();
        int httpPort = AppliancePortDefaults.DefaultHttpPortForPlatform();
        int httpsPort = AppliancePortDefaults.StandardHttpsPort;

        if (File.Exists(systemSettingsPath))
        {
            try {
                var json = File.ReadAllText(systemSettingsPath);
                var settings = JsonSerializer.Deserialize<SystemSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (settings != null) {
                    if (settings.HttpPort > 0) httpPort = AppliancePortDefaults.ResolveHttpPort(settings.HttpPort);
                    if (settings.HttpsPort > 0) httpsPort = settings.HttpsPort;
                }
            } catch { /* Fallback */ }
        }

        // Environment Overrides
        if (int.TryParse(Environment.GetEnvironmentVariable("STACKSATLAS_PORT"), out var p)) httpPort = p;
        if (int.TryParse(Environment.GetEnvironmentVariable("STACKSATLAS_HTTPS_PORT"), out var hp)) httpsPort = hp;

        if (PortableMode.IsEnabled)
        {
            if (PortableMode.AllowLanBind)
            {
                options.ListenAnyIP(httpPort);
                options.ListenAnyIP(httpsPort, listenOptions => listenOptions.UseHttps());
            }
            else
            {
                options.ListenLocalhost(httpPort);
                options.ListenLocalhost(httpsPort, listenOptions => listenOptions.UseHttps());
            }
            return;
        }

        options.ListenAnyIP(httpPort);
        options.ListenAnyIP(httpsPort, listenOptions => listenOptions.UseHttps());

        if (ExecutionState.IsHub)
        {
            int mtlsPort = 5002;
            if (int.TryParse(Environment.GetEnvironmentVariable("STACKSATLAS_MTLS_PORT"), out var mp)) mtlsPort = mp;

            options.ListenAnyIP(mtlsPort, listenOptions =>
            {
                listenOptions.UseHttps(httpsOptions =>
                {
                    // Use a CA-signed server certificate so Nodes can validate the server
                    // against the same Hub Root CA they received during enrollment.
                    // Eagerly generate/load the CA-signed server certificate at startup
                    // to avoid ServerCertificateSelector mTLS RequireCertificate issues.
                    try
                    {
                        var cert = HubCertificateAuthority.GetOrCreateServerCertificateStatic(null, (msg, ex) =>
                        {
                            if (ex != null) Log.Error(ex, "Kestrel Sovereign CA: {Message}", msg);
                            else Log.Information("Kestrel Sovereign CA: {Message}", msg);
                        });
                        httpsOptions.ServerCertificate = cert;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to load/generate CA-signed server certificate. Falling back to self-signed cert.");
                        httpsOptions.ServerCertificate = certManager.GetOrCreateCertificate();
                    }
                    httpsOptions.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.RequireCertificate;
                    httpsOptions.ClientCertificateValidation = (certificate, chain, errors) =>
                    {
                        Log.Debug("mTLS Handshake: ClientCertificateValidation invoked. SslPolicyErrors: {Errors}", errors);
                        if (certificate == null)
                        {
                            Log.Warning("mTLS Handshake: Rejecting connection because client certificate is null.");
                            return false;
                        }
                        if (chain == null)
                        {
                            Log.Warning("mTLS Handshake: Rejecting connection because certificate chain is null.");
                            return false;
                        }

                        Log.Information("mTLS Handshake: Client Certificate Subject: {Subject}, Issuer: {Issuer}, Serial: {Serial}",
                            certificate.Subject, certificate.Issuer, certificate.SerialNumber);

                        try
                        {
                            var rootCa = HubCertificateAuthority.GetOrCreateRootCaStatic((msg, ex) =>
                            {
                                if (ex != null) Log.Error(ex, "Kestrel Sovereign CA (Val): {Message}", msg);
                                else Log.Information("Kestrel Sovereign CA (Val): {Message}", msg);
                            });

                            chain.ChainPolicy.ExtraStore.Add(rootCa);
                            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                            chain.ChainPolicy.CustomTrustStore.Add(rootCa);
                            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

                            bool buildResult = chain.Build(certificate);
                            Log.Information("mTLS Handshake: Chain build result: {Result}", buildResult);

                            if (!buildResult)
                            {
                                foreach (var element in chain.ChainElements)
                                {
                                    foreach (var status in element.ChainElementStatus)
                                    {
                                        Log.Warning("mTLS Handshake: Chain Element Error: {Status} - {Info}", status.Status, status.StatusInformation);
                                    }
                                }
                                return false;
                            }

                            bool isRevoked = HubCertificateAuthority.IsRevokedStatic(certificate.SerialNumber);
                            Log.Information("mTLS Handshake: Is certificate revoked? {IsRevoked}", isRevoked);

                            return !isRevoked;
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "mTLS Handshake: Exception occurred during client certificate validation.");
                            return false;
                        }
                    };
                });
            });
        }
    }

    public static void OptimizeThreadPool(int minThreads = 512)
    {
        ThreadPool.GetMinThreads(out _, out int completionPortThreads);
        ThreadPool.SetMinThreads(minThreads, completionPortThreads);
        Log.Information("ThreadPool Optimized: MinThreads set to {Min}", minThreads);
    }

    public static string? ResolveWebRoot()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
            Path.Combine(baseDir, "wwwroot"),
            // macOS .app bundle: wwwroot lives under Contents/Resources (see installer/mac build scripts)
            Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", "wwwroot"))
        };

        foreach (var root in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(root, "index.html")))
                return root;
        }

        return null;
    }
}
