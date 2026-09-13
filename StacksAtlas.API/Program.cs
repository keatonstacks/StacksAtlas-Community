using Serilog;
using Serilog.Core;
using StacksAtlas.API.Extensions;
using StacksAtlas.API.Filters;
using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Services.Auth;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.State;
using Microsoft.AspNetCore.DataProtection;

// --- v1.3.0 Federation Bootstrap ---
PortableMode.InitializeFromEnvironment();
if (PortableMode.IsEnabled)
    PlatformPaths.EnsureDirectoriesExist();

using var bootstrapLogger = LoggerFactory.Create(builder => builder.AddConsole());
var fedStore = new FederationSettingsStore(bootstrapLogger.CreateLogger<FederationSettingsStore>());
var fedSettings = fedStore.Current;

var mode = HubModeGuard.ResolveStartupMode(fedSettings.Mode);
var persistHubModeOnStart = mode == ExecutionMode.Hub && fedSettings.Mode != ExecutionMode.Hub;
string? hubUrl = fedSettings.HubUrl;
var tailnetFederation = fedSettings.UseTailscaleForHubConnection &&
    (!string.IsNullOrWhiteSpace(fedSettings.HubTailscaleMagicDns) ||
     !string.IsNullOrWhiteSpace(fedSettings.HubTailscaleIpv4));

// CLI Overrides take precedence
if (args.Any(a => a.Equals("--mode=hub", StringComparison.OrdinalIgnoreCase))) mode = ExecutionMode.Hub;
if (args.Any(a => a.Equals("--mode=standalone", StringComparison.OrdinalIgnoreCase) || a.Equals("--mode=node", StringComparison.OrdinalIgnoreCase))) mode = ExecutionMode.Standalone;

var hubArg = args.FirstOrDefault(a => a.StartsWith("--hub=", StringComparison.OrdinalIgnoreCase));
if (hubArg != null) hubUrl = hubArg.Split('=')[1];

ExecutionState.Initialize(mode, hubUrl, tailnetFederation);

Console.WriteLine("--------------------------------------------------");
        var appVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.3.2";
        var modeLabel = ExecutionState.IsPortable ? $"{mode.ToString().ToUpper()} (PORTABLE)" : mode.ToString().ToUpper();
        Console.WriteLine($" StacksAtlas v{appVersion} | Role: {modeLabel}");
if (ExecutionState.IsPortable)
{
    Console.WriteLine($" Data: {PlatformPaths.BaseDataDir}");
}
if (ExecutionState.IsFederationActive)
{
    Console.WriteLine($" Hub Director: {hubUrl ?? "(Tailscale transport)"}");
}
Console.WriteLine("--------------------------------------------------");
var builder = WebApplication.CreateBuilder(args);

// --- Infrastructure & Logging ---
var levelSwitch = new LoggingLevelSwitch();
builder.Services.AddSingleton(levelSwitch);

builder.Host.ConfigureStacksAtlasHost();
builder.WebHost.ConfigureKestrel(HostingExtensions.ConfigureStacksAtlasKestrel);
HostingExtensions.OptimizeThreadPool();

builder.Logging.ClearProviders();

// Manually load settings for early logging/syslog config
var initialSettings = new SystemSettings();
var settingsPath = PlatformPaths.GetSystemSettingsPath();
if (File.Exists(settingsPath))
{
    try 
    {
        var json = File.ReadAllText(settingsPath);
        initialSettings = System.Text.Json.JsonSerializer.Deserialize<SystemSettings>(json, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }) ?? new SystemSettings();
    }
    catch { /* fallback to defaults */ }
}

