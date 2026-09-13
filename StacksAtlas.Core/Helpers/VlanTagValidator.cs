namespace StacksAtlas.Core.Helpers;

public static class VlanTagValidator
{
    public const int MaxLabelLength = 32;

    /// <summary>
    /// Validates operator VLAN tags: numeric values must be 1-4094; labels are limited to 32 characters.
    /// </summary>
    public static bool TryNormalize(string? tag, out string? normalized, out string? errorMessage)
    {
        normalized = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(tag))
            return true;

        normalized = tag.Trim();
        if (normalized.Length > MaxLabelLength)
        {
            errorMessage = $"VLAN label cannot exceed {MaxLabelLength} characters.";
            return false;
        }

        if (normalized.All(char.IsDigit))
        {
            if (!int.TryParse(normalized, out var vlanId) || vlanId is < 1 or > 4094)
            {
                errorMessage = "Numeric VLAN IDs must be between 1 and 4094.";
                return false;
            }
        }

        return true;
    }
}
