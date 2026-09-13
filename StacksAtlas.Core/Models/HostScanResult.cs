using System;
using System.Collections.Generic;

namespace StacksAtlas.Core.Models;

/// <summary>
/// Represents the fingerprint and status of a network host.
/// </summary>
public record HostScanResult(
    string Ip,
    bool IsOnline,
    long RoundtripTimeMs,
    string? Mac,
    string? Vendor,
    string? Hostname,
    string? Name,   // Added to fix CS1061 in PingWorker
    string? Type,
    string? Model,
    int ConfidenceScore,
    List<int> OpenPorts,
    string? Error,
    string? SnmpSysName = null,
    string? SnmpSysDescr = null,
    string? HttpTitle = null,
    DateTime? SnmpLastCheck = null
)
{
    /// <summary>
    /// Convenience helper to create an offline result. 
    /// Updated to include the new Name parameter.
    /// </summary>
    public static HostScanResult Offline(string ip) =>
        new(
            Ip: ip,
            IsOnline: false,
            RoundtripTimeMs: -1,
            Mac: null,
            Vendor: null,
            Hostname: null,
            Name: null,      
            Type: null,
            Model: null,
            ConfidenceScore: 0,
            OpenPorts: [],
            Error: null,
            SnmpSysName: null,
            SnmpSysDescr: null,
            HttpTitle: null,
            SnmpLastCheck: null
        );

    public static HostScanResult Online(string ip, long rtt, string? mac, string? vendor, string? hostname) =>
        new(
            Ip: ip,
            IsOnline: true,
            RoundtripTimeMs: rtt,
            Mac: mac,
            Vendor: vendor,
            Hostname: hostname,
            Name: hostname ?? ip,
            Type: "Unknown",
            Model: null,
            ConfidenceScore: 0,
            OpenPorts: [],
            Error: null
        );
}
