using LiteDB;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Services.Network;
using StacksAtlas.Core.Services.Licensing;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Services.Federation;
using StacksAtlas.Core.Services.Governance;
using StacksAtlas.Core.Services.Integrations;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Services.Auth;
using StacksAtlas.API.Services;
using StacksAtlas.Core.Services.Updates;

namespace StacksAtlas.API.Workers;

/// <summary>
/// A background worker exclusively active in Node mode.
/// Establishes and maintains a resilient SignalR connection to the designated Hub.
/// </summary>
public class FederationSyncWorker : BackgroundService
{
    private readonly ILogger<FederationSyncWorker> _logger;
    private readonly FederationSettingsStore _settings;
    private readonly SystemSettingsStore _systemSettings;
    private readonly NetworkSettingsStore _networkSettings;
    private readonly PollingSettingsStore _pollingSettings;
    private readonly LiteDatabase _db;
    private readonly SystemStateProvider _stateProvider;
    private readonly IHostPinger _pinger;
    private readonly IWakeOnLanService _wolService;
    private readonly IDeepScanService _deepScanService;
    private readonly IDeviceRepository _deviceRepo;
    private readonly ILicenseService _licenseService;
    private readonly StacksAtlas.Core.Services.Logging.FederatedLogBuffer _logBuffer;
    private readonly IServiceProvider _serviceProvider;
    private readonly INetworkBindingService _bindingService;
    private readonly NodeEnrollmentClient _enrollmentClient;
    private readonly IClock _clock;
    private readonly IHardwareIdProvider _hwidProvider;
    private readonly IFederationBacklogService _backlogService;
    private readonly FederationTelemetryWakeSignal _telemetryWake;
    private readonly HubEndpointResolver _endpointResolver;
    private readonly FederationTransportHelper _transportHelper;
    private readonly ITailscaleStatusService _tailscaleStatusService;
    private HubConnection? _connection;
    private HubEndpointPlan? _endpointPlan;
    private int _activeCandidateIndex;
    private readonly string? _directorUrl;
    private bool _isRegistered = false;
    private bool _isDeepSleep = false;
    private DateTime? _disconnectedSinceUtc = null;
    private DateTime? _lastDeepSleepWakeUtc = null;
    private CancellationTokenSource? _reconnectCts;
    private readonly SemaphoreSlim _connectionSemaphore = new SemaphoreSlim(1, 1);
    private int _decoupling;
    private CancellationToken _workerToken;
    private string? _lastReportedLicenseTier;

    public FederationSyncWorker(
        ILogger<FederationSyncWorker> logger, 
        FederationSettingsStore settings, 
        SystemSettingsStore systemSettings,
        NetworkSettingsStore networkSettings,
        PollingSettingsStore pollingSettings,
        LiteDatabase db, 
        SystemStateProvider stateProvider,
        IHostPinger pinger,
        IWakeOnLanService wolService,
        IDeepScanService deepScanService,
        IDeviceRepository deviceRepo,
        ILicenseService licenseService,
        StacksAtlas.Core.Services.Logging.FederatedLogBuffer logBuffer,
        IServiceProvider serviceProvider,
        INetworkBindingService bindingService,
        NodeEnrollmentClient enrollmentClient,
        IClock clock,
        IHardwareIdProvider hwidProvider,
        IFederationBacklogService backlogService,
        FederationTelemetryWakeSignal telemetryWake,
        HubEndpointResolver endpointResolver,
        FederationTransportHelper transportHelper,
        ITailscaleStatusService tailscaleStatusService)
    {
        _logger = logger;
        _settings = settings;
        _systemSettings = systemSettings;
        _networkSettings = networkSettings;
        _pollingSettings = pollingSettings;
        _db = db;
        _stateProvider = stateProvider;
        _pinger = pinger;
        _wolService = wolService;
        _deepScanService = deepScanService;
        _deviceRepo = deviceRepo;
        _licenseService = licenseService;
        _logBuffer = logBuffer;
        _serviceProvider = serviceProvider;
        _bindingService = bindingService;
        _enrollmentClient = enrollmentClient;
        _clock = clock;
        _hwidProvider = hwidProvider;
        _backlogService = backlogService;
        _telemetryWake = telemetryWake;
        _endpointResolver = endpointResolver;
        _transportHelper = transportHelper;
        _tailscaleStatusService = tailscaleStatusService;
        _directorUrl = ExecutionState.HubUrl;

        // --- Real-time Settings Sync ---
        _settings.OnSettingsChanged += async (s) => 
        {
            if (Volatile.Read(ref _decoupling) == 1)
                return;

            if (!HasHubConnectionTarget(s))
                return;

            if (_connection?.State == HubConnectionState.Connected)
            {
                _logger.LogInformation("FederationSyncWorker: Settings changed. Re-registering with Hub to sync metadata...");
                await PerformRegistrationAsync(CancellationToken.None);
            }
        };

        _networkSettings.OnSettingsChanged += ScheduleScanSettingsReport;
        _pollingSettings.OnSettingsChanged += ScheduleScanSettingsReport;

        _systemSettings.OnSettingsChanged += async () =>
        {
            if (Volatile.Read(ref _decoupling) == 1)
                return;

            if (!HasHubConnectionTarget(_settings.Current))
                return;

            if (_connection?.State == HubConnectionState.Connected)
            {
                _logger.LogInformation(
                    "FederationSyncWorker: System settings changed. Re-registering with Hub to sync operational status...");
                await PerformRegistrationAsync(CancellationToken.None);
            }
        };

        _licenseService.OnLicenseStatusChanged += status =>
        {
            if (Volatile.Read(ref _decoupling) == 1)
                return;

            if (!HasHubConnectionTarget(_settings.Current))
                return;

            if (_connection?.State != HubConnectionState.Connected || !_isRegistered)
                return;

            var tier = status.Tier.ToString();
            if (string.Equals(tier, _lastReportedLicenseTier, StringComparison.OrdinalIgnoreCase))
                return;

            _logger.LogInformation(
                "FederationSyncWorker: License tier changed to {Tier}. Re-registering with Hub...",
                tier);
            _ = PerformRegistrationAsync(CancellationToken.None, tierRefreshOnly: true);
        };
    }

