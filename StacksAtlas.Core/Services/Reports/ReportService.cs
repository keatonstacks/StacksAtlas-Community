using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;
using SKImage = SkiaSharp.SKImage;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.State;

namespace StacksAtlas.Core.Services.Reports;

public class ReportService
{
    private readonly IDeviceRepository _deviceRepo;
    private readonly IEventRepository _eventRepo;
    private readonly IFederatedNodeRepository _nodeRepo;
    private readonly SecurityAuditService _securityAuditor;
    private readonly IClock _clock;
    private readonly ILogger<ReportService> _logger;

    public ReportService(
        IDeviceRepository deviceRepo,
        IEventRepository eventRepo,
        IFederatedNodeRepository nodeRepo,
        SecurityAuditService securityAuditor,
        IClock clock,
        ILogger<ReportService> logger)
    {
        _deviceRepo = deviceRepo;
        _eventRepo = eventRepo;
        _nodeRepo = nodeRepo;
        _securityAuditor = securityAuditor;
        _clock = clock;
        _logger = logger;
    }

    private List<Device> GetFilteredDevices(string[]? nodeIds, string? client, string? building, string? room)
    {
        var query = _deviceRepo.GetAll().AsEnumerable();
        if (nodeIds != null && nodeIds.Length > 0) query = query.Where(d => nodeIds.Contains(d.NodeId));
        if (!string.IsNullOrWhiteSpace(client)) query = query.Where(d => d.Client?.Equals(client, StringComparison.OrdinalIgnoreCase) == true);
        if (!string.IsNullOrWhiteSpace(building)) query = query.Where(d => d.Building?.Equals(building, StringComparison.OrdinalIgnoreCase) == true);
        if (!string.IsNullOrWhiteSpace(room)) query = query.Where(d => d.Room?.Equals(room, StringComparison.OrdinalIgnoreCase) == true);
        return query.ToList();
    }

    public byte[] GenerateInventoryCsv(string[]? nodeIds = null, string? client = null, string? building = null, string? room = null)
    {
        var portable = ExecutionState.IsPortable;
        var devices = GetFilteredDevices(portable ? null : nodeIds, portable ? null : client, portable ? null : building, portable ? null : room);
        var nodeLookup = portable
            ? new Dictionary<string, FederatedNode>(StringComparer.OrdinalIgnoreCase)
            : _nodeRepo.GetAll().ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);

        // Parent names may live outside the current filter (e.g. site-wide switch).
        var deviceById = _deviceRepo.GetAll().ToDictionary(d => d.Id);
        var rows = devices
            .Select(device => BuildInventoryCsvRow(device, nodeLookup, deviceById))
            .ToList();

        rows = portable
            ? rows.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.DeviceIp, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : rows.OrderBy(row => row.SiteName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.LocationMetadata, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.ReportingNodeName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.DeviceIp, StringComparer.OrdinalIgnoreCase)
                .ToList();

        var sb = new StringBuilder();
        sb.AppendLine(portable
            ? "DiscoveryInterface,DeviceIP,AssetStatus,FirstDiscoveredUtc,ID,Name,Hostname,Vendor,Model,MAC Address,Manually Set,Last Seen,Flaps,Avg Latency (ms),Type,Security Grade,Stability Score,Open Ports,Serial,Asset Tag,Firmware,Warranty Expires,AttachmentKind,AttachmentPort,AttachmentParentName"
            : "SiteName,LocationMetadata,ReportingNodeName,DiscoveryInterface,DiscoveryVlanTag,DeviceIP,AssetStatus,FirstDiscoveredUtc,ID,Name,Hostname,Vendor,Model,MAC Address,Manually Set,Last Seen,Flaps,Avg Latency (ms),Type,Security Grade,Stability Score,Open Ports,Serial,Asset Tag,Firmware,Warranty Expires,AttachmentKind,AttachmentPort,AttachmentParentName");

