using StacksAtlas.Core.Models;
using System.Linq;

namespace StacksAtlas.Core.Services.Scanning;

public class SecurityAuditService
{
    /// <summary>
    /// Analyzes a device's open ports and metadata to determine its security hygiene grade and score.
    /// </summary>
    public SecurityAuditResult AuditDevice(Device device)
    {
        var result = new SecurityAuditResult();
        int totalPenalty = 0;

        string[] lenientTypes = { "CRESTR_AV", "QSYS_CORE", "AV_CONTROLLER", "Gaming", "IoT", "Printer" };
        string[] lenientBrands = { "Crestron", "Q-SYS", "Extron", "Biamp", "Sony", "Samsung", "LG", "HP", "Epson", "Canon", "Brother" };

        bool isLenient = lenientTypes.Any(t => (device.Type ?? "").Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                         lenientBrands.Any(b => (device.Vendor ?? "").Contains(b, StringComparison.OrdinalIgnoreCase));

        // 🔴 CRITICAL RISKS (Severity: Critical)
        if (device.OpenPorts.Contains(23))
        {
            AddIssue(result, ref totalPenalty, new SecurityIssue(
                "SA-001", "Unencrypted Telnet Enabled", 
                "Telnet transmits data in plaintext, exposing credentials to anyone on the network.", 
                SecuritySeverity.Critical, "Protocol",
                "Disable Telnet and use SSH for remote administration. Block port 23 at the network edge if the device cannot be reconfigured."));
        }
        
        if (device.OpenPorts.Contains(21))
        {
            AddIssue(result, ref totalPenalty, new SecurityIssue(
                "SA-002", "Insecure FTP Enabled", 
                "FTP is an unencrypted protocol. Use SFTP or HTTPS for secure file transfers.", 
                SecuritySeverity.High, "Protocol",
                "Disable FTP and switch to SFTP, FTPS, or HTTPS. Restrict port 21 to management VLANs only."));
        }

        // 🟡 WARNINGS / CONDITIONAL RISKS
        if (device.OpenPorts.Contains(80) && !device.OpenPorts.Contains(443))
        {
            if (isLenient)
            {
                AddIssue(result, ref totalPenalty, new SecurityIssue(
                    "SA-003", "Plaintext HTTP Management", 
                    "Allowable for this AV/IoT device type, but HTTPS is preferred if available.", 
                    SecuritySeverity.Medium, "Management",
                    "Enable HTTPS on the device if supported. Otherwise isolate the management interface on a dedicated control VLAN."));
            }
            else
            {
                AddIssue(result, ref totalPenalty, new SecurityIssue(
                    "SA-003", "Unencrypted HTTP Interface", 
                    "The web interface is not encrypted. Credentials and data can be intercepted.", 
                    SecuritySeverity.High, "Management",
                    "Enable HTTPS/TLS on the management interface or restrict HTTP access to trusted management networks only."));
            }
        }

        // 🕒 LEGACY SYSTEMS
        if (IsLegacyOs(device))
        {
            AddIssue(result, ref totalPenalty, new SecurityIssue(
                "SA-004", "End-of-Life Operating System", 
                $"Legacy OS detected ({device.OperatingSystem ?? device.Model}). These systems no longer receive security patches.", 
                SecuritySeverity.Critical, "OS",
                "Plan replacement or network isolation. Do not expose EOL systems to untrusted networks or the internet."));
        }

        // 📹 PRIVACY & EXPOSURE RISKS
        if (device.OpenPorts.Contains(554) && device.Type != "Camera" && device.Type != "NVR")
        {
            AddIssue(result, ref totalPenalty, new SecurityIssue(
                "SA-005", "Unauthenticated RTSP Stream", 
                "Real-Time Streaming Protocol (Port 554) detected. Ensure streams are authenticated to prevent unauthorized viewing.", 
                SecuritySeverity.Medium, "Privacy",
                "Require authentication on RTSP streams and restrict port 554 to authorized viewers or VLANs."));
        }

        if (device.OpenPorts.Contains(5900) || device.OpenPorts.Contains(3389))
        {
            if (device.Type != "Workstation" && device.Type != "Server")
            {
                AddIssue(result, ref totalPenalty, new SecurityIssue(
                    "SA-006", "Remote Desktop Exposure", 
                    "Remote management ports (VNC/RDP) are open on a non-workstation device. Ensure these are intentional and secured.", 
                    SecuritySeverity.High, "Exposure",
                    "Confirm remote access is required. Use strong passwords, MFA where available, and firewall rules limiting source IPs."));
            }
        }

        if (device.OpenPorts.Contains(161))
        {
             AddIssue(result, ref totalPenalty, new SecurityIssue(
                "SA-007", "SNMP Service Exposed", 
                "SNMP is often configured with default community strings (public/private). Exposure can leak network topology.", 
                SecuritySeverity.Low, "Management",
                "Disable SNMP if unused. Otherwise use SNMPv3 and change default community strings immediately."));
        }

        // 🧠 Deep Scan Intelligence Merge
        if (device.DeepScanIssues?.Any() == true)
        {
            foreach (var deepIssue in device.DeepScanIssues)
            {
                AddIssue(result, ref totalPenalty, new SecurityIssue(
                    "SA-DEEP", "Intelligence Finding", 
                    deepIssue, 
                    SecuritySeverity.High, "Intelligence",
                    "Review the deep-scan finding and apply vendor guidance. Re-scan after remediation to confirm the exposure is closed."));
            }
        }

        // 🧹 FILTER IGNORED ISSUES & RE-CALCULATE
        if (device.IgnoredSecurityIssues?.Any() == true)
        {
            foreach (var ignored in device.IgnoredSecurityIssues)
            {
                var found = result.Findings.FirstOrDefault(f => 
                    string.Equals(f.Id, ignored.Trim(), StringComparison.OrdinalIgnoreCase) || 
                    string.Equals(f.Title, ignored.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    ignored.Contains(f.Id, StringComparison.OrdinalIgnoreCase));
                
                if (found != null)
                {
                    result.Findings.Remove(found);
                    // Regaining points for ignored issues? 
                    // Usually "Ignore" means "I accept this risk", so we restore the score.
                    totalPenalty -= (int)found.Severity;
                }
            }
        }

        // Final Grade & Score Logic
        result.Score = Math.Max(0, 100 - totalPenalty);
        
        // Only trigger RED if we have a Critical issue OR the score is catastrophically low (< 60)
        if (result.Findings.Any(f => f.Severity == SecuritySeverity.Critical) || result.Score < 60) 
        {
            result.Grade = SecurityGrade.Red;
        }
        // Trigger YELLOW if we have High/Medium issues OR the score is below 90
        else if (result.Findings.Any(f => f.Severity >= SecuritySeverity.Medium) || result.Score < 90) 
        {
            result.Grade = SecurityGrade.Yellow;
        }
        else 
        {
            result.Grade = SecurityGrade.Green;
        }

        return result;
    }

    private void AddIssue(SecurityAuditResult result, ref int totalPenalty, SecurityIssue issue)
    {
        result.Findings.Add(issue);
        totalPenalty += (int)issue.Severity;
    }

    private bool IsLegacyOs(Device device)
    {
        string[] legacyKeywords = { "Windows XP", "Windows 7", "Windows Server 2003", "Windows Server 2008" };
        var source = (device.OperatingSystem ?? device.Model ?? "").ToLowerInvariant();
        return legacyKeywords.Any(k => source.Contains(k.ToLowerInvariant()));
    }
}
