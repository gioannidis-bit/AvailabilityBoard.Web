using Dapper;

namespace AvailabilityBoard.Web.Data;

public sealed record EmployeeScheduleRow(
    long ScheduleId,
    int EmployeeId,
    DateTime ScheduleDate,
    int TypeId,
    string TypeCode,
    string TypeLabel,
    string? ColorHex,
    string? Note,
    int UpdatedByEmployeeId,
    DateTime UpdatedAt
);

public sealed class ScheduleRepo
{
    private readonly string _cs;
    public ScheduleRepo(string cs) => _cs = cs;

    public async Task<EmployeeScheduleRow?> GetDay(int employeeId, DateTime date)
    {
        using var cn = Db.Open(_cs);
        var d = date.Date;

        return await cn.QuerySingleOrDefaultAsync<EmployeeScheduleRow>(@"
SELECT s.ScheduleId,
       s.EmployeeId,
       CAST(s.ScheduleDate AS datetime2) AS ScheduleDate,
       s.TypeId,
       t.Code AS TypeCode,
       t.Label AS TypeLabel,
       t.ColorHex AS ColorHex,
       s.Note,
       s.UpdatedByEmployeeId,
       s.UpdatedAt
FROM dbo.EmployeeSchedules s
JOIN dbo.AvailabilityTypes t ON t.TypeId = s.TypeId
WHERE s.EmployeeId = @employeeId AND s.ScheduleDate = @d
", new { employeeId, d });
    }

    public async Task Upsert(int employeeId, DateTime date, int typeId, string? note, int updatedByEmployeeId)
    {
        using var cn = Db.Open(_cs);
        var d = date.Date;

        await cn.ExecuteAsync(@"
MERGE dbo.EmployeeSchedules AS target
USING (SELECT @employeeId AS EmployeeId, @d AS ScheduleDate) AS src
ON (target.EmployeeId = src.EmployeeId AND target.ScheduleDate = src.ScheduleDate)
WHEN MATCHED THEN
    UPDATE SET TypeId=@typeId,
               Note=@note,
               UpdatedByEmployeeId=@updatedByEmployeeId,
               UpdatedAt=SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (EmployeeId, ScheduleDate, TypeId, Note, UpdatedByEmployeeId)
    VALUES (@employeeId, @d, @typeId, @note, @updatedByEmployeeId);
", new { employeeId, d, typeId, note, updatedByEmployeeId });
    }

    public async Task Delete(int employeeId, DateTime date)
    {
        using var cn = Db.Open(_cs);
        var d = date.Date;

        await cn.ExecuteAsync(
            @"DELETE FROM dbo.EmployeeSchedules
              WHERE EmployeeId=@employeeId AND ScheduleDate=@d",
            new { employeeId, d });
    }
}