    private static bool HasHubConnectionTarget(FederationSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.HubUrl))
            return true;

        return settings.UseTailscaleForHubConnection &&
            (!string.IsNullOrWhiteSpace(settings.HubTailscaleMagicDns) ||
             !string.IsNullOrWhiteSpace(settings.HubTailscaleIpv4));
    }

    public async Task DecoupleFromHubAsync()
    {
        if (_connection != null && _connection.State == HubConnectionState.Connected)
        {
            try
            {
                _logger.LogInformation("SignalR: Sending decoupling notification to Hub...");
                await _connection.InvokeAsync("DecoupleNode");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR: Failed to send decoupling notification to Hub.");
            }
        }

        // Purge local certificates
        _enrollmentClient.PurgeCertificates();
    }
    
    public FederationBacklogStatus GetBacklogStatus()
    {
        var hubConnected = _connection?.State == HubConnectionState.Connected && _isRegistered;
        var status = _backlogService.GetStatus(hubConnected, _isDeepSleep);
        if (!hubConnected && !string.IsNullOrWhiteSpace(_connectionHint))
        {
            return status with { Summary = $"{status.Summary} {_connectionHint}" };
        }
        return status;
    }

    public void WakeUp()
    {
        _logger.LogInformation("FederationSyncWorker: Wake-up pulse received. Resetting backoff and attempting reconnect...");
        _isDeepSleep = false;
        _disconnectedSinceUtc = null;
        _lastDeepSleepWakeUtc = _clock.UtcNow;
        
        try
        {
            _reconnectCts?.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "FederationSyncWorker: Failed to cancel reconnect delay.");
        }
    }

    private bool _connectFailureSummaryLogged;
    private bool _localTailscaleOfflineLogged;
    private string? _connectionHint;
    private int _scanSettingsReportScheduled;

    private async Task<string> BuildScanSettingsJsonAsync()
    {
        List<NetworkInterfaceInfo> nodeInterfaces = [];
        bool nmapAvailable = false;
        bool pcapAvailable = false;
        string pcapLabel = "Npcap";
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var interfaceService = scope.ServiceProvider.GetService<INetworkInterfaceService>();
            if (interfaceService != null)
                nodeInterfaces = interfaceService.GetAllInterfaces(NetworkInterfaceScope.Policy);

            var nmapService = scope.ServiceProvider.GetService<INmapService>();
            if (nmapService != null)
            {
                nmapAvailable = nmapService.HasNmapBinary();
                pcapAvailable = nmapService.HasPcapDriver();
            }

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux) ||
                System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                pcapLabel = "LibPcap";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR: Failed to collect local interface/scan telemetry for Hub registration.");
        }

        var combinedSettings = new
        {
            Network = _networkSettings.Load(),
            Polling = _pollingSettings.Load(),
            Interfaces = nodeInterfaces,
            ScanEngine = new
            {
                NmapAvailable = nmapAvailable,
                PcapAvailable = pcapAvailable,
                PcapLabel = pcapLabel
            },
            Logging = new
            {
                IsDebugLoggingEnabled = _systemSettings.Current.IsDebugLoggingEnabled
            }
        };

        return System.Text.Json.JsonSerializer.Serialize(combinedSettings, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
    }

    private async Task ReportScanSettingsToHubAsync(CancellationToken token)
    {
        if (_connection?.State != HubConnectionState.Connected || !_isRegistered)
            return;

        try
        {
            var scanSettingsJson = await BuildScanSettingsJsonAsync();
            // Fire-and-forget telemetry: avoid InvokeAsync timeouts on flaky links.
            await _connection.SendAsync("ReportScanSettings", scanSettingsJson, token);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "FederationSyncWorker: Failed to report scan settings to Hub.");
        }
    }

    private void ScheduleScanSettingsReport()
    {
        if (Interlocked.CompareExchange(ref _scanSettingsReportScheduled, 1, 0) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500);
                await ReportScanSettingsToHubAsync(CancellationToken.None);
            }
            finally
            {
                Interlocked.Exchange(ref _scanSettingsReportScheduled, 0);
            }
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (ExecutionState.IsHub) return; // Never runs on Hub

        if (!HasHubConnectionTarget(_settings.Current))
        {
            _logger.LogInformation("FederationSyncWorker: No Director URL configured. Node operating in pure standalone mode.");
            return;
        }

        _workerToken = stoppingToken;
        _backlogService.EnsureMigrationComplete();

        while (!_stateProvider.IsDatabaseReady && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(500, stoppingToken);
        }

        if (stoppingToken.IsCancellationRequested)
            return;

        var (clientCert, hubRootCert) = _enrollmentClient.LoadCertificates();
        var useMtls = clientCert != null && hubRootCert != null;
        _endpointPlan = _endpointResolver.Resolve(useMtls);

        if (_endpointPlan.Candidates.Count == 0)
        {
            _logger.LogError("FederationSyncWorker: Unable to resolve any Hub endpoint candidates.");
            return;
        }

        _activeCandidateIndex = 0;
        var activeCandidate = _endpointPlan.Candidates[_activeCandidateIndex];
        var hubEndpoint = _endpointPlan.BuildUrl(activeCandidate);

        if (useMtls)
        {
            _logger.LogInformation(
                "FederationSyncWorker: mTLS certificates loaded. Syncing via secure mTLS channel on {Endpoint} ({Label})",
                hubEndpoint,
                activeCandidate.Label);
        }
        else if (_endpointPlan.UsesTailscaleTransport)
        {
            _logger.LogInformation(
                "FederationSyncWorker: Tailscale transport enabled. Initial endpoint {Endpoint} ({Label})",
                hubEndpoint,
                activeCandidate.Label);
        }

        _connection = BuildHubConnection(_endpointPlan, activeCandidate, clientCert, hubRootCert);

        // --- Hub-to-Node Command Handlers ---
        _connection.On<FederationCommand, FederationCommandResult>("ExecuteCommand", async (cmd) => 
        {
            // Polls / status reads are Debug; mutations stay Information so operators can audit actions in logs.
            if (IsRoutineFederationCommand(cmd.CommandType))
            {
                _logger.LogDebug(
                    "SignalR: Received command {CommandType} from Hub (Target: {TargetId})",
                    cmd.CommandType,
                    string.IsNullOrEmpty(cmd.TargetId) ? "-" : cmd.TargetId);
            }
            else
            {
                _logger.LogInformation(
                    "SignalR: Received command {CommandType} from Hub (Target: {TargetId})",
                    cmd.CommandType,
                    string.IsNullOrEmpty(cmd.TargetId) ? "-" : cmd.TargetId);
            }
            
            try 
            {
                switch (cmd.CommandType)
                {
                    case "Ping":
                        var ip = cmd.Parameters.GetValueOrDefault("Ip");
                        if (string.IsNullOrEmpty(ip)) return new FederationCommandResult { Success = false, Message = "Missing IP" };
                        var pingResult = await _pinger.PingAsync(ip, stoppingToken);
                        return new FederationCommandResult { Success = true, Data = pingResult };

                    case "WakeOnLan":
                        var mac = cmd.Parameters.GetValueOrDefault("Mac");
                        if (string.IsNullOrEmpty(mac)) return new FederationCommandResult { Success = false, Message = "Missing MAC" };
                        await _wolService.SendMagicPacketAsync(mac);
                        return new FederationCommandResult { Success = true, Message = "WOL Packet Sent" };

                    case "DeepScan":
                        if (!Guid.TryParse(cmd.TargetId, out var deviceId)) return new FederationCommandResult { Success = false, Message = "Invalid Device ID" };
                        _deepScanService.QueueScan(deviceId);
                        return new FederationCommandResult { Success = true, Message = "Scan Queued" };

                    case "GetDeepScanStatus":
                        if (!Guid.TryParse(cmd.TargetId, out var statusDeviceId)) return new FederationCommandResult { Success = false, Message = "Invalid Device ID" };
                        var (scanStatus, scanProgress) = _deepScanService.GetScanStatus(statusDeviceId);
                        return new FederationCommandResult { Success = true, Data = new { status = scanStatus, progress = scanProgress } };

                    case "GetDeepScanResults":
                        if (!Guid.TryParse(cmd.TargetId, out var resultsDeviceId)) return new FederationCommandResult { Success = false, Message = "Invalid Device ID" };
                        var serviceCol = _db.GetCollection<ServiceDetail>("service_details");
                        var serviceRows = serviceCol.Find(x => x.DeviceId == resultsDeviceId).OrderBy(x => x.Port).ToList();
                        return new FederationCommandResult { Success = true, Data = serviceRows };

                    case "GetLeaseHistoryByMac":
                    {
                        var leaseMac = cmd.Parameters.GetValueOrDefault("Mac");
                        if (string.IsNullOrWhiteSpace(leaseMac))
                            return new FederationCommandResult { Success = false, Message = "Missing MAC" };
                        var cleanLeaseMac = leaseMac.Replace(":", "").Replace("-", "").ToUpperInvariant();
                        var leaseRepoMac = _serviceProvider.GetRequiredService<LeaseHistoryRepository>();
                        var macHistory = leaseRepoMac.GetHistoryForMac(cleanLeaseMac);
                        return new FederationCommandResult { Success = true, Data = macHistory };
                    }

                    case "GetLeaseHistoryByIp":
                    {
                        var leaseIp = cmd.Parameters.GetValueOrDefault("Ip");
                        if (string.IsNullOrWhiteSpace(leaseIp))
                            return new FederationCommandResult { Success = false, Message = "Missing IP" };
                        var leaseRepoIp = _serviceProvider.GetRequiredService<LeaseHistoryRepository>();
                        var ipHistory = leaseRepoIp.GetHistoryForIp(leaseIp);
                        return new FederationCommandResult { Success = true, Data = ipHistory };
                    }

                    case "AcknowledgeSecurityRisk":
                    {
                        if (!Guid.TryParse(cmd.TargetId, out var riskDeviceId))
                            return new FederationCommandResult { Success = false, Message = "Invalid Device ID" };
                        var issue = cmd.Parameters.GetValueOrDefault("Issue");
                        if (string.IsNullOrWhiteSpace(issue))
                            return new FederationCommandResult { Success = false, Message = "Missing Issue" };
                        if (!StacksAtlas.Core.Services.Security.DeviceRiskAcknowledgement.TryAcknowledge(_deviceRepo, riskDeviceId, issue))
                            return new FederationCommandResult { Success = false, Message = "Device not found" };
                        return new FederationCommandResult { Success = true, Message = "Risk acknowledged on node" };
                    }

                    case "ResetMetrics":
                    {
                        if (!Guid.TryParse(cmd.TargetId, out var metricsDeviceId))
                            return new FederationCommandResult { Success = false, Message = "Invalid Device ID" };
                        if (!_deviceRepo.ResetMetrics(metricsDeviceId))
                            return new FederationCommandResult { Success = false, Message = "Device not found" };
                        return new FederationCommandResult { Success = true, Message = "Metrics reset on node" };
                    }

                    case "RunTraceroute":
                    {
                        var traceIp = cmd.Parameters.GetValueOrDefault("Ip") ?? cmd.Parameters.GetValueOrDefault("Target");
                        if (string.IsNullOrWhiteSpace(traceIp))
                            return new FederationCommandResult { Success = false, Message = "Missing IP" };
                        var maxHops = 30;
                        if (int.TryParse(cmd.Parameters.GetValueOrDefault("MaxHops"), out var mh))
                            maxHops = mh;
                        var timeout = 1000;
                        if (int.TryParse(cmd.Parameters.GetValueOrDefault("Timeout"), out var traceTimeout))
                            timeout = traceTimeout;
                        var traceroute = _serviceProvider.GetRequiredService<ITracerouteService>();
                        var hops = new List<TracerouteHop>();
                        using var traceCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        traceCts.CancelAfter(TimeSpan.FromSeconds(60));
                        try
                        {
                            await foreach (var hop in traceroute.RunTracerouteAsync(traceIp, maxHops, timeout)
                                               .WithCancellation(traceCts.Token))
                            {
                                hops.Add(hop);
                            }
                        }
                        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested && hops.Count == 0)
                        {
                            return new FederationCommandResult
                            {
                                Success = false,
                                Message = "Traceroute timed out on node (SignalR relay limit)."
                            };
                        }

                        return new FederationCommandResult { Success = true, Data = hops };
                    }

                    case "OpenAvcGetDrawerContext":
                    {
                        if (!Guid.TryParse(cmd.TargetId, out var oaDeviceId))
                            return new FederationCommandResult { Success = false, Message = "Invalid Device ID" };
                        var openAvc = _serviceProvider.GetRequiredService<OpenAvcService>();
                        var targetIp = cmd.Parameters.GetValueOrDefault("Ip");
                        var hostname = cmd.Parameters.GetValueOrDefault("Hostname");
                        var deviceMac = cmd.Parameters.GetValueOrDefault("Mac");
                        var drawerContext = await openAvc.GetDrawerContextAsync(
                            oaDeviceId, targetIp, hostname, deviceMac, stoppingToken);
                        return new FederationCommandResult { Success = true, Data = drawerContext };
                    }

                    case "OpenAvcDeviceCommand":
                    {
                        if (!Guid.TryParse(cmd.TargetId, out var oaCmdDeviceId))
                            return new FederationCommandResult { Success = false, Message = "Invalid Device ID" };
                        var command = cmd.Parameters.GetValueOrDefault("Command");
                        if (string.IsNullOrWhiteSpace(command))
                            return new FederationCommandResult { Success = false, Message = "Missing Command" };
                        Dictionary<string, object>? cmdParams = null;
                        var paramsJson = cmd.Parameters.GetValueOrDefault("ParamsJson");
                        if (!string.IsNullOrWhiteSpace(paramsJson))
                        {
                            cmdParams = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(paramsJson);
                        }
                        var openAvcCmd = _serviceProvider.GetRequiredService<OpenAvcService>();
                        var cmdDevice = _deviceRepo.GetById(oaCmdDeviceId);
                        var cmdMac = cmd.Parameters.GetValueOrDefault("Mac") ?? cmdDevice?.MacAddress;
                        var cmdIp = cmd.Parameters.GetValueOrDefault("Ip") ?? cmdDevice?.IpAddress;
                        var cmdResult = await openAvcCmd.SendDeviceCommandAsync(
                            oaCmdDeviceId, command, cmdParams, cmdMac, cmdIp, stoppingToken);
                        return new FederationCommandResult
                        {
                            Success = cmdResult.Success,
                            Message = cmdResult.Message,
                            Data = cmdResult.Result,
                        };
                    }

                    case "OpenAvcExecuteMacro":
                    {
                        if (!Guid.TryParse(cmd.TargetId, out var oaMacroDeviceId))
                            return new FederationCommandResult { Success = false, Message = "Invalid Device ID" };
                        var macroId = cmd.Parameters.GetValueOrDefault("MacroId");
                        if (string.IsNullOrWhiteSpace(macroId))
                            return new FederationCommandResult { Success = false, Message = "Missing MacroId" };
                        var openAvcMacro = _serviceProvider.GetRequiredService<OpenAvcService>();
                        var macroDevice = _deviceRepo.GetById(oaMacroDeviceId);
                        var macroMac = cmd.Parameters.GetValueOrDefault("Mac") ?? macroDevice?.MacAddress;
                        var macroIp = cmd.Parameters.GetValueOrDefault("Ip") ?? macroDevice?.IpAddress;
                        var macroResult = await openAvcMacro.ExecuteMacroAsync(
                            macroId, oaMacroDeviceId, macroMac, macroIp, stoppingToken);
                        return new FederationCommandResult
                        {
                            Success = macroResult.Success,
                            Message = macroResult.Message,
                            Data = macroResult.Result,
                        };
                    }

                    case "OpenAvcSaveMacroPins":
                    {
                        if (!Guid.TryParse(cmd.TargetId, out var pinDeviceId))
                            return new FederationCommandResult { Success = false, Message = "Invalid Device ID" };
                        var macroIdsJson = cmd.Parameters.GetValueOrDefault("MacroIdsJson");
                        List<string> macroIds = [];
                        if (!string.IsNullOrWhiteSpace(macroIdsJson))
                        {
                            macroIds = System.Text.Json.JsonSerializer.Deserialize<List<string>>(macroIdsJson) ?? [];
                        }
                        var deviceMac = cmd.Parameters.GetValueOrDefault("Mac");
                        var deviceIp = cmd.Parameters.GetValueOrDefault("Ip");
                        var pinService = _serviceProvider.GetRequiredService<OpenAvcService>();
                        var pinResult = pinService.SaveMacroPins(pinDeviceId, macroIds, deviceMac, deviceIp);
                        return new FederationCommandResult { Success = pinResult.Success, Message = pinResult.Message };
                    }

                    case "OpenAvcSaveLink":
                    {
                        if (!Guid.TryParse(cmd.TargetId, out var oaLinkDeviceId))
                            return new FederationCommandResult { Success = false, Message = "Invalid Device ID" };
                        var openAvcDeviceId = cmd.Parameters.GetValueOrDefault("OpenAvcDeviceId");
                        if (string.IsNullOrWhiteSpace(openAvcDeviceId))
                            return new FederationCommandResult { Success = false, Message = "Missing OpenAvcDeviceId" };
                        var linkedBy = cmd.Parameters.GetValueOrDefault("LinkedBy");
                        var deviceMac = cmd.Parameters.GetValueOrDefault("Mac");
                        var deviceIp = cmd.Parameters.GetValueOrDefault("Ip");
                        var openAvcLink = _serviceProvider.GetRequiredService<OpenAvcService>();
                        var savedLink = await openAvcLink.SaveLinkAsync(
                            oaLinkDeviceId,
                            openAvcDeviceId,
                            linkedBy,
                            deviceMac,
                            deviceIp,
                            stoppingToken);
                        return new FederationCommandResult { Success = true, Data = savedLink };
                    }

                    case "OpenAvcDeleteLink":
                    {
                        if (!Guid.TryParse(cmd.TargetId, out var oaUnlinkDeviceId))
                            return new FederationCommandResult { Success = false, Message = "Invalid Device ID" };
                        var openAvcUnlink = _serviceProvider.GetRequiredService<OpenAvcService>();
                        openAvcUnlink.RemoveLink(oaUnlinkDeviceId);
                        return new FederationCommandResult { Success = true, Message = "Link removed" };
                    }

                    case "OpenAvcGetAllLinks":
                    {
                        var openAvcLinks = _serviceProvider.GetRequiredService<OpenAvcService>();
                        return new FederationCommandResult { Success = true, Data = openAvcLinks.GetAllLinkSummaries() };
                    }

                    case "UpdateDevice":
                    {
                        // Hub and Node device GUIDs often diverge; resolve by Id, then MAC, then IP.
                        var dev = ResolveCommandTargetDevice(cmd);
                        if (dev == null)
                            return new FederationCommandResult { Success = false, Message = "Device not found locally" };

                        if (cmd.Parameters.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name))
                            dev.Name = name;
                        if (cmd.Parameters.TryGetValue("Location", out var location))
                            dev.Location = string.IsNullOrWhiteSpace(location) ? null : location;
                        if (cmd.Parameters.TryGetValue("Vendor", out var vendor) && vendor != null)
                            dev.Vendor = vendor;
                        if (cmd.Parameters.TryGetValue("Model", out var model) && model != null)
                            dev.Model = model;
                        if (cmd.Parameters.TryGetValue("Type", out var type) && !string.IsNullOrWhiteSpace(type))
                            dev.Type = type;

                        if (bool.TryParse(cmd.Parameters.GetValueOrDefault("IsModelManuallySet"), out var mSet))
                            dev.IsModelManuallySet = mSet;
                        if (bool.TryParse(cmd.Parameters.GetValueOrDefault("IsVendorManuallySet"), out var vSet))
                            dev.IsVendorManuallySet = vSet;
                        if (bool.TryParse(cmd.Parameters.GetValueOrDefault("IsTypeManuallySet"), out var tSet))
                            dev.IsTypeManuallySet = tSet;

                        if (cmd.Parameters.TryGetValue("Hostname", out var hostname))
                            dev.Hostname = NullIfEmpty(hostname);
                        if (bool.TryParse(cmd.Parameters.GetValueOrDefault("IsHostnameManuallySet"), out var hSet))
                            dev.IsHostnameManuallySet = hSet;

                        if (cmd.Parameters.TryGetValue("SerialNumber", out var serial))
                            dev.SerialNumber = NullIfEmpty(serial);
                        if (cmd.Parameters.TryGetValue("AssetTag", out var assetTag))
                            dev.AssetTag = NullIfEmpty(assetTag);
                        if (cmd.Parameters.TryGetValue("FirmwareVersion", out var firmware))
                            dev.FirmwareVersion = NullIfEmpty(firmware);
                        if (DateTime.TryParse(cmd.Parameters.GetValueOrDefault("WarrantyExpiresUtc"), out var warrantyUtc))
                            dev.WarrantyExpiresUtc = warrantyUtc;
                        else if (string.IsNullOrEmpty(cmd.Parameters.GetValueOrDefault("WarrantyExpiresUtc")))
                            dev.WarrantyExpiresUtc = null;

                        if (bool.TryParse(cmd.Parameters.GetValueOrDefault("IsSerialNumberManuallySet"), out var serialSet))
                            dev.IsSerialNumberManuallySet = serialSet;
                        if (bool.TryParse(cmd.Parameters.GetValueOrDefault("IsFirmwareVersionManuallySet"), out var fwSet))
                            dev.IsFirmwareVersionManuallySet = fwSet;
                        if (bool.TryParse(cmd.Parameters.GetValueOrDefault("IsAssetTagManuallySet"), out var tagSet))
                            dev.IsAssetTagManuallySet = tagSet;
                        if (bool.TryParse(cmd.Parameters.GetValueOrDefault("IsWarrantyExpiresManuallySet"), out var warrantySet))
                            dev.IsWarrantyExpiresManuallySet = warrantySet;
                        if (int.TryParse(cmd.Parameters.GetValueOrDefault("AssetMetadataSource"), out var metaSource))
                            dev.AssetMetadataSource = (AssetMetadataSource)metaSource;

                        if (Guid.TryParse(cmd.Parameters.GetValueOrDefault("ManagedByUserId"), out var mgrId))
                            dev.ManagedByUserId = mgrId;
                        else if (string.IsNullOrEmpty(cmd.Parameters.GetValueOrDefault("ManagedByUserId")))
                            dev.ManagedByUserId = null;

                        var mgrUsername = cmd.Parameters.GetValueOrDefault("ManagedByUsername");
                        if (mgrUsername != null)
                            dev.ManagedByUsername = string.IsNullOrWhiteSpace(mgrUsername) ? null : mgrUsername;

                        if (bool.TryParse(cmd.Parameters.GetValueOrDefault("AlertsEnabled"), out var alertsEnabled))
                            dev.AlertsEnabled = alertsEnabled;

                        if (cmd.Parameters.TryGetValue("AttachmentKind", out var attachKind))
                            dev.AttachmentKind = string.IsNullOrWhiteSpace(attachKind) ? "Unknown" : attachKind;
                        if (cmd.Parameters.TryGetValue("AttachmentPort", out var attachPort))
                            dev.AttachmentPort = string.IsNullOrWhiteSpace(attachPort) ? null : attachPort.Trim();

                        // Parent identity: MAC, then IP (Hub/Node GUIDs diverge). Never wipe a
                        // resolved parent when Hub sends a foreign GUID without MAC/IP.
                        if (cmd.Parameters.ContainsKey("AttachmentParentDeviceId")
                            || cmd.Parameters.ContainsKey("AttachmentParentMac")
                            || cmd.Parameters.ContainsKey("AttachmentParentIp"))
                        {
                            var parentIdRaw = cmd.Parameters.GetValueOrDefault("AttachmentParentDeviceId") ?? "";
                            var parentMacRaw = cmd.Parameters.GetValueOrDefault("AttachmentParentMac") ?? "";
                            var parentIpRaw = cmd.Parameters.GetValueOrDefault("AttachmentParentIp") ?? "";
                            var parentMac = DeviceMacNormalizer.Normalize(parentMacRaw);
                            var parentIp = string.IsNullOrWhiteSpace(parentIpRaw) ? null : parentIpRaw.Trim();

                            if (string.IsNullOrEmpty(parentIdRaw)
                                && string.IsNullOrEmpty(parentMacRaw)
                                && string.IsNullOrEmpty(parentIpRaw))
                            {
                                // Explicit clear from Hub.
                                dev.AttachmentParentDeviceId = null;
                                dev.AttachmentParentMac = null;
                                dev.AttachmentParentIp = null;
                            }
                            else
                            {
                                Device? parent = null;
                                if (!string.IsNullOrEmpty(parentMac))
                                    parent = _deviceRepo.GetByMac(parentMac);
                                if (parent == null && !string.IsNullOrEmpty(parentIp))
                                    parent = _deviceRepo.GetByIp(parentIp);
                                if (parent == null
                                    && Guid.TryParse(parentIdRaw, out var parentId))
                                {
                                    parent = _deviceRepo.GetById(parentId);
                                }

                                if (parent != null)
                                {
                                    dev.AttachmentParentDeviceId = parent.Id;
                                    var resolvedMac = DeviceMacNormalizer.Normalize(parent.MacAddress);
                                    dev.AttachmentParentMac = string.IsNullOrEmpty(resolvedMac) ? null : resolvedMac;
                                    dev.AttachmentParentIp = string.IsNullOrWhiteSpace(parent.IpAddress)
                                        ? parentIp
                                        : parent.IpAddress.Trim();
                                }
                                else
                                {
                                    // Keep durable keys for later remap; do not store foreign GUIDs.
                                    if (!string.IsNullOrEmpty(parentMac))
                                        dev.AttachmentParentMac = parentMac;
                                    if (!string.IsNullOrEmpty(parentIp))
                                        dev.AttachmentParentIp = parentIp;
                                }
                            }
                        }

                        if (bool.TryParse(cmd.Parameters.GetValueOrDefault("IsAttachmentManuallySet"), out var attachManual))
                            dev.IsAttachmentManuallySet = attachManual;
                        else
                            dev.IsAttachmentManuallySet = true;

                        _deviceRepo.UpsertDevice(dev, true);
                        _logger.LogInformation(
                            "SignalR: Successfully updated local device {DeviceId} (MAC {Mac}) from Hub sync",
                            dev.Id,
                            dev.MacAddress);
                        return new FederationCommandResult { Success = true, Message = "Local Update Complete" };
                    }

                    case "ArchiveDevice":
                        return ExecuteLocalDeviceLifecycle(cmd, (id) => _deviceRepo.Delete(id), "Device archived locally");

                    case "RestoreDevice":
                        return ExecuteLocalDeviceLifecycle(cmd, (id) => _deviceRepo.Restore(id), "Device restored locally");

                    case "RemoveFromFleet":
                        return ExecuteLocalDeviceLifecycle(
                            cmd,
                            (id) => _deviceRepo.RemoveFromFleet(id, cmd.Parameters.GetValueOrDefault("RemovedBy")),
                            "Device tombstoned locally");

                    case "RestoreFromFleet":
                        return ExecuteLocalDeviceLifecycle(
                            cmd,
                            (id) => _deviceRepo.RestoreFromFleet(id),
                            "Device restored to fleet locally");

                    case "ForceSync":
                        _backlogService.PrepareDeviceSnapshotOnReconnect();
                        _telemetryWake.Notify();
                        _logger.LogWarning("SignalR: Hub requested a FORCE SYNC. Scheduling full device snapshot push.");
                        _ = RetryFullSnapshotPushAsync(_workerToken);
                        return new FederationCommandResult { Success = true, Message = "Sync Reset Initiated" };

                    case "NotifyUpdateAvailable":
                        return ExecuteHubUpdateOffer(cmd, requestedApply: false);

                    case "TriggerUpdate":
                        return ExecuteHubUpdateOffer(cmd, requestedApply: true);

                    case "ConfigureScanning":
                        var scanSettingsJson = cmd.Parameters.GetValueOrDefault("ScanSettingsJson");
                        if (string.IsNullOrEmpty(scanSettingsJson))
                            return new FederationCommandResult { Success = false, Message = "Missing ScanSettingsJson parameter." };

                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(scanSettingsJson);
                            var root = doc.RootElement;

                            var jsonOptions = new System.Text.Json.JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true,
                                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                            };

                            if (root.TryGetProperty("Network", out var netProp) || root.TryGetProperty("network", out netProp))
                            {
                                var networkUpdate = System.Text.Json.JsonSerializer.Deserialize<NetworkSettings>(netProp.GetRawText(), jsonOptions);
                                if (networkUpdate != null)
                                {
                                    _networkSettings.ApplyHubGovernancePatch(networkUpdate);
                                    _logger.LogInformation(
                                        "SignalR: Applied Hub network governance ({SubnetCount} scopes, {AdapterCount} adapter policies).",
                                        networkUpdate.Subnets?.Count ?? 0,
                                        networkUpdate.InterfaceConfigs?.Count ?? 0);
                                }
                            }

                            if (root.TryGetProperty("Polling", out var pollProp) || root.TryGetProperty("polling", out pollProp))
                            {
                                var pollingUpdate = System.Text.Json.JsonSerializer.Deserialize<PollingOptions>(pollProp.GetRawText(), jsonOptions);
                                if (pollingUpdate != null)
                                {
                                    _pollingSettings.Save(pollingUpdate);
                                }
                            }

                            _logger.LogInformation("SignalR: Successfully parsed and applied scanning governance from Hub.");
                            return new FederationCommandResult { Success = true, Message = "Scanning configuration synchronized successfully." };
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "SignalR: Failed to parse or apply synchronized scanning settings from Hub.");
                            return new FederationCommandResult { Success = false, Message = $"Configuration sync failure: {ex.Message}" };
                        }

                    case "SyncUserGovernance":
                        try
                        {
                            var enabled = false;
                            if (cmd.Parameters != null && cmd.Parameters.TryGetValue("enabled", out var enabledStr))
                            {
                                enabled = enabledStr == "true";
                            }
                            
                            var localSettings = _settings.Current;
                            if (localSettings.SyncUsers != enabled || localSettings.SyncUserRegistry != enabled || localSettings.SyncSsoSettings != enabled)
                            {
                                localSettings.SyncUsers = enabled;
                                localSettings.SyncUserRegistry = enabled;
                                localSettings.SyncSsoSettings = enabled;
                                _settings.Save(localSettings);
                                _logger.LogInformation("SignalR: Updated legacy user/SSO governance setting to {Enabled}", enabled);
                            }
                            return new FederationCommandResult { Success = true, Message = $"Governance toggled to {enabled}" };
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "SignalR: Failed to update local user governance.");
                            return new FederationCommandResult { Success = false, Message = ex.Message };
                        }

                    case "SyncUserRegistryGovernance":
                        try
                        {
                            var enabled = false;
                            if (cmd.Parameters != null && cmd.Parameters.TryGetValue("enabled", out var enabledStr))
                            {
                                enabled = enabledStr == "true";
                            }
                            
                            var localSettings = _settings.Current;
                            bool changed = false;
                            if (localSettings.SyncUserRegistry != enabled)
                            {
                                localSettings.SyncUserRegistry = enabled;
                                changed = true;
                            }
                            if (localSettings.SyncUsers)
                            {
                                localSettings.SyncUsers = false;
                                changed = true;
                            }
                            if (changed)
                            {
                                _settings.Save(localSettings);
                                _logger.LogInformation("SignalR: Updated local user registry governance setting to {Enabled} (Legacy SyncUsers disabled)", enabled);
                            }
                            return new FederationCommandResult { Success = true, Message = $"User registry governance toggled to {enabled}" };
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "SignalR: Failed to update local user registry governance.");
                            return new FederationCommandResult { Success = false, Message = ex.Message };
                        }

                    case "SyncSsoSettingsGovernance":
                        try
                        {
                            var enabled = false;
                            if (cmd.Parameters != null && cmd.Parameters.TryGetValue("enabled", out var enabledStr))
                            {
                                enabled = enabledStr == "true";
                            }
                            
                            var localSettings = _settings.Current;
                            bool changed = false;
                            if (localSettings.SyncSsoSettings != enabled)
                            {
                                localSettings.SyncSsoSettings = enabled;
                                changed = true;
                            }
                            if (localSettings.SyncUsers)
                            {
                                localSettings.SyncUsers = false;
                                changed = true;
                            }
                            if (changed)
                            {
                                _settings.Save(localSettings);
                                _logger.LogInformation("SignalR: Updated local SSO settings governance setting to {Enabled} (Legacy SyncUsers disabled)", enabled);
                            }
                            return new FederationCommandResult { Success = true, Message = $"SSO settings governance toggled to {enabled}" };
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "SignalR: Failed to update local SSO settings governance.");
                            return new FederationCommandResult { Success = false, Message = ex.Message };
                        }

                    case "SyncAlertGovernance":
                        try
                        {
                            var enabled = false;
                            if (cmd.Parameters != null && cmd.Parameters.TryGetValue("enabled", out var enabledStr))
                            {
                                enabled = enabledStr == "true";
                            }
                            
                            var localSettings = _settings.Current;
                            if (localSettings.SyncAlertSettings != enabled)
                            {
                                localSettings.SyncAlertSettings = enabled;
                                _settings.Save(localSettings);
                                _logger.LogInformation("SignalR: Updated local alert settings governance setting to {Enabled}", enabled);
                            }
                            return new FederationCommandResult { Success = true, Message = $"Alert settings governance toggled to {enabled}" };
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "SignalR: Failed to update local alert settings governance.");
                            return new FederationCommandResult { Success = false, Message = ex.Message };
                        }

                    case "SyncSiemGovernance":
                        try
                        {
                            var enabled = false;
                            if (cmd.Parameters != null && cmd.Parameters.TryGetValue("enabled", out var enabledStr))
                            {
                                enabled = enabledStr == "true";
                            }
                            
                            var localSettings = _settings.Current;
                            if (localSettings.SyncSiemSettings != enabled)
                            {
                                localSettings.SyncSiemSettings = enabled;
                                _settings.Save(localSettings);
                                _logger.LogInformation("SignalR: Updated local SIEM settings governance setting to {Enabled}", enabled);
                            }
                            return new FederationCommandResult { Success = true, Message = $"SIEM settings governance toggled to {enabled}" };
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "SignalR: Failed to update local SIEM settings governance.");
                            return new FederationCommandResult { Success = false, Message = ex.Message };
                        }

                    case "RequestUserImport":
                        _logger.LogInformation("SignalR: Received RequestUserImport command from Hub. Uploading local accounts for migration...");
                        try
                        {
                            var userCol = _db.GetCollection<User>("users");
                            var localUsers = userCol.FindAll()
                                .Where(u => u.Username.ToLower() != "admin")
                                .ToList();

                            _logger.LogInformation("SignalR: Pushing {Count} local users to Hub...", localUsers.Count);
                            var importResult = await _connection.InvokeAsync<UserImportResult>("ImportLocalUsers", localUsers);
                            
                            if (importResult.Success)
                            {
                                _logger.LogInformation("SignalR: Successfully migrated local users. Imported: {Imported}, Collisions: {Collisions}", 
                                    importResult.ImportedCount, importResult.CollisionCount);
                                return new FederationCommandResult { Success = true, Message = importResult.Message };
                            }
                            else
                            {
                                _logger.LogWarning("SignalR: Local user migration rejected by Hub: {Message}", importResult.Message);
                                return new FederationCommandResult { Success = false, Message = importResult.Message };
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "SignalR: Failed to process local user migration.");
                            return new FederationCommandResult { Success = false, Message = $"Migration failure: {ex.Message}" };
                        }

                    case "UpdateInheritedLicense":
                        if (cmd.Parameters != null && 
                            cmd.Parameters.TryGetValue("PayloadJson", out var payloadJson) && 
                            cmd.Parameters.TryGetValue("Signature", out var signature))
                        {
                            try
                            {
                                var parsedPayload = System.Text.Json.JsonSerializer.Deserialize<InheritedLicensePayload>(payloadJson);
                                if (parsedPayload != null && Enum.TryParse<LicenseTier>(parsedPayload.LicenseTier, out var parsedTier))
                                {
                                    _licenseService.ApplyInheritedLicense(parsedTier, payloadJson, signature);
                                    _logger.LogInformation("SignalR: Successfully applied license update command from Hub. New Tier: {Tier}", parsedTier);
                                    return new FederationCommandResult { Success = true, Message = $"License updated to {parsedTier}" };
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "FederationSyncWorker: Failed to apply license update command.");
                                return new FederationCommandResult { Success = false, Message = ex.Message };
                            }
                        }
                        return new FederationCommandResult { Success = false, Message = "Missing license payload or signature parameters." };

                    case "TriggerSiteReset":
                        return await ExecuteTriggerSiteResetAsync(cmd);

                    case "Decouple":
                        _logger.LogWarning("SignalR: Hub requested this Node to decouple. Reverting to Standalone mode...");
                        await RevertToStandaloneAsync();
                        return new FederationCommandResult { Success = true, Message = "Decoupling initiated." };

                    case "ConfigureDebugLogging":
                    {
                        var debugEnabled = cmd.Parameters != null &&
                            cmd.Parameters.TryGetValue("enabled", out var enabledStr) &&
                            enabledStr == "true";
                        var sysSettings = _systemSettings.Load();
                        sysSettings.IsDebugLoggingEnabled = debugEnabled;
                        _systemSettings.Save(sysSettings);

                        var levelSwitch = _serviceProvider.GetService<Serilog.Core.LoggingLevelSwitch>();
                        if (levelSwitch != null)
                        {
                            levelSwitch.MinimumLevel = debugEnabled
                                ? Serilog.Events.LogEventLevel.Debug
                                : Serilog.Events.LogEventLevel.Information;
                        }

                        // Push updated logging telemetry so Hub UI stays in sync.
                        ScheduleScanSettingsReport();

                        _logger.LogInformation(
                            "SignalR: Hub {Action} debug logging on this Node.",
                            debugEnabled ? "enabled" : "disabled");
                        return new FederationCommandResult
                        {
                            Success = true,
                            Message = $"Debug logging {(debugEnabled ? "enabled" : "disabled")}."
                        };
                    }

                    default:
                        return new FederationCommandResult { Success = false, Message = $"Unknown command type: {cmd.CommandType}" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SignalR: Error executing remote command {CommandType}", cmd.CommandType);
                return new FederationCommandResult { Success = false, Message = ex.Message };
            }
        });

        _connection.On<FederatedIdentityPayload>("SyncIdentity", (payload) => 
        {
            try
            {
                if (payload == null)
                {
                    _logger.LogWarning("SignalR: Received null FederatedIdentityPayload from Hub.");
                    return;
                }

                var localSettings = _settings.Current;
                
                // 1. Sync local users (only if SyncUserRegistry is enabled)
                if (localSettings.SyncUserRegistry || localSettings.SyncUsers)
                {
                    var userCount = payload.Users?.Count ?? 0;
                    _logger.LogInformation("SignalR: Received user registry sync from Hub ({Count} users).", userCount);

                    var userCol = _db.GetCollection<User>("users");
                    userCol.DeleteAll();
                    
                    if (payload.Users != null && payload.Users.Count > 0)
                    {
                        _logger.LogInformation("SignalR: Writing {Count} synchronized users to local LiteDB...", payload.Users.Count);
                        userCol.Insert(payload.Users);
                    }
                    else
                    {
                        _logger.LogInformation("SignalR: No users in sync payload. Purged all local users successfully.");
                    }
                }
                else
                {
                    _logger.LogInformation("SignalR: User Registry Sync is disabled. Skipping users payload.");
                }
                
                // 2. Sync local AuthSettings (only if SyncSsoSettings is enabled)
                if (localSettings.SyncSsoSettings || localSettings.SyncUsers)
                {
                    if (payload.AuthSettings != null)
                    {
                        _logger.LogInformation("SignalR: Saving updated authentication settings locally... SsoEnabled: {SsoEnabled}", 
                            payload.AuthSettings.SsoEnabled);
                        var currentSettings = _systemSettings.Current;
                        currentSettings.Auth = payload.AuthSettings;
                        _systemSettings.Save(currentSettings);
                    }
                }
                else
                {
                    _logger.LogInformation("SignalR: Global SSO Settings Sync is disabled. Skipping SSO settings payload.");
                }
                
                _logger.LogInformation("SignalR: Successfully processed IAM/SSO state sync.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SignalR: Failed to synchronize local IAM/SSO state.");
            }
        });

        _connection.On<FederatedSettingsPayload>("SyncSettings", (payload) => 
        {
            try
            {
                if (payload == null)
                {
                    _logger.LogWarning("SignalR: Received null FederatedSettingsPayload from Hub.");
                    return;
                }

                var localSettings = _settings.Current;
                
                // 1. Sync Alerts Settings
                if (localSettings.SyncAlertSettings && !localSettings.OverrideAlertSettings)
                {
                    _logger.LogInformation("SignalR: Received alert settings sync from Hub.");

                    if (payload.EmailSettings != null)
                    {
                        var emailCol = _db.GetCollection<EmailSettings>("email_settings");
                        payload.EmailSettings.Id = 1;
                        payload.EmailSettings.LastUpdatedUtc = DateTime.UtcNow;
                        emailCol.Upsert(1, payload.EmailSettings);
                    }

                    if (payload.Webhooks != null)
                    {
                        var webhooksCol = _db.GetCollection<WebhookConfiguration>("webhooks");
                        webhooksCol.DeleteAll();
                        foreach(var wh in payload.Webhooks)
                        {
                            wh.LastUpdatedAt = DateTime.UtcNow;
                        }
                        webhooksCol.Insert(payload.Webhooks);
                    }
                }
                else
                {
                    _logger.LogInformation("SignalR: Alert Settings Sync is disabled. Skipping payload.");
                }
                
                // 2. Sync SIEM Settings
                if (localSettings.SyncSiemSettings && !localSettings.OverrideSiemSettings)
                {
                    _logger.LogInformation("SignalR: Saving updated SIEM settings locally...");
                    var currentSysSettings = _systemSettings.Current;
                    currentSysSettings.SyslogEnabled = payload.SyslogEnabled ?? false;
                    currentSysSettings.SyslogHost = payload.SyslogHost ?? "";
                    currentSysSettings.SyslogPort = payload.SyslogPort ?? 514;
                    currentSysSettings.SyslogAppName = payload.SyslogAppName ?? "StacksAtlas";
                    _systemSettings.Save(currentSysSettings);
                }
                else
                {
                    _logger.LogInformation("SignalR: SIEM Settings Sync is disabled. Skipping payload.");
                }
                
                _logger.LogInformation("SignalR: Successfully processed alerts and SIEM settings sync.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SignalR: Failed to synchronize local alert/SIEM settings.");
            }
        });

        _connection.Closed += async (error) =>
        {
            var message = error?.Message ?? "Unknown error";
            _logger.LogWarning("SignalR: Connection closed ({Message}). Attempting to restart...", message);
            _licenseService.ClearInheritedLicense();
            _isRegistered = false;
            _backlogService.MarkHubDisconnected();
            await Task.Delay(Random.Shared.Next(0, 5) * 1000, stoppingToken);
            await TryConnectAsync(stoppingToken);
        };

        _connection.Reconnecting += error =>
        {
            var message = error?.Message ?? "Unknown error";
            _logger.LogWarning("SignalR: Connection lost ({Message}). Reconnecting...", message);
            _licenseService.ClearInheritedLicense();
            _isRegistered = false;
            _backlogService.MarkHubDisconnected();
            return Task.CompletedTask;
        };

        _connection.Reconnected += async connectionId =>
        {
            _logger.LogInformation("SignalR: Successfully reconnected to Hub. New ConnectionId: {ConnectionId}", connectionId);
            _disconnectedSinceUtc = null;
            _isDeepSleep = false;
            _backlogService.MarkHubConnected();
            await PerformRegistrationAsync(stoppingToken);
        };

        _logger.LogInformation("FederationSyncWorker: Starting connection to Hub at {Url}", hubEndpoint);
        await TryConnectAsync(stoppingToken);

        // Telemetry Processing Loop (Background Task)
        _ = Task.Run(async () => await ProcessQueueAsync(stoppingToken), stoppingToken);

        // Remote Log Streaming Loop (Background Task)
        _ = Task.Run(async () => await StreamLogsAsync(stoppingToken), stoppingToken);

        // System Event Streaming Loop (Background Task)
        _ = Task.Run(async () => await SyncEventsAsync(stoppingToken), stoppingToken);

        // Admin audit event streaming loop (Background Task)
        _ = Task.Run(async () => await SyncAuditEventsAsync(stoppingToken), stoppingToken);

        // Alert Event Streaming/Delegation Loop (Background Task)
        _ = Task.Run(async () => await SyncAlertsAsync(stoppingToken), stoppingToken);

        // Keep the worker alive
        var lastScanSettingsReportUtc = DateTime.UtcNow;
        var lastNodePulseUtc = DateTime.UtcNow;
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            if (_connection?.State == HubConnectionState.Connected)
            {
                if (!_isRegistered)
                {
                    _logger.LogWarning("SignalR: Connected but not registered  -  retrying Hub registration.");
                    await PerformRegistrationAsync(stoppingToken);
                }
                else if (_clock.UtcNow - lastNodePulseUtc >= TimeSpan.FromMinutes(1))
                {
                    await PushNodePulseAsync(stoppingToken);
                    lastNodePulseUtc = _clock.UtcNow;
                }
            }

            if (_isRegistered &&
                _connection?.State == HubConnectionState.Connected &&
                DateTime.UtcNow - lastScanSettingsReportUtc >= TimeSpan.FromMinutes(5))
            {
                await ReportScanSettingsToHubAsync(stoppingToken);
                lastScanSettingsReportUtc = DateTime.UtcNow;
            }
            
            // Periodically check if we need to manually restart a dead connection
            if (_connection?.State == HubConnectionState.Disconnected)
            {
                if (_isDeepSleep)
                {
                    if (_lastDeepSleepWakeUtc == null ||
                        _clock.UtcNow - _lastDeepSleepWakeUtc.Value >= TimeSpan.FromMinutes(5))
                    {
                        _lastDeepSleepWakeUtc = _clock.UtcNow;
                        WakeUp();
                    }
                }
                else
                {
                    _logger.LogDebug("SignalR: Connection disconnected  -  scheduling reconnect attempt.");
                    await TryConnectAsync(stoppingToken);
                }
            }
        }
    }

    private async Task TryConnectAsync(CancellationToken token)
    {
        if (_connection == null) return;

        if (!await _connectionSemaphore.WaitAsync(0))
        {
            // Already connecting or reconnecting in another thread
            return;
        }

        try
        {
            var (clientCert, hubRootCert) = _enrollmentClient.LoadCertificates();
            var useMtls = clientCert != null && hubRootCert != null;
            _endpointPlan ??= _endpointResolver.Resolve(useMtls);

            if (_endpointPlan.Candidates.Count == 0)
                return;

            var activeCandidate = _endpointPlan.Candidates[_activeCandidateIndex];
            var hubEndpoint = _endpointPlan.BuildUrl(activeCandidate);

            int retryDelayMs = 10000; // Start with 10s retry delay

            while (!token.IsCancellationRequested && _connection.State == HubConnectionState.Disconnected)
            {
                if (_endpointPlan.UsesTailscaleTransport)
                {
                    var tailscale = await _tailscaleStatusService.GetStatusAsync(token);
                    var localTailnetReady = !string.IsNullOrWhiteSpace(tailscale.TailnetIpv4) &&
                        (tailscale.Connected || tailscale.DetectedViaInterface);

                    if (!localTailnetReady)
                    {
                        _connectionHint = "Local Tailscale is offline  -  Hub sync paused.";
                        if (!_localTailscaleOfflineLogged)
                        {
                            _localTailscaleOfflineLogged = true;
                            _logger.LogWarning(
                                "Federation: Local Tailscale is not connected. Hub sync is paused; local discovery continues. " +
                                "Start Tailscale on this Node or disable tailnet transport in Settings → Federation.");
                        }

                        try
                        {
                            await Task.Delay(TimeSpan.FromMinutes(1), token);
                        }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested)
                        {
                            // Wake-up pulse
                        }
                        continue;
                    }

                    if (_localTailscaleOfflineLogged)
                    {
                        _localTailscaleOfflineLogged = false;
                        _connectFailureSummaryLogged = false;
                        _logger.LogInformation("Federation: Local Tailscale is available again. Resuming Hub connection attempts.");
                    }
                }

                if (_isDeepSleep)
                {
                    _logger.LogInformation("SignalR: Node is in deep sleep mode. Retries paused.");
                    try
                    {
                        _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        await Task.Delay(TimeSpan.FromMinutes(5), _reconnectCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("SignalR: Deep sleep interrupted by wake-up pulse.");
                    }
                    finally
                    {
                        _reconnectCts?.Dispose();
                        _reconnectCts = null;
                    }

                    // Reset backoff and sleep state
                    _isDeepSleep = false;
                    _disconnectedSinceUtc = null;
                    retryDelayMs = 10000;
                    continue;
                }

                if (_disconnectedSinceUtc == null)
                {
                    _disconnectedSinceUtc = _clock.UtcNow;
                    _backlogService.MarkHubDisconnected();
                }
                else if (_clock.UtcNow - _disconnectedSinceUtc.Value > TimeSpan.FromMinutes(15))
                {
                    _isDeepSleep = true;
                    _connectionHint = _endpointPlan.UsesTailscaleTransport
                        ? "Hub unreachable over tailnet  -  deep sleep (local monitoring active)."
                        : "Hub unreachable  -  deep sleep (local monitoring active).";
                    _logger.LogWarning(
                        "Federation: Hub has been unreachable for 15+ minutes. Entering deep sleep to reduce log noise. " +
                        "Local scanning continues. Hub sync resumes when the Hub is reachable again (or after a Hub startup pulse).");
                    continue;
                }

                try
                {
                    using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    attemptTimeout.CancelAfter(TimeSpan.FromSeconds(30));

                    await _connection.StartAsync(attemptTimeout.Token);
                    _logger.LogInformation("SignalR: Successfully connected to Hub!");
                    _connectFailureSummaryLogged = false;
                    _connectionHint = null;

                    _disconnectedSinceUtc = null;
                    _isDeepSleep = false;
                    _backlogService.MarkHubConnected();
                    _backlogService.ClearDisconnectedState();

                    await PerformRegistrationAsync(token);
                    return; // Success
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    LogConnectFailure(hubEndpoint, activeCandidate.Label, retryDelayMs, "Connection attempt timed out after 30s");
                }
                catch (Exception ex)
                {
                    LogConnectFailure(hubEndpoint, activeCandidate.Label, retryDelayMs, ex.Message);
                }

                // Perform retry delay
                try
                {
                    _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    await Task.Delay(retryDelayMs, _reconnectCts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("SignalR: Reconnect delay interrupted by wake-up pulse.");
                }
                finally
                {
                    _reconnectCts?.Dispose();
                    _reconnectCts = null;
                }

                // Incremental backoff: 10s -> 30s -> 1m -> 2m -> 5m (cap)
                if (retryDelayMs < 30000)
                    retryDelayMs = 30000;
                else if (retryDelayMs < 60000)
                    retryDelayMs = 60000;
                else if (retryDelayMs < 120000)
                    retryDelayMs = 120000;
                else
                    retryDelayMs = 300000;
            }
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    private async Task PerformRegistrationAsync(CancellationToken token, bool tierRefreshOnly = false)
    {
        if (Volatile.Read(ref _decoupling) == 1)
            return;

        try
        {
            if (!HasHubConnectionTarget(_settings.Current))
                return;

            var nodeId = string.IsNullOrWhiteSpace(_settings.Current.NodeId) ? "node-unnamed" : _settings.Current.NodeId;
            var federationToken = string.IsNullOrWhiteSpace(_settings.Current.FederationToken) ? "empty-token" : _settings.Current.FederationToken;
            var license = await _licenseService.GetCurrentStatusAsync();
            var scanSettingsJson = await BuildScanSettingsJsonAsync();

            bool hasUsers;
            using (var scope = _serviceProvider.CreateScope())
            {
                var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
                OnboardingLegacyMigration.ApplyIfNeeded(_systemSettings, authService, _logger);
                hasUsers = authService.AnyUsers();
            }

            OnboardingSettingsReset.ReconcileIfNoUsers(_systemSettings, hasUsers, _logger, databaseReady: true);
            var onboarding = _systemSettings.Load().Onboarding;

            var localUsersCol = _db.GetCollection<User>("users");
            var localUsersList = localUsersCol.FindAll()
                .Where(u => u.Username.ToLower() != "admin")
                .ToList();

            long dbSize = 0;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var maintenance = scope.ServiceProvider.GetRequiredService<IDatabaseMaintenance>();
                dbSize = await maintenance.GetDatabaseSizeBytesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR: Failed to calculate local database size telemetry.");
            }

            var request = new FederationRegistrationRequest
            {
                NodeId = nodeId,
                FederationToken = federationToken,
                Version = typeof(FederationSyncWorker).Assembly.GetName().Version?.ToString(3) ?? "1.3.2",
                OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                LicenseTier = license.Tier.ToString(),
                IsPortable = ExecutionState.IsPortable,
                // Hierarchy Metadata
                Client = _settings.Current.Client,
                Building = _settings.Current.Building,
                Room = _settings.Current.Room,
                OverrideAlertSettings = _settings.Current.OverrideAlertSettings,
                OverrideSiemSettings = _settings.Current.OverrideSiemSettings,
                ScanSettingsJson = scanSettingsJson,
                LocalUsersToImport = localUsersList,
                DatabaseSize = dbSize,
                HardwareId = _hwidProvider.GetHardwareId(),
                HttpPort = _systemSettings.Current.HttpPort,
                HttpsPort = _systemSettings.Current.HttpsPort,
                RequiresOperatorSetup = FederatedNodeStatus.RequiresOperatorSetup(onboarding, hasUsers)
            };

            var response = await _connection!.InvokeAsync<FederationRegistrationResponse>("AuthenticateNode", request, token);
            
            if (response != null && response.Success)
            {
                _isRegistered = true;
                _lastReportedLicenseTier = request.LicenseTier;
                _logger.LogInformation("SignalR: Node {NodeId} registered successfully (tier {Tier}). SyncUsers: {SyncUsers}, SyncUserRegistry: {SyncUserRegistry}, SyncSsoSettings: {SyncSsoSettings}", 
                    nodeId, request.LicenseTier, response.SyncUsers, response.SyncUserRegistry, response.SyncSsoSettings);

                if (!string.IsNullOrEmpty(response.InheritedLicensePayloadJson) && !string.IsNullOrEmpty(response.InheritedLicenseSignature))
                {
                    try
                    {
                        var parsedPayload = System.Text.Json.JsonSerializer.Deserialize<InheritedLicensePayload>(response.InheritedLicensePayloadJson);
                        if (parsedPayload != null && Enum.TryParse<LicenseTier>(parsedPayload.LicenseTier, out var parsedTier))
                        {
                            _licenseService.ApplyInheritedLicense(parsedTier, response.InheritedLicensePayloadJson, response.InheritedLicenseSignature);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "FederationSyncWorker: Failed to parse or apply inherited license.");
                    }
                }

                var localSettings = _settings.Current;
                bool changed = false;
                if (localSettings.SyncUsers != response.SyncUsers)
                {
                    localSettings.SyncUsers = response.SyncUsers;
                    changed = true;
                }
                if (localSettings.SyncUserRegistry != response.SyncUserRegistry)
                {
                    localSettings.SyncUserRegistry = response.SyncUserRegistry;
                    changed = true;
                }
                if (localSettings.SyncSsoSettings != response.SyncSsoSettings)
                {
                    localSettings.SyncSsoSettings = response.SyncSsoSettings;
                    changed = true;
                }
                if (localSettings.SyncAlertSettings != response.SyncAlertSettings)
                {
                    localSettings.SyncAlertSettings = response.SyncAlertSettings;
                    changed = true;
                }
                if (localSettings.SyncSiemSettings != response.SyncSiemSettings)
                {
                    localSettings.SyncSiemSettings = response.SyncSiemSettings;
                    changed = true;
                }
                if (localSettings.DelegateAlertDispatch != response.DelegateAlertDispatch)
                {
                    localSettings.DelegateAlertDispatch = response.DelegateAlertDispatch;
                    changed = true;
                }

                // Authoritative location metadata sync
                if (localSettings.Client != response.Client)
                {
                    localSettings.Client = response.Client;
                    changed = true;
                }
                if (localSettings.Building != response.Building)
                {
                    localSettings.Building = response.Building;
                    changed = true;
                }
                if (localSettings.Room != response.Room)
                {
                    localSettings.Room = response.Room;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(response.NodeDisplayName) &&
                    localSettings.NodeDisplayName != response.NodeDisplayName)
                {
                    localSettings.NodeDisplayName = response.NodeDisplayName;
                    changed = true;
                }

                if (changed)
                {
                    _settings.Save(localSettings);
                }

                // Update local sync cursor if the Hub is further behind than we thought (or if we are fresh)
                if (response.LastKnownSyncUtc != DateTime.MinValue)
                {
                     _logger.LogInformation("SignalR: Hub reported last known sync for this node as {LastSync}", response.LastKnownSyncUtc);
                     
                     // We update the local state so the Watcher picks it up
                     var stateCol = _db.GetCollection<FederationState>("federation_state");
                     var state = stateCol.FindById("singleton") ?? new FederationState();
                     
                     // If Hub says it's never seen us, or is behind our local record, we might need to reset.
                     // For initial sync, if Hub says DateTime.MinValue, we want to send EVERYTHING.
                     state.LastSyncUtc = response.LastKnownSyncUtc;
                     stateCol.Upsert(state);
                }
                else
                {
                     _logger.LogWarning("SignalR: Hub has no previous sync record for this node. Requesting full state push...");
                     var stateCol = _db.GetCollection<FederationState>("federation_state");
                     var state = stateCol.FindById("singleton") ?? new FederationState();
                     state.LastSyncUtc = DateTime.MinValue;
                     stateCol.Upsert(state);
                }

                if (!tierRefreshOnly)
                    await HandlePostReconnectAsync(token);

                await PushNodePulseAsync(token);
            }
            else
            {
                _isRegistered = false;
                _logger.LogWarning("SignalR: Registration failed: {Message}", response?.Message ?? "Unknown error");
                if (response != null && response.Decouple)
                {
                    _logger.LogWarning("SignalR: Hub requested this Node to decouple. Reverting to Standalone mode...");
                    await RevertToStandaloneAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _isRegistered = false;
            _logger.LogError(ex, "SignalR: Failed to register Node with Hub.");
            throw; // Re-throw so the caller can handle retry if needed
        }
    }

    private async Task HandlePostReconnectAsync(CancellationToken token)
    {
        if (_connection?.State != HubConnectionState.Connected || !_isRegistered)
            return;

        if (_backlogService.ShouldCatchUpOnReconnect())
        {
            _backlogService.PrepareDeviceSnapshotOnReconnect();
            _logger.LogInformation("FederationSyncWorker: Prepared full device snapshot after Hub reconnect.");
            await BackfillAlertsAsync(token);
            await BackfillAnomaliesAsync(token);
            await PushFullDeviceSnapshotAsync(token);
            _telemetryWake.Notify();
        }

        _backlogService.ClearDisconnectedState();
    }

    private async Task PushNodePulseAsync(CancellationToken token)
    {
        if (_connection?.State != HubConnectionState.Connected || !_isRegistered)
            return;

        try
        {
            var requiresOperatorSetup = await ResolveRequiresOperatorSetupAsync();
            await _connection.InvokeAsync("PushNodePulse", requiresOperatorSetup, cancellationToken: token);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "FederationSyncWorker: Node pulse to Hub failed.");
        }
    }

    private async Task<bool> ResolveRequiresOperatorSetupAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        OnboardingLegacyMigration.ApplyIfNeeded(_systemSettings, authService, _logger);
        var hasUsers = authService.AnyUsers();
        OnboardingSettingsReset.ReconcileIfNoUsers(_systemSettings, hasUsers, _logger, databaseReady: true);
        var onboarding = _systemSettings.Load().Onboarding;
        return FederatedNodeStatus.RequiresOperatorSetup(onboarding, hasUsers);
    }

    private async Task RetryFullSnapshotPushAsync(CancellationToken token)
    {
        for (var attempt = 0; attempt < 12 && !token.IsCancellationRequested; attempt++)
        {
            if (_connection?.State == HubConnectionState.Connected && _isRegistered)
            {
                await PushFullDeviceSnapshotAsync(token);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), token);
        }

        _logger.LogWarning(
            "FederationSyncWorker: Force sync snapshot push did not complete within 60s (Hub not registered). " +
            "DeviceStateWatcher will retry when the backlog gate opens.");
    }

    private async Task PushFullDeviceSnapshotAsync(CancellationToken token)
    {
        if (_connection?.State != HubConnectionState.Connected || !_isRegistered)
            return;

        var devices = _deviceRepo
            .GetAll(includeDeleted: true, includePermanentlyRemoved: true)
            .ToList();

        if (devices.Count == 0)
            return;

        const int batchSize = 50;
        _logger.LogInformation(
            "FederationSyncWorker: Pushing full device snapshot ({Count} devices) to Hub.",
            devices.Count);

        try
        {
            for (var i = 0; i < devices.Count; i += batchSize)
            {
                if (token.IsCancellationRequested)
                    return;

                var batch = devices.Skip(i).Take(batchSize).ToList();
                await _connection.InvokeAsync("PushTelemetry", batch, cancellationToken: token);
            }

            var stateCol = _db.GetCollection<FederationState>("federation_state");
            var state = stateCol.FindById("singleton") ?? new FederationState();
            state.LastSyncUtc = devices.Max(d => d.LastModifiedUtc);
            stateCol.Upsert(state);
            _backlogService.MarkHubConnected();
            _logger.LogInformation("FederationSyncWorker: Full device snapshot push complete.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FederationSyncWorker: Full device snapshot push failed. DeviceStateWatcher will retry.");
        }
    }

    private async Task BackfillAlertsAsync(CancellationToken token)
    {
        if (_connection?.State != HubConnectionState.Connected || !_isRegistered)
            return;

        var alerts = _backlogService.GetAlertsForBackfill();
        if (alerts.Count == 0)
            return;

        const int batchSize = 100;
        for (var i = 0; i < alerts.Count; i += batchSize)
        {
            var batch = alerts.Skip(i).Take(batchSize).Select(alert => new PendingAlertDto
            {
                Id = alert.Id,
                Alert = alert,
                IsDelegation = false,
                IsHistoricalReplay = true,
                CreatedAtUtc = alert.TriggeredAt
            }).ToList();

            await _connection.InvokeAsync("PushAlerts", batch, cancellationToken: token);
        }

        _db.GetCollection<PendingAlert>("pending_alerts").DeleteAll();
        _logger.LogInformation("FederationSyncWorker: Backfilled {Count} alert records to Hub (historical replay, notifications suppressed).", alerts.Count);
    }

    private async Task BackfillAnomaliesAsync(CancellationToken token)
    {
        if (_connection?.State != HubConnectionState.Connected || !_isRegistered)
            return;

        var events = _backlogService.GetAnomaliesForBackfill();
        if (events.Count == 0)
            return;

        const int batchSize = 100;
        for (var i = 0; i < events.Count; i += batchSize)
        {
            var batch = events.Skip(i).Take(batchSize).ToList();
            await _connection.InvokeAsync("PushEvents", batch, cancellationToken: token);
        }

        _db.GetCollection<PendingEvent>("pending_events").DeleteAll();
        _db.GetCollection<PendingAuditEvent>("pending_audit_events").DeleteAll();
        _logger.LogInformation("FederationSyncWorker: Backfilled {Count} warning/critical anomaly records to Hub.", events.Count);
    }

    private bool IsWaitingForHub => _connection?.State != HubConnectionState.Connected || !_isRegistered;

    private async Task DelayWhileWaitingForHubAsync(CancellationToken token)
    {
        if (_isDeepSleep)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), token);
            return;
        }

        if (_disconnectedSinceUtc.HasValue &&
            _clock.UtcNow - _disconnectedSinceUtc.Value >= IFederationBacklogService.SnapshotDisconnectThreshold)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(5), token);
    }

    private async Task ProcessQueueAsync(CancellationToken token)
    {
        _logger.LogInformation("FederationSyncWorker: Waiting for database to be ready...");
        while (!_stateProvider.IsDatabaseReady && !token.IsCancellationRequested)
        {
            await Task.Delay(1000, token);
        }

        if (token.IsCancellationRequested) return;

        _logger.LogInformation("FederationSyncWorker: Telemetry processing loop started.");

        var pendingCol = _db.GetCollection<PendingTelemetry>("pending_telemetry");

        while (!token.IsCancellationRequested)
        {
            var next = pendingCol.Query().OrderBy(x => x.CreatedAtUtc).FirstOrDefault();
            
            if (next == null)
            {
                var delayTask = Task.Delay(2000, token);
                var wakeTask = _telemetryWake.Reader.WaitToReadAsync(token).AsTask();
                await Task.WhenAny(delayTask, wakeTask);
                continue;
            }

            var hasLifecycle = next.Devices.Any(d => d.IsLifecycleGovernancePush);
            if (hasLifecycle)
            {
                _logger.LogInformation(
                    "FederationSyncWorker: Draining lifecycle governance batch ({Count} device(s)).",
                    next.Devices.Count);
            }

            bool sent = false;
            while (!sent && !token.IsCancellationRequested)
            {
                if (_connection?.State == HubConnectionState.Connected && _isRegistered)
                {
                    try
                    {
                        await _connection.InvokeAsync("PushTelemetry", next.Devices, cancellationToken: token);
                        _logger.LogTrace("FederationSyncWorker: Successfully pushed delta batch of {Count} devices to Hub.", next.Devices.Count);
                        
                        // Remove from queue on success
                        pendingCol.Delete(next.Id);
                        sent = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "FederationSyncWorker: Error pushing telemetry. Retrying in 10s...");
                        await Task.Delay(10000, token);
                    }
                }
                else
                {
                    await DelayWhileWaitingForHubAsync(token);
                }
            }
        }
    }

    private async Task StreamLogsAsync(CancellationToken token)
    {
        _logger.LogInformation("FederationSyncWorker: Remote log streaming loop started.");

        var pendingCol = _db.GetCollection<PendingLog>("pending_logs");
        pendingCol.EnsureIndex(x => x.CreatedAtUtc);

        while (!token.IsCancellationRequested)
        {
            // 1. Drain memory buffer and write directly to LiteDB local persistence queue
            var logs = _logBuffer.Drain();
            if (logs.Count > 0)
            {
                var pendingList = logs.Select(l => new PendingLog
                {
                    LogLevel = l.LogLevel,
                    Message = l.Message,
                    Exception = l.Exception,
                    Timestamp = l.Timestamp,
                    CreatedAtUtc = _clock.UtcNow
                }).ToList();

                pendingCol.InsertBulk(pendingList);
                _logger.LogTrace("FederationSyncWorker: Queued {Count} logs locally on disk.", logs.Count);

                // Housekeeping: Cap queue at 20,000 logs to prevent disk space exhaustion
                var totalCount = pendingCol.Count();
                if (totalCount > 20000)
                {
                    var excess = totalCount - 20000;
                    var toDelete = pendingCol.Query()
                        .OrderBy(x => x.CreatedAtUtc)
                        .Limit(excess)
                        .ToList()
                        .Select(x => x.Id)
                        .ToList();

                    foreach (var id in toDelete)
                    {
                        pendingCol.Delete(id);
                    }
                    _logger.LogWarning("FederationSyncWorker: Pruned {Count} oldest pending logs to satisfy 20k limit.", excess);
                }
            }

            // 2. Replay queue sequentially if connected
            var nextLogs = pendingCol.Query()
                .OrderBy(x => x.CreatedAtUtc)
                .Limit(200) // Batch of 200 to protect SignalR payloads
                .ToList();

            if (nextLogs.Count > 0)
            {
                if (_connection?.State == HubConnectionState.Connected && _isRegistered)
                {
                    try
                    {
                        // Convert PendingLog back to FederatedLog for transmission
                        var federatedLogs = nextLogs.Select(pl => new FederatedLog
                        {
                            LogLevel = pl.LogLevel,
                            Message = pl.Message,
                            Exception = pl.Exception,
                            Timestamp = pl.Timestamp
                        }).ToList();

                        await _connection.InvokeAsync("PushLogs", federatedLogs, cancellationToken: token);
                        _logger.LogTrace("FederationSyncWorker: Successfully pushed {Count} remote logs to Hub.", nextLogs.Count);

                        // Prune successfully sent logs from queue
                        foreach (var log in nextLogs)
                        {
                            pendingCol.Delete(log.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "FederationSyncWorker: Error pushing remote logs to Hub. Retrying in 10s...");
                        await Task.Delay(10000, token);
                    }
                }
                else
                {
                    await DelayWhileWaitingForHubAsync(token);
                }
            }
            else
            {
                // No pending logs, idle check in 2 seconds
                await Task.Delay(2000, token);
            }
        }
    }

    private async Task RevertToStandaloneAsync()
    {
        if (Interlocked.CompareExchange(ref _decoupling, 1, 0) != 0)
            return;

        try
        {
            _logger.LogWarning("FederationSyncWorker: Initiating local decoupling sequence...");
            _isRegistered = false;

            // Stop Hub connection before persisting decoupled settings  -  otherwise
            // OnSettingsChanged re-triggers registration and loops until process exit.
            if (_connection != null)
            {
                try
                {
                    await _connection.StopAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "FederationSyncWorker: Error stopping Hub connection during decouple.");
                }
            }

            // 1. Revert settings
            var localSettings = _settings.Current;
            localSettings.Mode = ExecutionMode.Standalone;
            localSettings.HubUrl = null;
            localSettings.FederationToken = null;
            localSettings.UseTailscaleForHubConnection = false;
            localSettings.HubTailscaleMagicDns = null;
            localSettings.HubTailscaleIpv4 = null;
            localSettings.SyncUsers = false;
            localSettings.SyncUserRegistry = false;
            localSettings.SyncSsoSettings = false;
            _settings.Save(localSettings);

            _enrollmentClient.PurgeCertificates();

            // 2. Promote federated users to local
            var userCol = _db.GetCollection<User>("users");
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
            _logger.LogInformation("FederationSyncWorker: Node successfully decoupled. Promoted {Count} federated users to local accounts.", promotedCount);

            // 3. Graceful exit to restart process/container in standalone mode
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                Environment.Exit(0);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FederationSyncWorker: Failed to execute local decoupling routine.");
        }
    }

    private async Task SyncEventsAsync(CancellationToken token)
    {
        _logger.LogInformation("FederationSyncWorker: Waiting for database to be ready before event sync...");
        while (!_stateProvider.IsDatabaseReady && !token.IsCancellationRequested)
        {
            await Task.Delay(1000, token);
        }

        if (token.IsCancellationRequested) return;

        _logger.LogInformation("FederationSyncWorker: System events sync loop started.");

        var pendingCol = _db.GetCollection<PendingEvent>("pending_events");
        pendingCol.EnsureIndex(x => x.CreatedAtUtc);

        while (!token.IsCancellationRequested)
        {
            var nextBatch = pendingCol.Query()
                .OrderBy(x => x.CreatedAtUtc)
                .Limit(100)
                .ToList();

            if (nextBatch.Count == 0)
            {
                await Task.Delay(2000, token);
                continue;
            }

            bool sent = false;
            while (!sent && !token.IsCancellationRequested)
            {
                if (_connection?.State == HubConnectionState.Connected && _isRegistered)
                {
                    try
                    {
                        var eventsToSync = nextBatch.Select(pe => pe.Event).ToList();
                        await _connection.InvokeAsync("PushEvents", eventsToSync, cancellationToken: token);
                        _logger.LogTrace("FederationSyncWorker: Successfully pushed batch of {Count} system events to Hub.", nextBatch.Count);

                        foreach (var pe in nextBatch)
                        {
                            pendingCol.Delete(pe.Id);
                        }
                        sent = true;
                    }
                    catch (Exception ex)
                    {
                        var exStr = ex.ToString();
                        if (exStr.Length > 2000) exStr = exStr.Substring(0, 2000) + "... [TRUNCATED]";
                        _logger.LogError("FederationSyncWorker: Error pushing system events to Hub. Exception: {Exception}. Retrying in 10s...", exStr);
                        await Task.Delay(10000, token);
                    }
                }
                else
                {
                    await DelayWhileWaitingForHubAsync(token);
                }
            }
        }
    }

    private async Task SyncAuditEventsAsync(CancellationToken token)
    {
        _logger.LogInformation("FederationSyncWorker: Waiting for database to be ready before audit event sync...");
        while (!_stateProvider.IsDatabaseReady && !token.IsCancellationRequested)
        {
            await Task.Delay(1000, token);
        }

        if (token.IsCancellationRequested) return;

        _logger.LogInformation("FederationSyncWorker: Audit events sync loop started.");

        var pendingCol = _db.GetCollection<PendingAuditEvent>("pending_audit_events");
        pendingCol.EnsureIndex(x => x.CreatedAtUtc);

        while (!token.IsCancellationRequested)
        {
            var nextBatch = pendingCol.Query()
                .OrderBy(x => x.CreatedAtUtc)
                .Limit(100)
                .ToList();

            if (nextBatch.Count == 0)
            {
                await Task.Delay(2000, token);
                continue;
            }

            bool sent = false;
            while (!sent && !token.IsCancellationRequested)
            {
                if (_connection?.State == HubConnectionState.Connected && _isRegistered)
                {
                    try
                    {
                        var eventsToSync = nextBatch.Select(pe => pe.Event).ToList();
                        await _connection.InvokeAsync("PushAuditEvents", eventsToSync, cancellationToken: token);
                        _logger.LogTrace("FederationSyncWorker: Successfully pushed batch of {Count} audit events to Hub.", nextBatch.Count);

                        foreach (var pe in nextBatch)
                        {
                            pendingCol.Delete(pe.Id);
                        }
                        sent = true;
                    }
                    catch (Exception ex)
                    {
                        var exStr = ex.ToString();
                        if (exStr.Length > 2000) exStr = exStr.Substring(0, 2000) + "... [TRUNCATED]";
                        _logger.LogError("FederationSyncWorker: Error pushing audit events to Hub. Exception: {Exception}. Retrying in 10s...", exStr);
                        await Task.Delay(10000, token);
                    }
                }
                else
                {
                    await DelayWhileWaitingForHubAsync(token);
                }
            }
        }
    }

    private async Task SyncAlertsAsync(CancellationToken token)
    {
        _logger.LogInformation("FederationSyncWorker: Waiting for database to be ready before alerts sync...");
        while (!_stateProvider.IsDatabaseReady && !token.IsCancellationRequested)
        {
            await Task.Delay(1000, token);
        }

        if (token.IsCancellationRequested) return;

        _logger.LogInformation("FederationSyncWorker: Alert events sync loop started.");

        var pendingCol = _db.GetCollection<PendingAlert>("pending_alerts");
        pendingCol.EnsureIndex(x => x.CreatedAtUtc);

        while (!token.IsCancellationRequested)
        {
            var nextBatch = pendingCol.Query()
                .OrderBy(x => x.CreatedAtUtc)
                .Limit(100)
                .ToList();

            if (nextBatch.Count == 0)
            {
                await Task.Delay(2000, token);
                continue;
            }

            bool sent = false;
            while (!sent && !token.IsCancellationRequested)
            {
                if (_connection?.State == HubConnectionState.Connected && _isRegistered)
                {
                    try
                    {
                        var alertsDto = nextBatch.Select(pa => new PendingAlertDto
                        {
                            Id = pa.Id.ToString(),
                            Alert = pa.Alert,
                            IsDelegation = pa.IsDelegation,
                            CreatedAtUtc = pa.CreatedAtUtc,
                            Device = pa.Device != null ? new DeviceDto
                            {
                                Id = pa.Device.Id,
                                IpAddress = pa.Device.IpAddress,
                                MacAddress = pa.Device.MacAddress,
                                Name = pa.Device.Name,
                                ManagedByUserId = pa.Device.ManagedByUserId,
                                AlertsEnabled = pa.Device.AlertsEnabled,
                                NodeId = pa.Device.NodeId,
                                FirstDiscoveredUtc = pa.Device.FirstDiscoveredUtc
                            } : null
                        }).ToList();
                        await _connection.InvokeAsync("PushAlerts", alertsDto, cancellationToken: token);
                        _logger.LogTrace("FederationSyncWorker: Successfully pushed batch of {Count} alert events to Hub.", nextBatch.Count);

                        foreach (var pa in nextBatch)
                        {
                            pendingCol.Delete(pa.Id);
                        }
                        sent = true;
                    }
                    catch (Exception ex)
                    {
                        var exStr = ex.ToString();
                        if (exStr.Length > 2000) exStr = exStr.Substring(0, 2000) + "... [TRUNCATED]";
                        _logger.LogError("FederationSyncWorker: Error pushing alert events to Hub. Exception: {Exception}. Retrying in 10s...", exStr);
                        await Task.Delay(10000, token);
                    }
                }
                else
                {
                    await DelayWhileWaitingForHubAsync(token);
                }
            }
        }
    }

    private HubConnection BuildHubConnection(
        HubEndpointPlan plan,
        HubEndpointCandidate candidate,
        System.Security.Cryptography.X509Certificates.X509Certificate2? clientCert,
        System.Security.Cryptography.X509Certificates.X509Certificate2? hubRootCert)
    {
        var hubEndpoint = plan.BuildUrl(candidate);
        return new HubConnectionBuilder()
            .WithUrl(hubEndpoint, options =>
            {
                options.HttpMessageHandlerFactory = _ =>
                    _transportHelper.CreateHandler(plan, candidate, clientCert, hubRootCert);
            })
            .AddJsonProtocol(options => StacksAtlas.API.Extensions.FederationJson.Configure(options.PayloadSerializerOptions))
            .WithAutomaticReconnect([
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMinutes(1)
            ])
            .WithServerTimeout(TimeSpan.FromMinutes(2))
            .WithKeepAliveInterval(TimeSpan.FromSeconds(15))
            .Build();
    }

    private void LogConnectFailure(string hubEndpoint, string label, int retryDelayMs, string details)
    {
        if (!_connectFailureSummaryLogged)
        {
            _connectFailureSummaryLogged = true;

            if (_endpointPlan?.UsesTailscaleTransport == true)
            {
                _connectionHint =
                    "Hub unreachable over tailnet (Hub may be offline or Tailscale stopped on the Hub host).";
                _logger.LogWarning(
                    "Federation: Hub unreachable at {Url} ({Label}). Local monitoring continues. " +
                    "If Tailscale was stopped on the Hub, restart it or disable tailnet transport in Settings → Federation. " +
                    "Details: {Details}. Further attempts logged at Debug.",
                    hubEndpoint,
                    label,
                    details);
                return;
            }

            _connectionHint = "Hub unreachable  -  retrying with backoff.";
            _logger.LogWarning(
                "SignalR: Unable to reach Hub at {Url} ({Label}). Retrying with backoff. Details: {Details}. Further attempts logged at Debug.",
                hubEndpoint,
                label,
                details);
            return;
        }

        _logger.LogDebug(
            "SignalR: Hub still unreachable at {Url} ({Label}). Next retry in {Delay}s. Details: {Details}",
            hubEndpoint,
            label,
            retryDelayMs / 1000,
            details);
    }

    private FederationCommandResult ExecuteHubUpdateOffer(FederationCommand cmd, bool requestedApply)
    {
        var channel = cmd.Parameters.GetValueOrDefault("channel") ?? "stable";
        var availableVersion = cmd.Parameters.GetValueOrDefault("availableVersion");
        if (string.IsNullOrWhiteSpace(availableVersion))
            return new FederationCommandResult { Success = false, Message = "Missing availableVersion parameter." };

        var initiatedBy = cmd.Parameters.GetValueOrDefault("initiatedBy");
        var message = cmd.Parameters.GetValueOrDefault("message");
        if (cmd.Parameters.TryGetValue("requestedApply", out var flag)
            && bool.TryParse(flag, out var parsedFlag))
        {
            requestedApply = parsedFlag;
        }

        var currentVersion = _serviceProvider.GetService<IApplianceVersionProvider>()?.GetCurrent().Version;
        if (!UpdateFleetPolicy.IsActionableOffer(availableVersion, currentVersion))
        {
            UpdateFleetPolicy.ClearPendingHubUpdate(_systemSettings);
            _logger.LogInformation(
                "SignalR: Ignoring Hub {Action} for v{Version}; local appliance is already on v{Current}",
                requestedApply ? "trigger" : "notify",
                availableVersion,
                currentVersion ?? "unknown");
            return new FederationCommandResult
            {
                Success = false,
                Message = $"Already on v{currentVersion ?? "current"}; Hub offer for v{availableVersion} was not stored."
            };
        }

        UpdateFleetPolicy.SetPendingHubUpdate(
            _systemSettings,
            channel,
            availableVersion,
            initiatedBy,
            requestedApply,
            message,
            _clock);

        _logger.LogWarning(
            "SignalR: Hub {Action} update v{Version} (channel={Channel}, by={InitiatedBy})",
            requestedApply ? "triggered" : "notified",
            availableVersion,
            channel,
            initiatedBy ?? "Hub");

        return new FederationCommandResult
        {
            Success = true,
            Message = requestedApply
                ? $"Update v{availableVersion} offered. Local admin must confirm apply."
                : $"Update v{availableVersion} notification stored."
        };
    }

    private async Task<FederationCommandResult> ExecuteTriggerSiteResetAsync(FederationCommand cmd)
    {
        var initiatedBy = cmd.Parameters.GetValueOrDefault("initiatedBy");
        if (string.IsNullOrWhiteSpace(initiatedBy))
            return new FederationCommandResult { Success = false, Message = "Missing initiatedBy parameter." };

        var initiatedAtUtc = _clock.UtcNow;
        if (cmd.Parameters.TryGetValue("initiatedAtUtc", out var atStr) &&
            DateTime.TryParse(atStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            initiatedAtUtc = parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
        }

        cmd.Parameters.TryGetValue("hubDisplayName", out var hubDisplayName);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var snapshotService = scope.ServiceProvider.GetRequiredService<ISnapshotService>();
            var dbPath = scope.ServiceProvider.GetRequiredService<string>();

            snapshotService.CreateSnapshot("Automatic_Before_SiteReset");
            SiteResetGovernance.Stage(dbPath);

            SiteResetGovernance.PersistRecoveryContext(
                _systemSettings,
                new SiteResetRecoveryContext
                {
                    HubInitiated = true,
                    InitiatedByUsername = initiatedBy,
                    InitiatedAtUtc = initiatedAtUtc,
                    HubDisplayName = hubDisplayName
                },
                _settings,
                _logger);

            _logger.LogWarning(
                "SignalR: Hub-initiated site reset staged by {InitiatedBy}. Restarting node service.",
                initiatedBy);

            // Return success before disconnect/restart so the Hub receives the ack.
            _ = Task.Run(async () =>
            {
                await Task.Delay(750);
                if (_connection != null)
                {
                    try
                    {
                        await _connection.StopAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "FederationSyncWorker: Error stopping Hub connection before site reset restart.");
                    }
                }

                await Task.Delay(750);
                Environment.Exit(0);
            });

            return new FederationCommandResult
            {
                Success = true,
                Message = "Site reset staged; restarting."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR: Failed to stage Hub-initiated site reset.");
            return new FederationCommandResult { Success = false, Message = ex.Message };
        }
    }

    private FederationCommandResult ExecuteLocalDeviceLifecycle(
        FederationCommand cmd,
        Func<Guid, bool> apply,
        string successMessage)
    {
        var device = ResolveCommandTargetDevice(cmd);
        if (device == null)
            return new FederationCommandResult { Success = false, Message = "Device not found locally" };

        if (!apply(device.Id))
            return new FederationCommandResult { Success = false, Message = "Lifecycle action failed locally" };

        _logger.LogInformation(
            "SignalR: {CommandType} applied to local device {DeviceId}",
            cmd.CommandType,
            device.Id);

        return new FederationCommandResult { Success = true, Message = successMessage };
    }

    private Device? ResolveCommandTargetDevice(FederationCommand cmd)
    {
        if (Guid.TryParse(cmd.TargetId, out var targetId))
        {
            var byId = _deviceRepo.GetById(targetId);
            if (byId != null) return byId;
        }

        var mac = cmd.Parameters.GetValueOrDefault("MacAddress");
        if (!string.IsNullOrWhiteSpace(mac))
        {
            var byMac = _deviceRepo.GetByMac(StacksAtlas.Core.Services.Devices.DeviceMacNormalizer.Normalize(mac) ?? mac);
            if (byMac != null) return byMac;
        }

        var ip = cmd.Parameters.GetValueOrDefault("IpAddress");
        if (!string.IsNullOrWhiteSpace(ip))
            return _deviceRepo.GetByIp(ip);

        return null;
    }

    /// <summary>
    /// High-frequency Hub polls and status reads. Logged at Debug so Info stays useful for real actions.
    /// </summary>
    private static bool IsRoutineFederationCommand(string? commandType) =>
        commandType is "Ping"
            or "GetDeepScanStatus"
            or "GetDeepScanResults"
            or "OpenAvcGetAllLinks"
            or "OpenAvcGetDrawerContext";

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        if (_connection != null)
        {
            await _connection.StopAsync(stoppingToken);
            await _connection.DisposeAsync();
        }
        await base.StopAsync(stoppingToken);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
