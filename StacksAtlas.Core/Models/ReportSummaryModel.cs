using System;
using System.Collections.Generic;

namespace StacksAtlas.Core.Models;

public class ReportSummaryModel
{
    public int TotalAssets { get; set; }
    public string StabilityScore { get; set; } = "100%";
    public int CriticalAlerts { get; set; }
    public string NetworkGrade { get; set; } = "F";
    public double AvgStability { get; set; }
    public List<FlapperEntry> TopFlappers { get; set; } = new();
    public List<SecurityRisk> SecurityRisks { get; set; } = new();
    public DistributionModel Distribution { get; set; } = new();
    public NetworkDeltasModel Deltas { get; set; } = new();
    public List<ServiceStatsModel> Services { get; set; } = new();
    public HealthInsightsModel Insights { get; set; } = new();
}

public class FlapperEntry
{
    public string? Name { get; set; }
    public string? IpAddress { get; set; }
    public int FlapCount { get; set; }
    public string? Status { get; set; }
    public string? Severity { get; set; }
}

public class DistributionModel
{
    public List<LabelValueCount> Vendors { get; set; } = new();
    public List<LabelValueCount> Types { get; set; } = new();
}

public class LabelValueCount
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
}

public class NetworkDeltasModel
{
    public List<NewDeviceDelta> NewDevices { get; set; } = new();
    public List<OfflineDeviceDelta> PersistentOffline { get; set; } = new();
}

public class NewDeviceDelta
{
    public string? IpAddress { get; set; }
    public string? Name { get; set; }
    public string? Vendor { get; set; }
    public DateTime FirstSeen { get; set; }
    public string? Id { get; set; }
}

public class OfflineDeviceDelta
{
    public string? IpAddress { get; set; }
    public string? Name { get; set; }
    public string? Vendor { get; set; }
    public DateTime LastSeen { get; set; }
    public string? Id { get; set; }
}

public class ServiceStatsModel
{
    public int Port { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class HealthInsightsModel
{
    public List<InsightEntry> Lagging { get; set; } = new();
    public List<InsightEntry> Flappers { get; set; } = new();
}

public class InsightEntry
{
    public string? Name { get; set; }
    public string? IpAddress { get; set; }
    public string? Id { get; set; }
    public string? Value { get; set; }
    public string? Label { get; set; }
}
