using Microsoft.EntityFrameworkCore;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Data.Hub;
using StacksAtlas.Core.Abstractions;

namespace StacksAtlas.Core.Data.Hub;

public class SqliteFederatedNodeRepository : IFederatedNodeRepository
{
    private readonly IDbContextFactory<HubDbContext> _contextFactory;
    private readonly IClock _clock;

    public SqliteFederatedNodeRepository(IDbContextFactory<HubDbContext> contextFactory, IClock clock)
    {
        _contextFactory = contextFactory;
        _clock = clock;
    }

    public FederatedNode? GetById(string id)
    {
        using var context = _contextFactory.CreateDbContext();
        return context.Nodes.AsNoTracking().FirstOrDefault(n => n.Id == id);
    }

    public FederatedNode? GetByHardwareId(string hardwareId)
    {
        if (string.IsNullOrWhiteSpace(hardwareId))
            return null;

        using var context = _contextFactory.CreateDbContext();
        return context.Nodes.AsNoTracking()
            .FirstOrDefault(n => n.HardwareId == hardwareId);
    }

    public List<FederatedNode> GetAll()
    {
        using var context = _contextFactory.CreateDbContext();
        return context.Nodes.AsNoTracking().ToList();
    }

    public void UpsertNode(FederatedNode node)
    {
        int retries = 3;
        while (retries > 0)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var existing = context.Nodes.FirstOrDefault(n => n.Id == node.Id);
                
                if (existing != null)
                {
                    existing.Name = node.Name;
                    existing.IPAddress = node.IPAddress;
                    existing.Status = node.Status;
                    existing.LastSeenUtc = node.LastSeenUtc;
                    existing.Version = node.Version;
                    existing.OS = node.OS;
                    existing.LicenseTier = node.LicenseTier;
                    existing.ConnectionId = node.ConnectionId;
                    existing.HttpPort = node.HttpPort;
                    existing.HttpsPort = node.HttpsPort;
                    existing.LastSyncUtc = node.LastSyncUtc;
                    
                    // Hierarchy Sync
                    existing.Client = node.Client;
                    existing.Building = node.Building;
                    existing.Room = node.Room;
                    existing.SyncUsers = node.SyncUsers;
                    existing.SyncUserRegistry = node.SyncUserRegistry;
                    existing.SyncSsoSettings = node.SyncSsoSettings;
                    existing.SyncAlertSettings = node.SyncAlertSettings;
                    existing.SyncSiemSettings = node.SyncSiemSettings;
                    existing.ScanSettingsJson = node.ScanSettingsJson;
                    existing.IsIdentityImported = node.IsIdentityImported;
                    existing.DatabaseSize = node.DatabaseSize;
                    existing.HardwareId = node.HardwareId;

                    context.Nodes.Update(existing);
                }
                else
                {
                    context.Nodes.Add(node);
                }
                
                context.SaveChanges();
                return; // Success
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (SqliteConcurrency.IsLockError(ex) && retries > 1)
            {
                retries--;
                Task.Delay(100 * (3 - retries)).Wait(); // Incremental backoff
            }
            catch (Exception)
            {
                throw; // Rethrow other exceptions
            }
        }
    }

    public bool Delete(string id)
    {
        using var context = _contextFactory.CreateDbContext();
        var node = context.Nodes.FirstOrDefault(n => n.Id == id);
        if (node == null) return false;
        
        context.Nodes.Remove(node);
        context.SaveChanges();
        return true;
    }
}
