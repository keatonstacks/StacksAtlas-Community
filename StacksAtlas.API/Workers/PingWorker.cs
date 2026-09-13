using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.API.Services;
using System.Diagnostics;
using StacksAtlas.Core.Services.Licensing;

/// <summary>
/// The central heart of the StacksAtlas network monitoring engine.
/// This worker orchestrates the periodic discovery cycles and synchronizes results with the database.
/// </summary>
public sealed class PingWorker(
    ILogger<PingWorker> logger,
    PollingSettingsStore pollingStore,
    NetworkSettingsStore networkSettings,
    INetworkDiscoveryService discoveryService,
    IDeviceSyncService syncService,
    IAlertService alertService,
    SystemStateProvider stateProvider,
    StacksAtlas.Core.Services.Auth.IAuthService authService,
    INetworkService networkService,
    IClock clock,
    ILicenseService licenseService,
    SystemSettingsStore systemSettingsStore) : BackgroundService
{
    private readonly ILogger<PingWorker> _logger = logger;
    private readonly PollingSettingsStore _pollingStore = pollingStore;
    private readonly NetworkSettingsStore _networkSettings = networkSettings;
    private readonly INetworkDiscoveryService _discoveryService = discoveryService;
    private readonly IDeviceSyncService _syncService = syncService;
    private readonly IAlertService _alertService = alertService;
    private readonly SystemStateProvider _stateProvider = stateProvider;
    private readonly StacksAtlas.Core.Services.Auth.IAuthService _authService = authService;
    private readonly INetworkService _networkService = networkService;
    private readonly IClock _clock = clock;
    private readonly ILicenseService _licenseService = licenseService;
    private readonly SystemSettingsStore _systemSettingsStore = systemSettingsStore;
    private DateTime _lastDiscoveryGateLogUtc = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PingWorker (Production Grade) is now active.");

        // Wait for database to be fully operational before starting discovery
        while (!_stateProvider.IsDatabaseReady && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(500, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            // 1. Setup Gate: Wait for initial admin account
            if (!_authService.AnyUsers())
            {
                _stateProvider.IsScanning = false;
                LogDiscoveryGateThrottled("Waiting for admin account setup before discovery can start.");
                await DelayUnlessSettingsChanged(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            // 2. License gate: active HWID-bound entitlement required (v2 PLG)
            var licenseStatus = await _licenseService.GetCurrentStatusAsync();
            if (!licenseStatus.IsActive)
            {
                _stateProvider.IsScanning = false;
                LogDiscoveryGateThrottled(
                    "Discovery paused: license is not active ({Message}).",
                    licenseStatus.Message ?? "inactive");
                await DelayUnlessSettingsChanged(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            // 3. Onboarding gate: foundation wizard must be complete
            var onboarding = _systemSettingsStore.Load().Onboarding;
            var foundationComplete = OnboardingSettingsReset.GetEffectiveFoundationComplete(
                onboarding,
                hasUsers: true);
            if (!foundationComplete)
            {
                _stateProvider.IsScanning = false;
                LogDiscoveryGateThrottled("Discovery paused: onboarding foundation is not complete yet.");
                await DelayUnlessSettingsChanged(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            // 4. Subnet gate: ensure at least one subnet is configured (standalone / node sites)
            if (!ExecutionState.IsHub && !onboarding.OnboardingNetworkConfirmed)
            {
                _stateProvider.IsScanning = false;
                LogDiscoveryGateThrottled("Discovery paused: network scope has not been confirmed in onboarding.");
                await DelayUnlessSettingsChanged(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            var currentSettings = _networkSettings.Load();
            if (currentSettings.Subnets.Count == 0)
            {
                NetworkDiscoveryScopeBootstrap.EnsureAtLeastOneSubnet(
                    _networkSettings,
                    _networkService,
                    _logger);
                currentSettings = _networkSettings.Load();
                if (currentSettings.Subnets.Count == 0)
                {
                    LogDiscoveryGateThrottled(
                        "Discovery paused: no scan subnets configured and auto-seed could not detect a usable CIDR. Set a subnet in Settings → Network (Docker needs network_mode: host on Linux).");
                    await DelayUnlessSettingsChanged(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }
            }

            // 5. Execution Cycle
            _stateProvider.IsScanning = true;
            try
            {
                // Orchestrate the discovery (mDNS + Sweep + Enrichment)
                var sweep = await _discoveryService.RunDiscoveryCycleAsync(stoppingToken);

                // Synchronize result to DB and calculate state changes (online/offline/roamed)
                var (devices, preSweepStatuses) = await _syncService.SyncSweepResultAsync(sweep, stoppingToken);

                // Evaluate and dispatch alerts
                await _alertService.EvaluateAndSendAlertsAsync(devices, preSweepStatuses);

                _stateProvider.LastSweepTime = _clock.UtcNow;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PingWorker: Discovery cycle failed.");
            }
            finally
            {
                _stateProvider.IsScanning = false;
            }

            // 4. Responsive Delay (Interruptible if settings change)
            await WaitForNextIntervalAsync(stoppingToken);
        }
    }

    private async Task DelayUnlessSettingsChanged(TimeSpan delay, CancellationToken stoppingToken)
    {
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        void OnSettingsChanged() { try { delayCts.Cancel(); } catch { } }

        _systemSettingsStore.OnSettingsChanged += OnSettingsChanged;
        _pollingStore.OnSettingsChanged += OnSettingsChanged;
        _networkSettings.OnSettingsChanged += OnSettingsChanged;

        try
        {
            await Task.Delay(delay, delayCts.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("PingWorker: Waking early due to configuration change.");
        }
        finally
        {
            _systemSettingsStore.OnSettingsChanged -= OnSettingsChanged;
            _pollingStore.OnSettingsChanged -= OnSettingsChanged;
            _networkSettings.OnSettingsChanged -= OnSettingsChanged;
        }
    }

    private async Task WaitForNextIntervalAsync(CancellationToken stoppingToken)
    {
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        void OnSettingsChanged() { try { delayCts.Cancel(); } catch { } }

        _systemSettingsStore.OnSettingsChanged += OnSettingsChanged;
        _pollingStore.OnSettingsChanged += OnSettingsChanged;
        _networkSettings.OnSettingsChanged += OnSettingsChanged;

        try
        {
            // Honor operator-configured interval on all tiers (no license velocity floor).
            var intervalSeconds = Math.Max(1, _pollingStore.Current.IntervalSeconds);
            _logger.LogDebug("PingWorker: Next sweep in {Interval}s", intervalSeconds);

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), delayCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (stoppingToken.IsCancellationRequested) throw;
            _logger.LogInformation("PingWorker: Rescheduling scan due to configuration update.");
        }
        finally
        {
            _systemSettingsStore.OnSettingsChanged -= OnSettingsChanged;
            _pollingStore.OnSettingsChanged -= OnSettingsChanged;
            _networkSettings.OnSettingsChanged -= OnSettingsChanged;
        }
    }

    private void LogDiscoveryGateThrottled(string message, params object?[] args)
    {
        var now = _clock.UtcNow;
        if (now - _lastDiscoveryGateLogUtc < TimeSpan.FromMinutes(1))
            return;

        _lastDiscoveryGateLogUtc = now;
        _logger.LogWarning(message, args);
    }
}
