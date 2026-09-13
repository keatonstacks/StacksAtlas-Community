namespace StacksAtlas.Core.Models;

public record SystemRuntimeInfo(
    bool IsPortable,
    string InstallMode,
    string DataDirectory,
    string? PromoteHint);
