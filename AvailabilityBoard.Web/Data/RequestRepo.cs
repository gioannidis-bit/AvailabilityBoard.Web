using Dapper;

namespace AvailabilityBoard.Web.Data;

public sealed class RequestRepo
{
    private readonly string _cs;
    public RequestRepo(string cs) => _cs = cs;

    public async Task<long> CreateRequest(int employeeId, int typeId, DateTime start, DateTime end, string? note, int? approverEmployeeId)
    {
        if (end <= start) throw new ArgumentException("End must be after Start");

        using var cn = Db.Open(_cs);
        var id = await cn.ExecuteScalarAsync<long>(
            @"INSERT INTO dbo.AvailabilityRequests(EmployeeId, TypeId, StartDateTime, EndDateTime, Note, Status, ApproverEmployeeId)
              OUTPUT INSERTED.RequestId
              VALUES(@employeeId, @typeId, @start, @end, @note, 'Pending', @approverEmployeeId)",
            new { employeeId, typeId, start, end, note, approverEmployeeId });

        return id;
    }

    public async Task<List<RequestRow>> GetMyRequests(int employeeId)
    {
        using var cn = Db.Open(_cs);
        var rows = await cn.QueryAsync<RequestRow>(
            @"SELECT r.RequestId, r.EmployeeId,
                     e.DisplayName AS EmployeeName,
                     r.TypeId, t.Code AS TypeCode, t.Label AS TypeLabel,
                     r.StartDateTime, r.EndDateTime, r.Status, r.Note,
                     r.ApproverEmployeeId, a.DisplayName AS ApproverName,
                     r.SubmittedAt, r.DecisionAt, r.DecisionNote
              FROM dbo.AvailabilityRequests r
              JOIN dbo.Employees e ON e.EmployeeId=r.EmployeeId
              JOIN dbo.AvailabilityTypes t ON t.TypeId=r.TypeId
              LEFT JOIN dbo.Employees a ON a.EmployeeId=r.ApproverEmployeeId
              WHERE r.EmployeeId=@employeeId
              ORDER BY r.SubmittedAt DESC",
            new { employeeId });

        return rows.ToList();
    }

    public async Task<List<RequestRow>> GetPendingForManager(int managerEmployeeId, bool allowApproverGroup)
    {
        using var cn = Db.Open(_cs);

        // scope: direct reports OR (if in approver group) all pending
        var sql = allowApproverGroup
            ? @"SELECT r.RequestId, r.EmployeeId, e.DisplayName AS EmployeeName,
                       r.TypeId, t.Code AS TypeCode, t.Label AS TypeLabel,
                       r.StartDateTime, r.EndDateTime, r.Status, r.Note,
                       r.ApproverEmployeeId, a.DisplayName AS ApproverName,
                       r.SubmittedAt, r.DecisionAt, r.DecisionNote
                FROM dbo.AvailabilityRequests r
                JOIN dbo.Employees e ON e.EmployeeId=r.EmployeeId
                JOIN dbo.AvailabilityTypes t ON t.TypeId=r.TypeId
                LEFT JOIN dbo.Employees a ON a.EmployeeId=r.ApproverEmployeeId
                WHERE r.Status='Pending'
                ORDER BY r.SubmittedAt ASC"
            : @"SELECT r.RequestId, r.EmployeeId, e.DisplayName AS EmployeeName,
                       r.TypeId, t.Code AS TypeCode, t.Label AS TypeLabel,
                       r.StartDateTime, r.EndDateTime, r.Status, r.Note,
                       r.ApproverEmployeeId, a.DisplayName AS ApproverName,
                       r.SubmittedAt, r.DecisionAt, r.DecisionNote
                FROM dbo.AvailabilityRequests r
                JOIN dbo.Employees e ON e.EmployeeId=r.EmployeeId
                JOIN dbo.AvailabilityTypes t ON t.TypeId=r.TypeId
                LEFT JOIN dbo.Employees a ON a.EmployeeId=r.ApproverEmployeeId
                WHERE r.Status='Pending'
                  AND e.ManagerEmployeeId=@managerEmployeeId
                ORDER BY r.SubmittedAt ASC";

        var rows = await cn.QueryAsync<RequestRow>(sql, new { managerEmployeeId });
        return rows.ToList();
    }

    public async Task Decide(long requestId, int approverEmployeeId, bool approve, string? decisionNote)
    {
        using var cn = Db.Open(_cs);

        // Only pending can be decided
        var status = approve ? "Approved" : "Rejected";

        await cn.ExecuteAsync(
            @"UPDATE dbo.AvailabilityRequests
              SET Status=@status,
                  DecisionAt=SYSUTCDATETIME(),
                  DecisionNote=@decisionNote,
                  ApproverEmployeeId=@approverEmployeeId
              WHERE RequestId=@requestId AND Status='Pending'",
            new { requestId, status, decisionNote, approverEmployeeId });
    }

    public async Task<List<CalendarEvent>> GetApprovedEvents(
        DateTime start, DateTime end,
        int? departmentId, string? typeCodes, bool myTeamOnly, int viewerEmployeeId)
    {
        using var cn = Db.Open(_cs);

        var codes = (typeCodes ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant())
            .ToArray();

        var sql = @"
SELECT r.RequestId AS id,
       (e.DisplayName + ' — ' + t.Label) AS title,
       r.StartDateTime AS start,
       r.EndDateTime AS [end],
       t.Code AS typeCode,
       e.EmployeeId AS employeeId
FROM dbo.AvailabilityRequests r
JOIN dbo.Employees e ON e.EmployeeId=r.EmployeeId
JOIN dbo.AvailabilityTypes t ON t.TypeId=r.TypeId
WHERE r.Status='Approved'
  AND r.StartDateTime < @end
  AND r.EndDateTime > @start
";

        if (departmentId.HasValue)
            sql += " AND e.DepartmentId = @departmentId ";

        if (codes.Length > 0)
            sql += " AND t.Code IN @codes ";

        if (myTeamOnly)
            sql += " AND (e.EmployeeId=@viewerEmployeeId OR e.ManagerEmployeeId=@viewerEmployeeId) ";

        sql += " ORDER BY e.DisplayName";

        var rows = await cn.QueryAsync<CalendarEvent>(sql, new
        {
            start,
            end,
            departmentId,
            codes,
            viewerEmployeeId
        });

        return rows.ToList();
    }
}
