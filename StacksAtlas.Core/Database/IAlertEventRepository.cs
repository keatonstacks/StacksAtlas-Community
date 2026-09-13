using System.Collections.Generic;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Database;

public interface IAlertEventRepository
{
    void LogAlert(AlertEvent alertEvent, Device? device = null, bool isDelegation = false);
    IEnumerable<AlertEvent> GetRecentAlerts(int limit = 50, string[]? nodeIds = null);
    List<AlertEvent> GetAlertsByUser(string email, int limit = 50);
    bool DeleteAlert(string id);
    int ClearHistory(string[]? nodeIds = null);
}
