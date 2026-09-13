using System;
using System.Collections.Generic;
using LiteDB;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Abstractions;

namespace StacksAtlas.Core.Database;

public class WebhookRepository(ILiteDatabase db, ILogger<WebhookRepository> logger, IClock clock)
{
    private readonly ILiteCollection<WebhookConfiguration> _collection = db.GetCollection<WebhookConfiguration>("webhooks");

    public List<WebhookConfiguration> GetAll()
    {
        try
        {
            return _collection.FindAll().ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve all webhooks.");
            return new List<WebhookConfiguration>();
        }
    }

    public List<WebhookConfiguration> GetActive()
    {
        try
        {
            return _collection.Find(x => x.Enabled && x.Status != WebhookStatus.Disabled).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve active webhooks.");
            return new List<WebhookConfiguration>();
        }
    }

    public WebhookConfiguration? GetById(Guid id)
    {
        try
        {
            return _collection.FindById(id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve webhook with ID: {Id}", id);
            return null;
        }
    }

    public void Upsert(WebhookConfiguration config)
    {
        try
        {
            config.LastUpdatedAt = clock.UtcNow;
            _collection.Upsert(config);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upsert webhook configuration: {Name}", config.Name);
            throw;
        }
    }

    public void Delete(Guid id)
    {
        try
        {
            _collection.Delete(id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete webhook configuration: {Id}", id);
            throw;
        }
    }

    public void UpdateStatus(Guid id, WebhookStatus status, string? errorMessage = null)
    {
        try
        {
            var config = _collection.FindById(id);
            if (config != null)
            {
                config.Status = status;
                config.LastErrorMessage = errorMessage;
                config.LastUpdatedAt = clock.UtcNow;
                
                if (status == WebhookStatus.CircuitOpen)
                {
                    config.CircuitResetAt = clock.UtcNow.AddMinutes(30);
                }
                else if (status == WebhookStatus.Active)
                {
                    config.FailureCount = 0;
                    config.CircuitResetAt = null;
                }

                _collection.Update(config);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update status for webhook: {Id}", id);
        }
    }

    public void IncrementFailure(Guid id, string errorMessage)
    {
        try
        {
            var config = _collection.FindById(id);
            if (config != null)
            {
                config.FailureCount++;
                config.LastFailureAt = clock.UtcNow;
                config.LastErrorMessage = errorMessage;
                config.LastUpdatedAt = clock.UtcNow;

                if (config.FailureCount >= 5)
                {
                    config.Status = WebhookStatus.CircuitOpen;
                    config.CircuitResetAt = clock.UtcNow.AddMinutes(30);
                    logger.LogWarning("Webhook Circuit Opened for {Name} due to 5 consecutive failures.", config.Name);
                }

                _collection.Update(config);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to increment failure for webhook: {Id}", id);
        }
    }

}
