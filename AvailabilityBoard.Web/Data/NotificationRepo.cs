using Dapper;

namespace AvailabilityBoard.Web.Data;

public sealed class NotificationRepo
{
    private readonly string _cs;
    public NotificationRepo(string cs) => _cs = cs;

    public async Task Add(int toEmployeeId, string title, string body, string? url)
    {
        using var cn = Db.Open(_cs);
        await cn.ExecuteAsync(
            @"INSERT INTO dbo.Notifications(ToEmployeeId, Title, Body, Url)
              VALUES(@toEmployeeId, @title, @body, @url)",
            new { toEmployeeId, title, body, url });
    }

    public async Task<int> GetUnreadCount(int toEmployeeId)
    {
        using var cn = Db.Open(_cs);
        return await cn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.Notifications WHERE ToEmployeeId=@toEmployeeId AND IsRead=0",
            new { toEmployeeId });
    }

    public async Task<List<dynamic>> GetLatest(int toEmployeeId, int top = 20)
    {
        using var cn = Db.Open(_cs);
        var rows = await cn.QueryAsync(
            @"SELECT TOP (@top) NotificationId, Title, Body, Url, IsRead, CreatedAt
              FROM dbo.Notifications
              WHERE ToEmployeeId=@toEmployeeId
              ORDER BY CreatedAt DESC",
            new { toEmployeeId, top });
        return rows.ToList();
    }

    public async Task MarkRead(long notificationId, int toEmployeeId)
    {
        using var cn = Db.Open(_cs);
        await cn.ExecuteAsync(
            @"UPDATE dbo.Notifications SET IsRead=1
              WHERE NotificationId=@notificationId AND ToEmployeeId=@toEmployeeId",
            new { notificationId, toEmployeeId });
    }
}
