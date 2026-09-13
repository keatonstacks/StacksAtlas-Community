using System;
using System.Collections.Generic;
using System.Linq;

namespace StacksAtlas.Core.Models;

public enum SecuritySeverity
{
    Critical = 40,
    High = 20,
    Medium = 10,
    Low = 5,
    Info = 0
}

public record SecurityIssue(
    string Id, 
    string Title, 
    string Description, 
    SecuritySeverity Severity, 
    string Category,
    string Mitigation
);

public class SecurityAuditResult
{
    public SecurityGrade Grade { get; set; } = SecurityGrade.Green;
    public int Score { get; set; } = 100;
    public List<SecurityIssue> Findings { get; set; } = new();
    
    // Stable storage key for ignore/ack APIs; UI parses via parseSecurityFinding().
    public List<string> Issues => Findings.Select(FormatIssue).ToList();

    private static string FormatIssue(SecurityIssue f) =>
        $"{f.Id}::{f.Severity}::{f.Title}::{f.Description}::{f.Mitigation}";
}
