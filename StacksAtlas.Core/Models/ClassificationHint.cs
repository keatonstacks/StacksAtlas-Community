namespace StacksAtlas.Core.Models;

/// <summary>
/// Represents a single piece of evidence contributing to the classification of a device.
/// </summary>
public record ClassificationHint(
    DeviceType Type,
    string? Vendor,
    string? Model,
    int Confidence,
    string Source
);
