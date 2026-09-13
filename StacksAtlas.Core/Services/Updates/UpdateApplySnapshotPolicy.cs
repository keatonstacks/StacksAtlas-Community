namespace StacksAtlas.Core.Services.Updates;

public static class UpdateApplySnapshotPolicy
{
    public static bool ShouldCreatePreUpdateSnapshot(string artifactKey) =>
        artifactKey != UpdateArtifactKeys.WinX64Portable;
}
