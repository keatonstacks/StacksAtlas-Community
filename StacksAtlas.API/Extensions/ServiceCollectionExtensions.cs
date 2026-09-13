using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;
using StacksAtlas.Core.Services.Reports;
using StacksAtlas.Core.Services.Network;
using StacksAtlas.Core.Services.Auth;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.Core.Services.Licensing;
using StacksAtlas.Core.Services.Integrations;
using StacksAtlas.Core.Services.Governance;
using StacksAtlas.Core.Services.Federation;
using StacksAtlas.Core.Services.Updates;
using StacksAtlas.Core.Services.Audit;
using StacksAtlas.API.Workers;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Services.Notifications;
using StacksAtlas.API.Services;
using LiteDB;
using StacksAtlas.Core.Data.Hub;
using Microsoft.EntityFrameworkCore;

namespace StacksAtlas.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStacksAtlasCore(this IServiceCollection services, IConfiguration configuration, string databasePath)
    {
        // --- Infrastructure & Base ---
        services.AddHttpContextAccessor();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<StacksAtlas.Core.Services.Logging.FederatedLogBuffer>();
        services.AddHttpClient();
        services.AddHttpClient(nameof(UpdateCheckService), client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("StacksAtlas-UpdateCheck/1.0");
        });
        services.AddHttpClient(nameof(OpenAvcService), client =>
        {
            // Macros await full execution (delays, multi-device steps) before HTTP 200.
            client.Timeout = TimeSpan.FromMinutes(2);
        });
        services.AddHttpClient("Webhooks", client => 
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        })
        .ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var bindingService = sp.GetService<INetworkBindingService>();
            return bindingService != null 
                ? bindingService.CreateHandlerForRole(NetworkRole.AlertsAndSiem) 
                : new System.Net.Http.SocketsHttpHandler();
        });
        services.AddSingleton<DatabaseSecurityService>();
        services.AddSingleton<SettingsEncryptor>();
        
        // --- Settings Layer ---
        services.AddStacksAtlasSettings(configuration);

        // --- Database & Repositories ---
        services.AddStacksAtlasDatabase(configuration, databasePath);
        if (ExecutionState.IsHubBrainEnabled)
        {
            services.AddSingleton<DatabaseInfrastructureMigrationService>();
        }

        // --- Identity & Intelligence ---
        services.AddSingleton<IntelligenceEngine>();
        services.AddSingleton<RecogMatchingService>();
        services.AddSingleton<DeviceReconciliationService>();
        services.AddSingleton<DeviceTombstoneGate>();
        services.AddSingleton<SecurityAuditService>();

        // --- Licensing ---
        services.AddStacksAtlasLicensing();

        // --- System State & Reports ---
        services.AddSingleton<SystemStateProvider>();
        services.AddSingleton<ReportService>();

        // --- Release manifest / update check (9.2) ---
        services.AddHttpClient(nameof(UpdateApplyService), client =>
        {
            client.Timeout = TimeSpan.FromMinutes(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("StacksAtlas-UpdateApply/1.0");
        });
        services.AddSingleton<IApplianceVersionProvider, ApplianceVersionProvider>();
        services.AddSingleton<IReleaseManifestVerifier, ReleaseManifestVerifier>();
        services.AddSingleton<UpdateCheckService>();
        services.AddSingleton<HubDepotUpdateCheckService>();
        if (ExecutionState.IsHubBrainEnabled)
        {
            services.AddSingleton<IUpdateCheckService>(sp => sp.GetRequiredService<UpdateCheckService>());
        }
        else
        {
            services.AddSingleton<IUpdateCheckService, HubPreferringUpdateCheckService>();
        }
        services.AddSingleton<UpdateArtifactDownloader>();
        services.AddSingleton<IUpdateArtifactDownloader>(sp => sp.GetRequiredService<UpdateArtifactDownloader>());
        services.AddSingleton<WindowsUpdateApplyRunner>();
        services.AddSingleton<IUpdateApplyService, UpdateApplyService>();
        services.AddSingleton<IUpdateDepotService, UpdateDepotService>();

        // --- Shared Core Application Services ---
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IAuditSyslogForwarder, AuditSyslogForwarder>();
        services.AddSingleton<IAuditService, StacksAtlas.API.Services.AuditService>();
        services.AddSingleton<IAlertService, AlertService>();
        services.AddSingleton<LdapService>();
        services.AddSingleton<SsoDiagnosticService>();
        services.AddSingleton<ApiKeyService>();
        services.AddSingleton<CertificateManager>();
        services.AddSingleton<EmailService>();
        services.AddSingleton<WebhookService>();
        services.AddSingleton<IFederationIdentitySyncService, FederationIdentitySyncService>();
        services.AddSingleton<IFederationSettingsSyncService, FederationSettingsSyncService>();
        services.AddSingleton<IHubCertificateAuthority, HubCertificateAuthority>();
        services.AddSingleton<EnrollmentTokenManager>();
        services.AddSingleton<NodeEnrollmentClient>(sp => 
        {
            var logger = sp.GetRequiredService<ILogger<NodeEnrollmentClient>>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            var settingsStore = sp.GetRequiredService<FederationSettingsStore>();
            var hwidProvider = sp.GetRequiredService<IHardwareIdProvider>();
            return new NodeEnrollmentClient(logger, httpClient, settingsStore, hwidProvider);
        });
        services.AddSingleton<FederationNodePulseService>();

        // --- Role-Aware Service Resolution ---
        if (ExecutionState.IsHubBrainEnabled)
        {
            // Hub Services (Proxied to Nodes)
            services.AddSingleton<RemoteExecutionService>();
            services.AddSingleton<TracerouteService>();
            services.AddSingleton<INetworkInterfaceService, NoOpNetworkInterfaceService>();
            services.AddSingleton<INetworkService, NoOpNetworkService>();
            services.AddSingleton<INmapService, NoOpNmapService>();
            services.AddSingleton<IDeepScanService>(sp => sp.GetRequiredService<RemoteExecutionService>());
            services.AddSingleton<IHostPinger>(sp => sp.GetRequiredService<RemoteExecutionService>());
            services.AddSingleton<IWakeOnLanService>(sp => sp.GetRequiredService<RemoteExecutionService>());
            services.AddSingleton<ITracerouteService>(sp => sp.GetRequiredService<RemoteExecutionService>());
            services.AddSingleton<ScanProgressService>(); // Lightweight state holder
            services.AddSingleton<OpenAvcRelayService>();
        }
        else
        {
            // Node Services (Active Scanning)
            services.AddSingleton<IDeviceSyncService, DeviceSyncService>();
            services.AddSingleton<INetworkDiscoveryService, NetworkDiscoveryService>();
            services.AddSingleton<INetworkInterfaceService, NetworkInterfaceService>();
            services.AddSingleton<INetworkBindingService, NetworkBindingService>();
            services.AddSingleton<FederationTransportHelper>();
            services.AddSingleton<INetworkService, NetworkService>();
            services.AddSingleton<IHostPinger, HostPinger>();
            services.AddSingleton<ISsdpDiscoveryService, SsdpDiscoveryService>();
            services.AddSingleton<SubnetScanner>();
            services.AddSingleton<ISweepMetricsCollector, SweepMetricsCollector>();
            services.AddSingleton<ScanProgressService>();
            services.AddSingleton<INmapService, NmapService>();
            services.AddSingleton<IDeepScanService, DeepScanService>();
            services.AddHostedService<DeepScanService>(sp => (DeepScanService)sp.GetRequiredService<IDeepScanService>());
            services.AddSingleton<HttpTitleGrabber>();
            services.AddSingleton<ISnmpService, SnmpService>();
            services.AddSingleton<IWakeOnLanService, WakeOnLanService>();
            services.AddSingleton<ITracerouteService, TracerouteService>();
        }

        services.AddSingleton<ISysctlReader, LinuxSysctlReader>();
        services.AddSingleton<INetworkRoutingSettingsProvider, NetworkRoutingSettingsProvider>();
        services.AddSingleton<INetworkRoutingDiagnosticsService, NetworkRoutingDiagnosticsService>();
        services.AddSingleton<IFederationBacklogService, FederationBacklogService>();
        services.AddSingleton<FederationTelemetryWakeSignal>();
        services.AddSingleton<ITailscaleCliRunner, TailscaleCliRunner>();
        services.AddSingleton<ITailscaleStatusService, TailscaleStatusService>();
        services.AddSingleton<HubEndpointResolver>();
        services.AddSingleton<ITailscaleReachabilityService, TailscaleReachabilityService>();

        return services;
    }

    private static IServiceCollection AddStacksAtlasSettings(this IServiceCollection services, IConfiguration configuration)
    {
        var baseDataDir = StacksAtlas.Core.Helpers.PlatformPaths.BaseDataDir;

        services.AddSingleton<SystemSettingsStore>();
        services.AddSingleton<NetworkSettingsStore>();
        services.AddSingleton<FederationSettingsStore>();
        services.AddSingleton<CleanupSettingsStore>();
        
        // Polling Settings Store
        services.AddSingleton<PollingSettingsStore>(sp =>
        {
            var store = new PollingSettingsStore(Path.Combine(baseDataDir, "pollingsettings.json"));
            store.Load();
            return store;
        });

        // Legacy bridge for appsettings.json
        services.Configure<CleanupOptions>(configuration.GetSection("StacksAtlas:Cleanup"));
        services.Configure<PollingOptions>(configuration.GetSection("StacksAtlas:Polling"));
        services.Configure<UpdateSettings>(configuration.GetSection(UpdateSettings.SectionName));

        return services;
    }

    private static IServiceCollection AddStacksAtlasDatabase(this IServiceCollection services, IConfiguration configuration, string databasePath)
    {
        services.AddSingleton<ILiteDatabase>(sp => 
        {
            var dbSecurity = sp.GetRequiredService<DatabaseSecurityService>();
            var dbPassword = dbSecurity.GetDatabasePassword();
            
            return new LiteDatabase(new ConnectionString(databasePath) 
            { 
                Password = dbPassword,
                Connection = ConnectionType.Shared
            });
        });

        // Interface registration for flexibility
        services.AddSingleton<LiteDatabase>(sp => (LiteDatabase)sp.GetRequiredService<ILiteDatabase>());
        services.AddSingleton<string>(databasePath);

        services.AddSingleton<SweepHistoryRepository>();
        services.AddSingleton<LeaseHistoryRepository>();
        services.AddSingleton<EmailSettingsRepository>();
        services.AddSingleton<OpenAvcSettingsRepository>();
        services.AddSingleton<OpenAvcDeviceLinkRepository>();
        services.AddSingleton<DeviceAssetEnrichmentService>();
        services.AddSingleton<Func<DeviceAssetEnrichmentService>>(sp => () => sp.GetRequiredService<DeviceAssetEnrichmentService>());
        services.AddSingleton<IDeviceAssetImportService, DeviceAssetImportService>();
        services.AddSingleton<OpenAvcService>();
        services.AddSingleton<WebhookRepository>();

        // Snapshot & Migration
        services.AddSingleton<ISnapshotService, SnapshotService>();
        services.AddSingleton<IDatabaseMigrationService, StacksAtlas.Core.Services.Scanning.DatabaseMigrationService>();
        
        if (ExecutionState.IsHubBrainEnabled)
        {
            services.AddDbContextFactory<HubDbContext>((serviceProvider, options) =>
            {
                var settingsStore = serviceProvider.GetRequiredService<SystemSettingsStore>();
                var encryptor = serviceProvider.GetRequiredService<SettingsEncryptor>();
                
                var dbSettings = settingsStore.Current.Database;
                var provider = dbSettings?.Provider ?? "Sqlite";
                var connectionString = dbSettings?.ConnectionString;

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    // Fall back to configuration file (appsettings.json) if defined
                    provider = configuration["StacksAtlas:Database:Provider"] ?? provider;
                    connectionString = configuration["StacksAtlas:Database:ConnectionString"];
                }

                // If connection string is encrypted, decrypt it using the SettingsEncryptor
                if (!string.IsNullOrWhiteSpace(connectionString) && encryptor.IsEncrypted(connectionString))
                {
                    connectionString = encryptor.Unprotect(connectionString);
                }

                switch (provider.ToLowerInvariant())
                {
                    case "postgresql":
                    case "postgres":
                    case "npgsql":
                        if (string.IsNullOrWhiteSpace(connectionString))
                        {
                            throw new InvalidOperationException("PostgreSQL connection string is required when PostgreSQL provider is selected.");
                        }
                        options.UseNpgsql(connectionString, b => b.MigrationsAssembly("StacksAtlas.Core"));
                        break;

                    case "sqlserver":
                    case "mssql":
                        if (string.IsNullOrWhiteSpace(connectionString))
                        {
                            throw new InvalidOperationException("SQL Server connection string is required when SQL Server provider is selected.");
                        }
                        options.UseSqlServer(connectionString, b => b.MigrationsAssembly("StacksAtlas.Core"));
                        break;

                    case "sqlite":
                    default:
                        if (string.IsNullOrWhiteSpace(connectionString))
                        {
                            var hubDbPath = Path.Combine(Path.GetDirectoryName(databasePath)!, "StacksAtlas.Hub.db");
                            connectionString = $"Data Source={hubDbPath};Cache=Shared;Mode=ReadWriteCreate;Default Timeout=30;";
                        }
                        options.UseSqlite(connectionString, b => b.MigrationsAssembly("StacksAtlas.Core"));
                        break;
                }
            });

            services.AddSingleton<FederatedIngestionBuffer>();
            services.AddSingleton<IDeviceSuppressionRegistry, SqliteDeviceSuppressionRegistry>();
            services.AddSingleton<IDeviceRepository, SqliteDeviceRepository>();
            services.AddSingleton<IFederatedNodeRepository, SqliteFederatedNodeRepository>();
            services.AddSingleton<IEventRepository, StacksAtlas.Core.Data.Hub.SqliteEventRepository>();
            services.AddSingleton<IAlertEventRepository, StacksAtlas.Core.Database.SqliteAlertEventRepository>();
            services.AddSingleton<IAuditEventRepository, SqliteAuditEventRepository>();
            services.AddScoped<IDatabaseMaintenance, HubDatabaseMaintenance>();
            services.AddSingleton<INodeFleetTelemetryPurgeService>(sp =>
                new NodeFleetTelemetryPurgeService(
                    sp.GetRequiredService<IDbContextFactory<StacksAtlas.Core.Data.Hub.HubDbContext>>(),
                    sp.GetRequiredService<LiteDatabase>()));
            services.AddSingleton<INodeIdentityReconciliationService>(sp =>
                new NodeIdentityReconciliationService(
                    sp.GetRequiredService<IFederatedNodeRepository>(),
                    sp.GetRequiredService<IDbContextFactory<StacksAtlas.Core.Data.Hub.HubDbContext>>(),
                    sp.GetRequiredService<LiteDatabase>(),
                    sp.GetRequiredService<IAlertEventRepository>(),
                    sp.GetRequiredService<IClock>(),
                    sp.GetRequiredService<ILogger<NodeIdentityReconciliationService>>()));
        }
        else
        {
            services.AddSingleton<IDeviceSuppressionRegistry, LiteDbDeviceSuppressionRegistry>();
            services.AddSingleton<IDeviceRepository, DeviceRepository>();
            services.AddSingleton<IFederatedNodeRepository, NoOpFederatedNodeRepository>();
            services.AddSingleton<IEventRepository, EventRepository>();
            services.AddSingleton<IAlertEventRepository, AlertEventRepository>();
            services.AddSingleton<IAuditEventRepository, AuditEventRepository>();
            services.AddSingleton<INodeFleetTelemetryPurgeService, NoOpNodeFleetTelemetryPurgeService>();
            services.AddSingleton<INodeIdentityReconciliationService, NoOpNodeIdentityReconciliationService>();
            
            services.AddScoped<IDatabaseMaintenance>(sp =>
            {
                var db = sp.GetRequiredService<LiteDatabase>();
                var settingsStore = sp.GetRequiredService<CleanupSettingsStore>();
                var logger = sp.GetRequiredService<ILogger<LiteDbDatabaseMaintenance>>();
                var securityService = sp.GetRequiredService<DatabaseSecurityService>();
                var clock = sp.GetRequiredService<IClock>();
                return new LiteDbDatabaseMaintenance(db, databasePath, settingsStore, logger, securityService, clock);
            });
        }

        return services;
    }

    private static IServiceCollection AddStacksAtlasLicensing(this IServiceCollection services)
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            services.AddSingleton<IHardwareIdProvider, WindowsHardwareIdProvider>();
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            services.AddSingleton<IHardwareIdProvider, MacHardwareIdProvider>();
        else
            services.AddSingleton<IHardwareIdProvider, LinuxHardwareIdProvider>();

        services.AddSingleton<ILicenseService, CommunityLicenseService>();
        return services;
    }

    public static IServiceCollection AddStacksAtlasWorkers(this IServiceCollection services)
    {
        services.AddHostedService<DatabaseStartupService>();
        
        if (ExecutionState.IsScanningEnabled)
        {
            services.AddHostedService<PingWorker>();
            
            // DeepScanService is a singleton background service
            services.AddSingleton<IDeepScanService, DeepScanService>();
            services.AddHostedService(sp => (DeepScanService)sp.GetRequiredService<IDeepScanService>());
        }

        if (ExecutionState.IsFederationActive)
        {
            // Federation Sync Pipeline
            services.AddSingleton<IFederatedLifecyclePushService, FederatedLifecyclePushService>();
            services.AddSingleton<FederationSyncWorker>();
            services.AddHostedService(sp => sp.GetRequiredService<FederationSyncWorker>());
            services.AddHostedService<DeviceStateWatcher>();
        }

        if (ExecutionState.IsHubBrainEnabled)
        {
            services.AddHostedService<FederatedIngestionWorker>();
            services.AddHostedService<FederationNodeLivenessWorker>();
        }
        else
        {
            services.AddHostedService<CleanupWorker>();
            services.AddHostedService<DeviceAssetEnrichmentWorker>();
        }

        // Appliance-local scheduled apply (Slice 5). No-ops on Hub brain / portable.
        services.AddHostedService<UpdateScheduleWorker>();

        return services;
    }
}
