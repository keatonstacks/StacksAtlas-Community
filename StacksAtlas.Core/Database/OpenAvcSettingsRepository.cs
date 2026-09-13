using LiteDB;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Security;

namespace StacksAtlas.Core.Database;

public class OpenAvcSettingsRepository(
    LiteDatabase db,
    SettingsEncryptor encryptor,
    ILogger<OpenAvcSettingsRepository> logger,
    IClock clock)
{
    private readonly ILiteCollection<OpenAvcSettings> _collection = db.GetCollection<OpenAvcSettings>("openavc_settings");

    public OpenAvcSettings GetSettings()
    {
        try
        {
            var settings = _collection.FindById(1) ?? new OpenAvcSettings();
            settings.Password = encryptor.Unprotect(settings.Password);
            return settings;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load OpenAVC settings.");
            return new OpenAvcSettings();
        }
    }

    public void SaveSettings(OpenAvcSettings settings, string? existingPlainPassword = null)
    {
        try
        {
            settings.Id = 1;
            settings.LastUpdatedUtc = clock.UtcNow;

            var toStore = new OpenAvcSettings
            {
                Id = 1,
                Enabled = settings.Enabled,
                BaseUrl = settings.BaseUrl.Trim().TrimEnd('/'),
                Username = settings.Username.Trim(),
                Password = encryptor.Protect(
                    string.IsNullOrEmpty(settings.Password) || settings.Password == "********"
                        ? existingPlainPassword ?? string.Empty
                        : settings.Password),
                LastUpdatedUtc = settings.LastUpdatedUtc,
            };

            _collection.Upsert(1, toStore);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist OpenAVC settings for host {BaseUrl}", settings.BaseUrl);
            throw;
        }
    }
}
