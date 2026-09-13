namespace StacksAtlas.Core.Models;

public class EmailSettings
{
    public int Id { get; set; } = 1;
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = ""; // Encrypted in DB
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "StacksAtlas Alerts";
    public bool Enabled { get; set; } = false;
    public DateTime LastUpdatedUtc { get; set; }
}
