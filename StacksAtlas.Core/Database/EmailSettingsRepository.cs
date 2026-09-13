using System;
using LiteDB;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Database;

/// <summary>
/// Repository for managing the singleton SMTP configuration in LiteDB.
/// </summary>
public class EmailSettingsRepository(LiteDatabase db, ILogger<EmailSettingsRepository> logger, IClock clock)
{
    private readonly ILiteCollection<EmailSettings> _collection = SetupCollection(db);

    private static ILiteCollection<EmailSettings> SetupCollection(LiteDatabase database)
    {
        return database.GetCollection<EmailSettings>("email_settings");
    }

    public EmailSettings? GetSettings()
    {
        try
        {
            return _collection.FindById(1);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve EmailSettings from database.");
            return null;
        }
    }

    public void SaveSettings(EmailSettings settings)
    {
        try
        {
            // Enforce the singleton pattern
            settings.Id = 1;
            
            // Architectural Audit: Track last modification for diagnostic supportability
            settings.LastUpdatedUtc = clock.UtcNow;

            _collection.Upsert(1, settings);
        }
        catch (Exception ex)
        {
            // Security Guard: We log the SMTP Host but never the Password or Username
            logger.LogError(ex, "Failed to persist EmailSettings for SMTP Host: {SmtpHost}", settings.SmtpHost);
        }
    }
}