        foreach (var row in rows)
        {
            sb.AppendLine(portable
                ? string.Join(",",
                    CsvField(row.DiscoveryInterface),
                    CsvField(row.DeviceIp),
                    CsvField(row.AssetStatus),
                    CsvField(row.FirstDiscoveredUtc),
                    row.Id.ToString(),
                    CsvField(row.Name),
                    CsvField(row.Hostname),
                    CsvField(row.Vendor),
                    CsvField(row.Model),
                    CsvField(row.MacAddress),
                    row.IsModelManuallySet,
                    row.LastSeen,
                    row.FlapCount,
                    row.AverageLatencyMs,
                    CsvField(row.Type),
                    row.SecurityGrade,
                    row.StabilityScore,
                    CsvField(row.OpenPorts),
                    CsvField(row.SerialNumber),
                    CsvField(row.AssetTag),
                    CsvField(row.FirmwareVersion),
                    CsvField(row.WarrantyExpires),
                    CsvField(row.AttachmentKind),
                    CsvField(row.AttachmentPort),
                    CsvField(row.AttachmentParentName))
                : string.Join(",",
                    CsvField(row.SiteName),
                    CsvField(row.LocationMetadata),
                    CsvField(row.ReportingNodeName),
                    CsvField(row.DiscoveryInterface),
                    CsvField(row.DiscoveryVlanTag),
                    CsvField(row.DeviceIp),
                    CsvField(row.AssetStatus),
                    CsvField(row.FirstDiscoveredUtc),
                    row.Id.ToString(),
                    CsvField(row.Name),
                    CsvField(row.Hostname),
                    CsvField(row.Vendor),
                    CsvField(row.Model),
                    CsvField(row.MacAddress),
                    row.IsModelManuallySet,
                    row.LastSeen,
                    row.FlapCount,
                    row.AverageLatencyMs,
                    CsvField(row.Type),
                    row.SecurityGrade,
                    row.StabilityScore,
                    CsvField(row.OpenPorts),
                    CsvField(row.SerialNumber),
                    CsvField(row.AssetTag),
                    CsvField(row.FirmwareVersion),
                    CsvField(row.WarrantyExpires),
                    CsvField(row.AttachmentKind),
                    CsvField(row.AttachmentPort),
                    CsvField(row.AttachmentParentName)));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static InventoryCsvRow BuildInventoryCsvRow(
        Device device,
        IReadOnlyDictionary<string, FederatedNode> nodeLookup,
        IReadOnlyDictionary<Guid, Device> deviceById)
    {
        nodeLookup.TryGetValue(device.NodeId ?? string.Empty, out var node);

        var siteName = FirstNonEmpty(device.Client, node?.Client);
        var locationMetadata = BuildLocationMetadata(device.Building, device.Room, node?.Building, node?.Room);
        var reportingNodeName = FirstNonEmpty(node?.Name, device.NodeId, "Local");
        var parentName = string.Empty;
        if (device.AttachmentParentDeviceId is Guid parentId && deviceById.TryGetValue(parentId, out var parent))
            parentName = FirstNonEmpty(parent.Name, parent.Hostname, parent.IpAddress);

        var firstDiscovered = device.FirstDiscoveredUtc ?? device.FirstSeen;
        var firstDiscoveredUtc = firstDiscovered == DateTime.MinValue
            ? string.Empty
            : firstDiscovered.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

        return new InventoryCsvRow
        {
            SiteName = siteName,
            LocationMetadata = locationMetadata,
            ReportingNodeName = reportingNodeName,
            DiscoveryInterface = FormatDiscoveryInterface(device.DiscoveryInterfaceName, device.DiscoveryInterfaceId),
            DiscoveryVlanTag = device.DiscoveryVlanTag ?? string.Empty,
            DeviceIp = device.IpAddress,
            AssetStatus = device.Status,
            FirstDiscoveredUtc = firstDiscoveredUtc,
            Id = device.Id,
            Name = device.Name,
            Hostname = device.Hostname,
            Vendor = device.Vendor,
            Model = device.Model,
            MacAddress = device.MacAddress,
            IsModelManuallySet = device.IsModelManuallySet,
            LastSeen = device.LastSeen == DateTime.MinValue ? string.Empty : device.LastSeen.ToString("yyyy-MM-dd HH:mm:ss"),
            FlapCount = device.FlapCount,
            AverageLatencyMs = device.AverageLatencyMs.GetValueOrDefault().ToString("F1"),
            Type = device.Type,
            SecurityGrade = device.SecurityGrade.ToString(),
            StabilityScore = device.StabilityScore,
            OpenPorts = device.OpenPorts != null ? string.Join(";", device.OpenPorts) : string.Empty,
            SerialNumber = device.SerialNumber,
            AssetTag = device.AssetTag,
            FirmwareVersion = device.FirmwareVersion,
            WarrantyExpires = DeviceAssetCsvParser.FormatWarrantyDate(device.WarrantyExpiresUtc),
            AttachmentKind = device.AttachmentKind ?? "Unknown",
            AttachmentPort = device.AttachmentPort ?? string.Empty,
            AttachmentParentName = parentName,
        };
    }

    private static string BuildLocationMetadata(string? deviceBuilding, string? deviceRoom, string? nodeBuilding, string? nodeRoom)
    {
        var building = FirstNonEmpty(deviceBuilding, nodeBuilding);
        var room = FirstNonEmpty(deviceRoom, nodeRoom);

        if (!string.IsNullOrWhiteSpace(building) && !string.IsNullOrWhiteSpace(room))
            return $"{building} / {room}";

        return FirstNonEmpty(building, room);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    private static string FormatDiscoveryInterface(string? interfaceName, string? interfaceId)
    {
        if (!string.IsNullOrWhiteSpace(interfaceName) && !string.IsNullOrWhiteSpace(interfaceId))
            return $"{interfaceName} ({interfaceId})";

        return interfaceName ?? interfaceId ?? string.Empty;
    }

    private static string CsvField(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private sealed class InventoryCsvRow
    {
        public string SiteName { get; init; } = string.Empty;
        public string LocationMetadata { get; init; } = string.Empty;
        public string ReportingNodeName { get; init; } = string.Empty;
        public string DiscoveryInterface { get; init; } = string.Empty;
        public string DiscoveryVlanTag { get; init; } = string.Empty;
        public string DeviceIp { get; init; } = string.Empty;
        public string AssetStatus { get; init; } = string.Empty;
        public string FirstDiscoveredUtc { get; init; } = string.Empty;
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Hostname { get; init; }
        public string? Vendor { get; init; }
        public string? Model { get; init; }
        public string? MacAddress { get; init; }
        public bool IsModelManuallySet { get; init; }
        public string LastSeen { get; init; } = string.Empty;
        public int FlapCount { get; init; }
        public string AverageLatencyMs { get; init; } = string.Empty;
        public string? Type { get; init; }
        public string SecurityGrade { get; init; } = string.Empty;
        public int StabilityScore { get; init; }
        public string OpenPorts { get; init; } = string.Empty;
        public string? SerialNumber { get; init; }
        public string? AssetTag { get; init; }
        public string? FirmwareVersion { get; init; }
        public string WarrantyExpires { get; init; } = string.Empty;
        public string AttachmentKind { get; init; } = "Unknown";
        public string AttachmentPort { get; init; } = string.Empty;
        public string AttachmentParentName { get; init; } = string.Empty;
    }

    public ReportSummaryModel GetFullSummary(string[]? nodeIds = null, string? client = null, string? building = null, string? room = null)
    {
        var devices = GetFilteredDevices(nodeIds, client, building, room);
        return GetFullSummaryInternal(devices);
    }

    private ReportSummaryModel GetFullSummaryInternal(List<Device> devices)
    {
        var total = devices.Count;
        var securityRisks = GetSecurityRisks(devices, 100).ToList();

        double stability = 100.0;
        if (total > 0)
        {
            stability = devices.Average(d => d.StabilityScore);
        }

        var criticalFlaps = devices.Count(d => d.FlapCount > 10);
        var criticalSecurity = securityRisks.Count(r => r.Level == "Critical");

        string grade = stability switch
        {
            >= 97 => "A+",
            >= 93 => "A",
            >= 90 => "A-",
            >= 87 => "B+",
            >= 83 => "B",
            >= 80 => "B-",
            >= 70 => "C",
            >= 60 => "D",
            _ => "F"
        };

        return new ReportSummaryModel
        {
            TotalAssets = total,
            StabilityScore = $"{stability:F1}%",
            AvgStability = stability,
            NetworkGrade = grade,
            CriticalAlerts = criticalFlaps + criticalSecurity,
            TopFlappers = GetTopFlappers(devices, 5).ToList(),
            SecurityRisks = securityRisks,
            Distribution = GetDistribution(devices),
            Deltas = GetNetworkDeltas(devices),
            Services = GetServiceStats(devices),
            Insights = GetHealthInsights(devices)
        };
    }

    public byte[] GenerateAuditPdf(string[]? nodeIds = null, string? client = null, string? building = null, string? room = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.EnableDebugging = true;

        var portable = ExecutionState.IsPortable;
        var devices = GetFilteredDevices(
            portable ? null : nodeIds,
            portable ? null : client,
            portable ? null : building,
            portable ? null : room);
        var summary = GetFullSummaryInternal(devices);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(0.5f, Unit.Inch); // Reduced margins for better footer placement
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

                page.Header().ShowIf(ctx => ctx.PageNumber > 1).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("StacksAtlas AUDIT REPORT")
                            .FontSize(20).SemiBold().FontColor(Colors.Blue.Medium); // Reduced from 32
                        col.Item().Text("NETWORK COMMISSIONING & GOVERNANCE")
                            .FontSize(10).LetterSpacing(0.1f).FontColor(Colors.Grey.Medium);
                    });

                    row.ConstantItem(100).AlignRight().Column(col =>
                    {
                        col.Item().Text("DATE").FontSize(9).SemiBold();
                        col.Item().Text(_clock.Now.ToString("yyyy-MM-dd")).FontSize(11);
                    });
                });

