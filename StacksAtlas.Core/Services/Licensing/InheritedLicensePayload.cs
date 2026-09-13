namespace StacksAtlas.Core.Services.Licensing;

public record InheritedLicensePayload(
    string NodeId,
    string LicenseTier,
    long TimestampUnix
);
