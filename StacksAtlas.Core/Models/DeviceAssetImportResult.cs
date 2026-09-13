namespace StacksAtlas.Core.Models;

public class DeviceAssetImportResult
{
    public int Updated { get; set; }
    public int NotFound { get; set; }
    public int Failed { get; set; }
    public int SkippedOpenAvc { get; set; }
    public List<Guid> UpdatedDeviceIds { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}