builder.Host.UseSerilog((ctx, services, config) =>
{
    var baseDataDir = PlatformPaths.BaseDataDir;
    var retentionDays = builder.Configuration.GetValue("Logging:RetentionDays", 30);
    var maxMB = builder.Configuration.GetValue("Logging:MaxFileSizeMB", 10);
    
    // Remote SIEM log streaming sink
    var buffer = services.GetRequiredService<StacksAtlas.Core.Services.Logging.FederatedLogBuffer>();
    config.WriteTo.Sink(new StacksAtlas.API.Services.Logging.FederatedLogSink(buffer, services));

    config.MinimumLevel.ControlledBy(levelSwitch)
        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Extensions.Http", Serilog.Events.LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            path: Path.Combine(baseDataDir, "logs", "log-.txt"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: retentionDays,
            fileSizeLimitBytes: maxMB * 1024 * 1024,
            rollOnFileSizeLimit: true,
            shared: true,
            buffered: false,
            flushToDiskInterval: System.TimeSpan.FromMilliseconds(500));

    if (initialSettings.SyslogEnabled && !string.IsNullOrWhiteSpace(initialSettings.SyslogHost))
    {
        config.WriteTo.Sink(new StacksAtlas.API.Services.Logging.BoundUdpSyslogSink(
            services,
            initialSettings.SyslogHost,
            initialSettings.SyslogPort,
            initialSettings.SyslogAppName));
    }
});

// --- Database Bootstrap ---
var databasePath = Path.Combine(PlatformPaths.BaseDataDir, "StacksAtlas.db");
DatabaseBootstrapper.PrepareDatabase(PlatformPaths.BaseDataDir, databasePath);

// --- Service Registrations ---
builder.Services.AddControllers(options => options.Filters.Add<ApiExceptionFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.Converters.Add(new StacksAtlas.Core.Json.LiteDbObjectIdJsonConverter());
        // Do NOT add JsonStringEnumConverter globally  -  it breaks UI checks that expect
        // numeric ExecutionMode (0/1) and LicenseTier (0/1/2). NetworkRole uses a type-level converter.
    });
builder.Services.AddStacksAtlasCore(builder.Configuration, databasePath);
builder.Services.AddStacksAtlasAuth(builder.Configuration);
builder.Services.AddStacksAtlasWorkers();
builder.Services.AddStacksAtlasSwagger();
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB
    // Federation relays (traceroute, deep scan status) can run longer than the 30s default.
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(2);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
}).AddJsonProtocol(options => FederationJson.Configure(options.PayloadSerializerOptions));

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new System.IO.DirectoryInfo(System.IO.Path.Combine(PlatformPaths.BaseDataDir, "keys")));

var app = builder.Build();

// --- Federation Encryption Handshake ---
var fedEncryptor = app.Services.GetRequiredService<StacksAtlas.Core.Services.Security.SettingsEncryptor>();
fedStore.Initialize(fedEncryptor);