                page.Content().Column(col =>
                {
                    // --- COVER PAGE ---
                    // REMOVED Height(800) to prevent overflow crash. 
                    // Using MinHeight or just spacing is safer.
                    col.Item().Background(Colors.Blue.Darken4).Padding(30).Column(cover =>
                    {
                        cover.Item().PaddingTop(80).Text("StacksAtlas").FontSize(36).ExtraBold().FontColor(Colors.White).LetterSpacing(0.2f);
                        cover.Item().Text("NETWORK COMMISSIONING & GOVERNANCE").FontSize(14).SemiBold().FontColor(Colors.Grey.Lighten1);
                        
                        cover.Item().PaddingTop(100).Column(meta => {
                            meta.Item().Text($"REPORT GENERATED: {_clock.Now:yyyy-MM-dd}").FontSize(11).FontColor(Colors.White);
                            meta.Item().Text($"CLIENT: {summary.TotalAssets} ASSETS DETECTED").FontSize(11).FontColor(Colors.White);
                            meta.Item().Text("AUDIT STATUS: COMPLETED").FontSize(11).FontColor(Colors.Green.Accent2).Bold();
                        });
                    });

                    col.Item().PageBreak();

                    // --- HEADER FOR SUBSEQUENT PAGES ---
                    col.Item().PaddingVertical(20).Column(header => {
                         header.Item().Text("EXECUTIVE SUMMARY").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken3);
                         header.Item().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Height(5);
                    });

                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Padding(10).Background(Colors.Grey.Lighten4).Column(inner =>
                        {
                            inner.Item().AlignCenter().Text("ASSETS").FontSize(9).SemiBold();
                            inner.Item().AlignCenter().Text(summary.TotalAssets.ToString()).FontSize(20).SemiBold();
                        });
                        row.ConstantItem(10);
                        row.RelativeItem().Padding(10).Background(Colors.Grey.Lighten4).Column(inner =>
                        {
                            inner.Item().AlignCenter().Text("HEALTH").FontSize(9).SemiBold();
                            inner.Item().AlignCenter().Text(summary.StabilityScore).FontSize(20).SemiBold().FontColor(Colors.Green.Medium);
                        });
                        row.ConstantItem(10);
                        row.RelativeItem().Padding(10).Background(Colors.Grey.Lighten4).Column(inner =>
                        {
                            inner.Item().AlignCenter().Text("ALERTS").FontSize(9).SemiBold();
                            inner.Item().AlignCenter().Text(summary.CriticalAlerts.ToString()).FontSize(20).SemiBold().FontColor(summary.CriticalAlerts > 0 ? Colors.Red.Medium : Colors.Grey.Darken1);
                        });
                    });

                    // --- EXECUTIVE GRADE BADGE ---
                    var avgStability = summary.AvgStability;
                    string grade = summary.NetworkGrade;

                    col.Item().PaddingTop(20).PaddingBottom(10).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                        .Row(row => 
                        {
                            row.RelativeItem().AlignMiddle().Text("NETWORK GRADE").FontSize(14).SemiBold().AlignLeft();
                            
                            // Render Badge
                            var badgeBytes = GenerateGradeBadge(grade);
                            if (badgeBytes.Length > 0)
                                row.ConstantItem(60).Image(badgeBytes);
                            else
                                row.RelativeItem().Text(grade).FontSize(24).ExtraBold().FontColor(avgStability >= 90 ? Colors.Green.Medium : (avgStability >= 70 ? Colors.Orange.Medium : Colors.Red.Medium)).AlignRight();
                        });


                    // --- NETWORK VITALS TABLE (BEAST MODE) ---
                    col.Item().PaddingTop(20).Text("NETWORK VITALS & PERFORMANCE").FontSize(14).SemiBold();
                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3); // Device
                            columns.RelativeColumn(2); // Vendor
                            columns.RelativeColumn(2); // Avg Latency
                            columns.RelativeColumn(1); // Security
                            columns.RelativeColumn(1); // Ports
                            columns.RelativeColumn(1); // Score
                        });

                        table.Header(header =>
                        {
                            header.Cell().BorderBottom(1).PaddingVertical(5).Text("DEVICE").SemiBold().FontSize(9);
                            header.Cell().BorderBottom(1).PaddingVertical(5).Text("VENDOR").SemiBold().FontSize(9);
                            header.Cell().BorderBottom(1).PaddingVertical(5).Text("AVG LATENCY").SemiBold().FontSize(9);
                            header.Cell().BorderBottom(1).PaddingVertical(5).Text("SECURITY").SemiBold().FontSize(9);
                            header.Cell().BorderBottom(1).PaddingVertical(5).Text("PORTS").SemiBold().FontSize(9);
                            header.Cell().BorderBottom(1).PaddingVertical(5).Text("SCORE").SemiBold().FontSize(9);
                        });

                        foreach(var d in devices.OrderBy(x => x.IpAddress))
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).PaddingVertical(5).Column(c => {
                                c.Item().Text(d.Name).FontSize(9).SemiBold();
                                c.Item().Text(d.IpAddress).FontSize(8).FontColor(Colors.Grey.Medium);
                            });
                            
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).PaddingVertical(5).Text(d.Vendor ?? "-").FontSize(9);
                            
                            // Latency: Prefer Average, fallback to Last
                            var lat = d.AverageLatencyMs > 0 ? d.AverageLatencyMs : (d.LastLatencyMs > 0 ? d.LastLatencyMs : 0);
                            var latencyText = lat > 0 ? $"{lat:F1} ms" : "-";
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).PaddingVertical(5).Text(latencyText).FontSize(9);

                            // Security Grade
                            var secColor = d.SecurityGrade == SecurityGrade.Green ? Colors.Green.Medium : (d.SecurityGrade == SecurityGrade.Yellow ? Colors.Orange.Medium : Colors.Red.Medium);
                            var secGrade = d.SecurityGrade == SecurityGrade.Green ? "A" : (d.SecurityGrade == SecurityGrade.Yellow ? "C" : "F");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).PaddingVertical(5).Text(secGrade).FontSize(9).Bold().FontColor(secColor);

                            // Ports
                            var portCount = d.OpenPorts?.Count ?? 0;
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).PaddingVertical(5).Text(portCount.ToString()).FontSize(9);

                            var scoreColor = d.StabilityScore >= 90 ? Colors.Green.Medium : (d.StabilityScore >= 70 ? Colors.Orange.Medium : Colors.Red.Medium);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).PaddingVertical(5).AlignRight().Text($"{d.StabilityScore}").FontSize(10).Bold().FontColor(scoreColor);
                        }
                    });

                    col.Item().PageBreak();
                    if (summary.SecurityRisks.Any())
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(5);
                                columns.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                header.Cell().BorderBottom(1).PaddingVertical(5).Text("Device").SemiBold();
                                header.Cell().BorderBottom(1).PaddingVertical(5).Text("Finding").SemiBold();
                                header.Cell().BorderBottom(1).PaddingVertical(5).Text("Severity").SemiBold();
                            });

                            foreach (var risk in summary.SecurityRisks)
                            {
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).Text(risk.DeviceName);
                                var findingText = !string.IsNullOrWhiteSpace(risk.Title)
                                    ? $"{risk.Title}\n{risk.Description}"
                                    : risk.Reason;
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).Text(findingText);
                                var severityLabel = !string.IsNullOrWhiteSpace(risk.Severity) ? risk.Severity : risk.Level;
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).Text(severityLabel).FontColor(risk.Level == "Critical" ? Colors.Red.Medium : Colors.Orange.Medium).SemiBold();
                            }
                        });
                    }

                    col.Item().PaddingTop(30).PaddingBottom(10).Text("HEALTH & PERFORMANCE OUTLIERS").FontSize(14).SemiBold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(3);
                        });

                        table.Header(header =>
                        {
                            header.Cell().BorderBottom(1).PaddingVertical(5).Text("Device Identifier").SemiBold();
                            header.Cell().BorderBottom(1).PaddingVertical(5).Text("Metric").SemiBold();
                            header.Cell().BorderBottom(1).PaddingVertical(5).Text("Current Value").SemiBold();
                        });

                        foreach (var insight in summary.Insights.Flappers)
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).Text(insight.Name);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).Text(insight.Label);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).Text(insight.Value);
                        }
                    });

                    col.Item().PageBreak();
                    col.Item().Text("ESTATE COMPOSITION: VENDOR DISTRIBUTION").FontSize(14).SemiBold();
                    
                    // --- PIE CHART SECTION ---
                    var vendorDict = summary.Distribution.Vendors.ToDictionary(x => x.Label, x => x.Value);
                    
                    col.Item().PaddingTop(20).Column(c => 
                    {
                        var chartBytes = GeneratePieChartImage(vendorDict);
                        
                        // 1. Chart (Centered)
                        c.Item().AlignCenter().Height(250).Image(chartBytes).FitHeight();
                        
                        // 2. Native Legend (Grid)
                        c.Item().PaddingTop(15).Table(table => 
                        {
                            table.ColumnsDefinition(cols => {
                                cols.ConstantColumn(25); // Dot (Increased from 15)
                                cols.RelativeColumn();   // Name
                                cols.ConstantColumn(30); // Count
                                
                                cols.ConstantColumn(20); // Gutter
                                
                                cols.ConstantColumn(25); // Dot (Increased from 15)
                                cols.RelativeColumn();
                                cols.ConstantColumn(30);
                            });
                            
                            var colors = new[] { "#2196F3", "#4CAF50", "#FFC107", "#F44336", "#9C27B0", "#607D8B" };
                            int idx = 0;
                            
                            foreach(var v in vendorDict) 
                            {
                                string color = colors[idx % colors.Length];
                                
                                // Color Dot (Text Bullet)
                                table.Cell().AlignMiddle().PaddingRight(5).Text("•").FontSize(20).FontColor(color).LineHeight(1);
                                
                            table.Cell().AlignMiddle().Text(v.Key).FontSize(9).FontColor(Colors.Grey.Darken2);
                            table.Cell().AlignMiddle().AlignRight().Text(v.Value.ToString()).FontSize(9).SemiBold();
                            
                            if (idx % 2 == 0 && idx < vendorDict.Count - 1) table.Cell(); // Spacer
                            idx++;
                            }
                        });
                    });

                    col.Item().PageBreak();
                    col.Item().Text("GLOBAL SERVICE MATRIX").FontSize(14).SemiBold();
                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        var services = summary.Services;
                        
                        // Header is not strictly necessary for a grid-like view, but good for structure
                        // For a pure grid look, we just add cells. 
                        // To allow wrapping (4 items per row), this Table approach is different from Grid.
                        // Grid allows simpler n-column wrapping. Table needs explicit row management if we want strict grid.
                        // However, QuestPDF's Table fills cells left-to-right, top-to-bottom.
                        
                        foreach (var s in services)
                        {
                            table.Cell().Padding(5).Background(Colors.Grey.Lighten4).AlignCenter().Column(inner =>
                            {
                                inner.Item().Text(s.Count.ToString()).FontSize(14).SemiBold();
                                inner.Item().Text(s.Name).FontSize(8).SemiBold().FontColor(Colors.Grey.Medium);
                                inner.Item().Text("PORT " + s.Port.ToString()).FontSize(7).FontColor(Colors.Grey.Lighten1);
                            });
                        }
                    });
                });

                page.Footer().AlignBottom().Column(col =>
                {
                    col.Item().BorderTop(1).BorderColor(Colors.Grey.Lighten2).Width(50).Height(5); // Small separator line
                    col.Item().PaddingTop(5).AlignCenter().Text("StacksAtlas PROFESSIONAL SERVICES  |  TECHNICAL GOVERNANCE REPORT").FontSize(8).FontColor(Colors.Grey.Medium).LetterSpacing(0.05f);
                    col.Item().AlignCenter().Text(x =>
                    {
                        x.CurrentPageNumber();
                        x.Span(" / ");
                        x.TotalPages();
                    });
                });
            });
        });

        try
        {
            using (var stream = new MemoryStream())
            {
                document.GeneratePdf(stream);
                return stream.ToArray();
            }
        }
        catch (Exception ex)
        {
            // Log deep details for Docker/Linux debugging
            _logger.LogError(ex, "QuestPDF/SkiaSharp Generation Crash");
            
            // Re-throw to be caught by the controller with context
            throw new InvalidOperationException("PDF rendering engine failed. This usually indicates missing native libraries (libfontconfig1) or fonts in the host environment.", ex);
        }
    }

    public IEnumerable<FlapperEntry> GetTopFlappers(List<Device> devices, int count)
    {
        return devices
            .Where(d => d.FlapCount > 0)
            .OrderByDescending(d => d.FlapCount)
            .Take(count)
            .Select(d => new FlapperEntry
            {
                Name = d.Name,
                IpAddress = d.IpAddress,
                FlapCount = d.FlapCount,
                Status = d.Status,
                Severity = d.FlapCount > 10 ? "Critical" : "Warning"
            });
    }

    public IEnumerable<SecurityRisk> GetSecurityRisks(List<Device> devices, int count)
    {
        var risks = new List<SecurityRisk>();

        foreach (var d in devices)
        {
            var audit = _securityAuditor.AuditDevice(d);
            foreach (var finding in audit.Findings)
            {
                var severity = finding.Severity.ToString();
                var rollup = finding.Severity is SecuritySeverity.Critical or SecuritySeverity.High
                    ? "Critical"
                    : "Warning";

                risks.Add(new SecurityRisk
                {
                    DeviceName = d.Name ?? d.IpAddress,
                    Reason = $"{finding.Id}::{finding.Severity}::{finding.Title}::{finding.Description}::{finding.Mitigation}",
                    Level = rollup,
                    DeviceId = d.Id.ToString(),
                    FindingId = finding.Id,
                    Title = finding.Title,
                    Description = finding.Description,
                    Mitigation = finding.Mitigation,
                    Severity = severity,
                });
            }
        }

        return risks
            .OrderByDescending(r => r.Level == "Critical")
            .ThenByDescending(r => r.Severity)
            .Take(count);
    }

    public DistributionModel GetDistribution(List<Device> devices)
    {
        return new DistributionModel
        {
            Vendors = devices.GroupBy(d => d.Vendor ?? "Unknown")
                             .Select(g => new LabelValueCount { Label = g.Key, Value = g.Count() })
                             .OrderByDescending(x => x.Value)
                             .Take(5)
                             .ToList(),
            Types = devices.GroupBy(d => d.Type ?? "Workstation")
                           .Select(g => new LabelValueCount { Label = g.Key, Value = g.Count() })
                           .OrderByDescending(x => x.Value)
                           .Take(5)
                           .ToList()
        };
    }

    public NetworkDeltasModel GetNetworkDeltas(List<Device> devices)
    {
        var threshold = _clock.Now.AddHours(-24);

        return new NetworkDeltasModel
        {
            NewDevices = devices.Where(d => d.FirstSeen > threshold)
                                .Select(d => new NewDeviceDelta { IpAddress = d.IpAddress, Name = d.Name, Vendor = d.Vendor, FirstSeen = d.FirstSeen, Id = d.Id.ToString() })
                                .ToList(),
            PersistentOffline = devices.Where(d => d.Status == "offline" && (d.LastSeen < threshold || d.LastSeen == DateTime.MinValue))
                                       .Select(d => new OfflineDeviceDelta { IpAddress = d.IpAddress, Name = d.Name, Vendor = d.Vendor, LastSeen = d.LastSeen, Id = d.Id.ToString() })
                                       .ToList()
        };
    }

    public List<ServiceStatsModel> GetServiceStats(List<Device> devices)
    {
        var ports = new Dictionary<int, string> {
            { 80, "Web" }, { 443, "Web TLS" }, { 22, "SSH" }, { 23, "Telnet" },
            { 5000, "StacksAtlas" }, { 8080, "Web Alt" }, { 8008, "Cast" },
            { 8009, "Cast TLS" }, { 62078, "Lockdown" }, { 554, "RTSP" }, { 161, "SNMP" }
        };

        return ports.Select(p => new ServiceStatsModel
        {
            Port = p.Key,
            Name = p.Value,
            Count = devices.Count(d => d.OpenPorts != null && d.OpenPorts.Contains(p.Key))
        })
        .Where(x => x.Count > 0)
        .OrderByDescending(x => x.Count)
        .ToList();
    }

    public HealthInsightsModel GetHealthInsights(List<Device> devices)
    {
        var lagging = devices.Where(d => d.AverageLatencyMs > 250)
            .OrderByDescending(d => d.AverageLatencyMs)
            .Select(d => {
                if (!_eventRepo.GetRecent(50).Any(e => e.DeviceIp == d.IpAddress && e.Type == "Performance" && e.Timestamp > _clock.Now.AddHours(-1)))
                {
                    _eventRepo.AddEvent(new SystemEvent {
                        DeviceIp = d.IpAddress,
                        Type = "Performance",
                        Severity = "Warning",
                        Message = $"High network latency detected on {d.Name}: {d.AverageLatencyMs:F1}ms",
                        Timestamp = _clock.Now
                    });
                }
                return new InsightEntry { Name = d.Name, IpAddress = d.IpAddress, Id = d.Id.ToString(), Value = $"{d.AverageLatencyMs:F1}ms", Label = "High Latency" };
            });

        return new HealthInsightsModel
        {
            Lagging = lagging.Take(3).ToList(),
            Flappers = devices.Where(d => d.FlapCount > 5)
                              .OrderByDescending(d => d.FlapCount)
                              .Take(3)
                              .Select(d => new InsightEntry { Name = d.Name, IpAddress = d.IpAddress, Id = d.Id.ToString(), Value = $"{d.FlapCount} Flaps", Label = "Connectivity Jitter" })
                              .ToList()
        };
    }

    private byte[] GenerateSparklineImage(List<long> history)
    {
        // Placeholder for "No Data" (Grey Flat Line)
        if (history == null || history.Count == 0) 
        {
            history = new List<long> { 0, 0 }; // Fake flat line
        }

        // FIX: If only 1 point, duplicate it so we can draw a flat line
        var dataPoints = new List<long>(history);
        if (dataPoints.Count == 1) dataPoints.Add(dataPoints[0]);

        // Fixed size for the image
        int width = 200;
        int height = 50;

        try 
        {
            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);
            
            // Clear background (transparent)
            canvas.Clear(SKColors.Transparent);

            bool isNoData = dataPoints.All(x => x == 0);
            
            using var paint = new SKPaint
            {
                Color = isNoData ? SKColors.LightGray : SKColor.Parse("#2196F3"),
                StrokeWidth = 3,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            };

            float maxVal = dataPoints.Max();
            float minVal = dataPoints.Min();
            float range = maxVal - minVal;
            if (range == 0) range = 1;

            using var path = new SKPath();
            float stepX = (float)width / (dataPoints.Count - 1);

            for (int i = 0; i < dataPoints.Count; i++)
            {
                float x = i * stepX;
                float val = dataPoints[i];
                float normalized = (val - minVal) / range;
                // Padding of 2px on int to avoid clipping
                float y = (height - 4) - (normalized * (height - 8)); 
                y += 2; // Offset

                if (i == 0) path.MoveTo(x, y);
                else path.LineTo(x, y);
            }

            canvas.DrawPath(path, paint);
            
            // Encode to PNG
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate sparkline image");
            return Array.Empty<byte>();
        }
    }

    private byte[] GeneratePieChartImage(Dictionary<string, int> data)
    {
        if (data == null || !data.Any()) return Array.Empty<byte>();

        int width = 400;
        int height = 300;
        int chartSize = 250;
        var colors = new[] { "#2196F3", "#4CAF50", "#FFC107", "#F44336", "#9C27B0", "#607D8B" };

        try
        {
            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);

            float total = data.Sum(x => x.Value);
            float startAngle = 0;
            var rect = new SKRect(0, (height - chartSize) / 2, chartSize, (height + chartSize) / 2);

            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            
            int colorIndex = 0;
            float legendY = 50;
            float legendX = chartSize + 20;

            foreach (var item in data)
            {
                float sweepAngle = (item.Value / total) * 360;
                paint.Color = SKColor.Parse(colors[colorIndex % colors.Length]);
                
                // Draw Arc
                using var path = new SKPath();
                path.MoveTo(rect.MidX, rect.MidY);
                path.ArcTo(rect, startAngle, sweepAngle, false);
                path.Close();
                canvas.DrawPath(path, paint);

                startAngle += sweepAngle;
                colorIndex++;
            }
            
            using var image = SKImage.FromBitmap(bitmap);
            using var encData = image.Encode(SKEncodedImageFormat.Png, 100);
            return encData.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate pie chart image");
            return Array.Empty<byte>();
        }
    }

    private byte[] GenerateGradeBadge(string grade)
    {
        int size = 60; 
        try
        {
            using var bitmap = new SKBitmap(size, size);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);

            // Professional Ring Design
            var colorHex = grade.StartsWith("A") ? "#4CAF50" : (grade.StartsWith("B") ? "#FF9800" : "#F44336");
            var color = SKColor.Parse(colorHex);

            // 1. Outer Ring (Thin)
            using var ringPaint = new SKPaint 
            { 
                Color = color, 
                IsAntialias = true, 
                Style = SKPaintStyle.Stroke, 
                StrokeWidth = 2 
            };
            canvas.DrawCircle(size/2, size/2, (size/2) - 2, ringPaint);
            
            // 2. Text
            using var textPaint = new SKPaint 
            { 
                Color = color, // Colored text instead of white-on-color
                IsAntialias = true, 
                TextSize = 28, 
                TextAlign = SKTextAlign.Center,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
            };
            
            var bounds = new SKRect();
            textPaint.MeasureText(grade, ref bounds);
            float y = (size/2) - bounds.MidY;

            canvas.DrawText(grade, size/2, y, textPaint);

            using var image = SKImage.FromBitmap(bitmap);
            using var encData = image.Encode(SKEncodedImageFormat.Png, 100);
            return encData.ToArray();
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }
}
