namespace StacksAtlas.Core.Models;

public class OpenAvcSettings
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://127.0.0.1:8080";
    public string Username { get; set; } = "";
    /// <summary>Encrypted at rest (ENC: prefix via SettingsEncryptor).</summary>
    public string Password { get; set; } = "";
    public DateTime LastUpdatedUtc { get; set; }
}