// CLI break-glass decoupling escape hatch
if (args.Any(a => a.Equals("decouple", StringComparison.OrdinalIgnoreCase) || a.Equals("break-glass", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine("----------------------------------------------------------------------------------");
    Console.WriteLine("🚨 BREAK-GLASS EMERGENCY DECOUPLING ACTIVATED");
    Console.WriteLine("----------------------------------------------------------------------------------");
    
    try
    {
        var db = app.Services.GetRequiredService<LiteDB.LiteDatabase>();
        
        // 1. Revert settings
        var settings = fedStore.Current;
        settings.HubUrl = null;
        settings.FederationToken = null;
        settings.SyncUsers = false;
        settings.SyncUserRegistry = false;
        settings.SyncSsoSettings = false;
        fedStore.Save(settings);
        
        Console.WriteLine("✔ Federation Settings cleared (HubUrl and Token removed).");
        
        // 2. Promote federated/governed users to standard local users
        var userCol = db.GetCollection<User>("users");
        var users = userCol.FindAll().ToList();
        int promotedCount = 0;
        
        foreach (var u in users)
        {
            bool modified = false;
            if (u.OriginNodeId != null)
            {
                u.OriginNodeId = null;
                modified = true;
            }
            if (u.Provider != "Local")
            {
                u.Provider = "Local";
                modified = true;
            }
            
            if (modified)
            {
                userCol.Update(u);
                promotedCount++;
            }
        }
        
        Console.WriteLine($"✔ Promoted {promotedCount} federated/governed users to standard local accounts.");
        Console.WriteLine("----------------------------------------------------------------------------------");
        Console.WriteLine("🎉 Decoupling completed successfully. StacksAtlas is now in STANDALONE mode.");
        Console.WriteLine("----------------------------------------------------------------------------------");
        Environment.Exit(0);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Decoupling failed: {ex.Message}");
        Environment.Exit(1);
    }
}

// --- Apply Initial Logging Level ---
var settingsStore = app.Services.GetRequiredService<SystemSettingsStore>();
levelSwitch.MinimumLevel = settingsStore.Current.IsDebugLoggingEnabled 
    ? Serilog.Events.LogEventLevel.Debug 
    : Serilog.Events.LogEventLevel.Information;

// --- Request Pipeline ---
app.UseStacksAtlasStartupGate();
app.UsePortableLocalOnly();
app.UseApplianceSchemeRouting();
app.UseStacksAtlasSecurityHeaders();

app.UseStacksAtlasSwagger();

var webRoot = HostingExtensions.ResolveWebRoot();
app.UseStacksAtlasStaticFiles(webRoot);

app.UseRouting();
app.UseMiddleware<StacksAtlas.API.Middleware.AuthRateLimitMiddleware>();
app.UseAuthentication();
app.UseStacksAtlasSetupRedirect();
app.UseAuthorization();
app.UseCors();

app.MapControllers();
app.MapHub<StacksAtlas.API.Hubs.FederationHub>("/api/federation/realtime");

// --- Lifecycle & Boot ---
app.Lifetime.ApplicationStarted.Register(() => 
{
    if (persistHubModeOnStart)
    {
        try
        {
            PlatformPaths.EnsureDirectoriesExist();
            var federationStore = app.Services.GetRequiredService<FederationSettingsStore>();
            var current = federationStore.Current;
            current.Mode = ExecutionMode.Hub;
            federationStore.Save(current);
            Log.Information("Persisted Hub execution mode after Hub database detection.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist Hub mode correction after startup.");
        }
    }

    Log.Information("----------------------------------------------------------------------------------");
    var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    Log.Information("🚀 STACKSATLAS ENGINE IS ONLINE (v{Version})", version);
    Log.Information("   Environment:    {Env}", app.Environment.EnvironmentName);
    Log.Information("   OS:             {OS}", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
    Log.Information("   Process:        ID {Pid}, Memory {Mem} MB", 
        System.Diagnostics.Process.GetCurrentProcess().Id, 
        System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024);
    Log.Information("   Data Directory: {DataDir}", PlatformPaths.BaseDataDir);
    foreach (var url in app.Urls)
    {
        Log.Information("   ACCESS UI:      {Url}", url.Replace("[::]", "localhost").Replace("0.0.0.0", "localhost"));
    }
    Log.Information("----------------------------------------------------------------------------------");

    // If running in Hub mode, pulse all registered nodes to wake them from deep sleep
    if (ExecutionState.IsHubBrainEnabled)
    {
        Task.Run(async () =>
        {
            try
            {
                // Wait for the server to be fully stabilized
                await Task.Delay(3000);
                
                using var scope = app.Services.CreateScope();
                var nodeRepo = scope.ServiceProvider.GetRequiredService<StacksAtlas.Core.Data.IFederatedNodeRepository>();
                var pulseService = scope.ServiceProvider.GetRequiredService<StacksAtlas.API.Services.FederationNodePulseService>();
                var fedStore = scope.ServiceProvider.GetRequiredService<StacksAtlas.Core.Settings.FederationSettingsStore>();
                if (string.IsNullOrWhiteSpace(fedStore.Current.FederationToken))
                {
                    Log.Warning("HubStartup: Skipping node pulse  -  FederationToken is not configured.");
                    return;
                }

                var nodes = nodeRepo.GetAll();
                if (nodes.Count == 0) return;

                Log.Information("HubStartup: Pulsing {Count} enrolled node(s) to exit deep sleep...", nodes.Count);
                await pulseService.PulseAllNodesAsync(nodes);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "HubStartup: Failed to pulse registered nodes.");
            }
        });
    }
});

try
{
    app.Run();
}
catch (Exception ex) when (ex is System.IO.IOException || ex is System.Net.Sockets.SocketException)
{
    Log.Fatal("PORT CONFLICT: StacksAtlas could not start. Is the port already in use?");
    Environment.Exit(1);
}
catch (Exception ex)
{
    Log.Fatal(ex, "StacksAtlas terminated unexpectedly.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
