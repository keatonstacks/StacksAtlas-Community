namespace StacksAtlas.Core.Models;

public class InterfaceRoleMapping
{
    public string InterfaceId { get; set; } = string.Empty;
    public NetworkRole Role { get; set; } = NetworkRole.Default;
}
