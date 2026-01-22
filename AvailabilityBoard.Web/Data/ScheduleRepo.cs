using Dapper;

namespace AvailabilityBoard.Web.Data;

public sealed record EmployeeScheduleBlockRow(
    long ScheduleBlockId,
    int EmployeeId,
    DateTime ScheduleDate,
    int TypeId,
    string TypeCode,
    string TypeLabel,
    string? ColorHex,
    TimeSpan? StartTime,
    TimeSpan? EndTime,
    string? CustomerName,
    string? OutActivity,
    string? Note,
    int UpdatedByEmployeeId,
    DateTime UpdatedAt
);

public sealed class ScheduleRepo
{
    private readonly string _cs;
    public ScheduleRepo(string cs) => _cs = cs;

    public async Task<List<EmployeeScheduleBlockRow>> GetDayBlocks(int employeeId, DateTime date)
    {
        using var cn = Db.Open(_cs);
        var d = date.Date;

        var rows = await cn.QueryAsync<EmployeeScheduleBlockRow>(@"
SELECT b.ScheduleBlockId,
       b.EmployeeId,
       CAST(b.ScheduleDate AS datetime2) AS ScheduleDate,
       b.TypeId,
       t.Code AS TypeCode,
       t.Label AS TypeLabel,
       t.ColorHex AS ColorHex,
       b.StartTime,
       b.EndTime,
       b.CustomerName,
       b.OutActivity,
       b.Note,
       b.UpdatedByEmployeeId,
       b.UpdatedAt
FROM dbo.EmployeeScheduleBlocks b
JOIN dbo.AvailabilityTypes t ON t.TypeId = b.TypeId
WHERE b.EmployeeId = @employeeId
  AND b.ScheduleDate = @d
ORDER BY
  CASE WHEN b.StartTime IS NULL THEN 0 ELSE 1 END,
  b.StartTime,
  b.EndTime,
  b.ScheduleBlockId;
", new { employeeId, d });

        return rows.ToList();
    }

    public async Task ReplaceDayBlocks(
        int employeeId,
        DateTime date,
        IEnumerable<(int TypeId, TimeSpan? StartTime, TimeSpan? EndTime, string? CustomerName, string? OutActivity, string? Note)> blocks,
        int updatedByEmployeeId)
    {
        using var cn = Db.Open(_cs);
        using var tx = cn.BeginTransaction();
        var d = date.Date;

        // Replace the day's blocks atomically
        await cn.ExecuteAsync(
            @"DELETE FROM dbo.EmployeeScheduleBlocks WHERE EmployeeId=@employeeId AND ScheduleDate=@d;",
            new { employeeId, d }, tx);

        const string insertSql = @"
INSERT INTO dbo.EmployeeScheduleBlocks
    (EmployeeId, ScheduleDate, TypeId, StartTime, EndTime, CustomerName, OutActivity, Note, UpdatedByEmployeeId)
VALUES
    (@employeeId, @d, @typeId, @startTime, @endTime, @customerName, @outActivity, @note, @updatedByEmployeeId);
";

        foreach (var b in blocks)
        {
            await cn.ExecuteAsync(insertSql, new
            {
                employeeId,
                d,
                typeId = b.TypeId,
                startTime = b.StartTime,
                endTime = b.EndTime,
                customerName = string.IsNullOrWhiteSpace(b.CustomerName) ? null : b.CustomerName.Trim(),
                outActivity = string.IsNullOrWhiteSpace(b.OutActivity) ? null : b.OutActivity.Trim(),
                note = string.IsNullOrWhiteSpace(b.Note) ? null : b.Note.Trim(),
                updatedByEmployeeId
            }, tx);
        }

        tx.Commit();
    }

    public async Task DeleteDay(int employeeId, DateTime date)
    {
        using var cn = Db.Open(_cs);
        var d = date.Date;

        await cn.ExecuteAsync(
            @"DELETE FROM dbo.EmployeeScheduleBlocks WHERE EmployeeId=@employeeId AND ScheduleDate=@d",
            new { employeeId, d });
    }
}
