using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;

namespace StacksAtlas.API.Services;

/// <summary>
/// Resolves an enrolled Node from the mTLS client certificate on Hub port 5002.
/// Mirrors FederationHub AuthenticateNode CN={nodeId} enrollment identity.
/// </summary>
public static class FederatedNodeCertificateAuth
{
    public static bool TryResolveEnrolledNode(
        HttpContext httpContext,
        IFederatedNodeRepository nodeRepo,
        out FederatedNode node,
        out string? failureMessage)
    {
        node = null!;
        failureMessage = null;

        var cert = httpContext.Connection.ClientCertificate;
        if (cert is null)
        {
            failureMessage = "Client certificate required. Connect via Hub mTLS port 5002.";
            return false;
        }

        var nodeId = TryGetCommonName(cert);
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            failureMessage = "Client certificate subject does not contain a Node id (CN).";
            return false;
        }

        var existing = nodeRepo.GetById(nodeId);
        if (existing is null)
        {
            failureMessage = "Node is not enrolled on this Hub.";
            return false;
        }

        if (existing.IsRevoked)
        {
            failureMessage = "Node certificate has been revoked.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(existing.CertificateSerialNumber))
        {
            var serial = cert.SerialNumber?.Replace(" ", "", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(serial, existing.CertificateSerialNumber, StringComparison.OrdinalIgnoreCase))
            {
                failureMessage = "Client certificate serial does not match the enrolled Node.";
                return false;
            }
        }

        node = existing;
        return true;
    }

    public static string? TryGetCommonName(X509Certificate2 cert)
    {
        var subject = cert.Subject ?? "";
        foreach (var part in subject.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                return part[3..].Trim();
        }

        return null;
    }
}
