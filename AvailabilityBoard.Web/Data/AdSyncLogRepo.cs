using Dapper;

namespace AvailabilityBoard.Web.Data;

public sealed class AdSyncLogRepo
{
    private readonly string _cs;
    public AdSyncLogRepo(string cs) => _cs = cs;

    public async Task<int> StartSync()
    {
        using var cn = Db.Open(_cs);
        return await cn.ExecuteScalarAsync<int>(
            @"INSERT INTO dbo.AdSyncLogs (StartedAt, Status)
              OUTPUT INSERTED.SyncId
              VALUES (SYSUTCDATETIME(), 'Running')");
    }

    public async Task CompleteSync(int syncId, bool success, int added, int updated, int deactivated, string? error)
    {
        using var cn = Db.Open(_cs);
        await cn.ExecuteAsync(
            @"UPDATE dbo.AdSyncLogs 
              SET CompletedAt = SYSUTCDATETIME(),
                  Status = @status,
                  UsersAdded = @added,
                  UsersUpdated = @updated,
                  UsersDeactivated = @deactivated,
                  ErrorMessage = @error
              WHERE SyncId = @syncId",
            new
            {
                syncId,
                status = success ? "Success" : "Failed",
                added,
                updated,
                deactivated,
                error
            });
    }

    public async Task<dynamic?> GetLastSync()
    {
        using var cn = Db.Open(_cs);
        return await cn.QuerySingleOrDefaultAsync(
            @"SELECT TOP 1 SyncId, StartedAt, CompletedAt, Status, 
                     UsersAdded, UsersUpdated, UsersDeactivated, ErrorMessage
              FROM dbo.AdSyncLogs
              ORDER BY SyncId DESC");
    }

    public async Task<List<dynamic>> GetRecentSyncs(int top = 20)
    {
        using var cn = Db.Open(_cs);
        var rows = await cn.QueryAsync(
            @"SELECT TOP (@top) SyncId, StartedAt, CompletedAt, Status,
                     UsersAdded, UsersUpdated, UsersDeactivated, ErrorMessage
              FROM dbo.AdSyncLogs
              ORDER BY SyncId DESC",
            new { top });
        return rows.ToList();
    }
}
