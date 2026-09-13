using System.Collections.Generic;

namespace StacksAtlas.Core.Models;

public class UserImportResult
{
    public bool Success { get; set; }
    public int ImportedCount { get; set; }
    public int CollisionCount { get; set; }
    public string? Message { get; set; }
    public List<string> CollidedUsernames { get; set; } = new();
}
