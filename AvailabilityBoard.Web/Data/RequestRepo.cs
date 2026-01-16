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
                     r.TypeId, t.Code AS TypeCode, t.Label AS TypeLabel, t.ColorHex AS TypeColorHex,
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

    public async Task<List<RequestRow>> GetPendingForApprover(int approverEmployeeId, IEnumerable<int>? approvableDeptIds, bool isGlobalApprover)
    {
        using var cn = Db.Open(_cs);

        string sql;
        object param;

        if (isGlobalApprover)
        {
            sql = @"SELECT r.RequestId, r.EmployeeId, e.DisplayName AS EmployeeName,
                           r.TypeId, t.Code AS TypeCode, t.Label AS TypeLabel, t.ColorHex AS TypeColorHex,
                           r.StartDateTime, r.EndDateTime, r.Status, r.Note,
                           r.ApproverEmployeeId, a.DisplayName AS ApproverName,
                           r.SubmittedAt, r.DecisionAt, r.DecisionNote
                    FROM dbo.AvailabilityRequests r
                    JOIN dbo.Employees e ON e.EmployeeId=r.EmployeeId
                    JOIN dbo.AvailabilityTypes t ON t.TypeId=r.TypeId
                    LEFT JOIN dbo.Employees a ON a.EmployeeId=r.ApproverEmployeeId
                    WHERE r.Status='Pending'
                    ORDER BY r.SubmittedAt ASC";
            param = new { };
        }
        else
        {
            var deptIds = approvableDeptIds?.ToArray() ?? Array.Empty<int>();

            sql = @"SELECT r.RequestId, r.EmployeeId, e.DisplayName AS EmployeeName,
                           r.TypeId, t.Code AS TypeCode, t.Label AS TypeLabel, t.ColorHex AS TypeColorHex,
                           r.StartDateTime, r.EndDateTime, r.Status, r.Note,
                           r.ApproverEmployeeId, a.DisplayName AS ApproverName,
                           r.SubmittedAt, r.DecisionAt, r.DecisionNote
                    FROM dbo.AvailabilityRequests r
                    JOIN dbo.Employees e ON e.EmployeeId=r.EmployeeId
                    JOIN dbo.AvailabilityTypes t ON t.TypeId=r.TypeId
                    LEFT JOIN dbo.Employees a ON a.EmployeeId=r.ApproverEmployeeId
                    WHERE r.Status='Pending'
                      AND (e.ManagerEmployeeId=@approverEmployeeId 
                           OR e.DepartmentId IN @deptIds)
                    ORDER BY r.SubmittedAt ASC";
            param = new { approverEmployeeId, deptIds };
        }

        var rows = await cn.QueryAsync<RequestRow>(sql, param);
        return rows.ToList();
    }

    public async Task<List<RequestRow>> GetPendingForManager(int managerEmployeeId, bool allowApproverGroup)
        => await GetPendingForApprover(managerEmployeeId, null, allowApproverGroup);

    public async Task Decide(long requestId, int approverEmployeeId, bool approve, string? decisionNote)
    {
        using var cn = Db.Open(_cs);
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

    // ===================== DASHBOARD (Requests + Schedules) =====================

    private static int[] ParseIntCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<int>();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
                  .Where(v => v.HasValue)
                  .Select(v => v!.Value)
                  .ToArray();
    }

    /// <summary>
    /// Calendar events: Approved requests + EmployeeSchedules (all-day), με permission filtering
    /// typeIdsCsv: "1,2,3" (filter by TypeId)
    /// </summary>
    public async Task<List<CalendarEvent>> GetApprovedEvents(
        DateTime start, DateTime end,
        IEnumerable<int> visibleDepartmentIds,
        bool includeNoDepartment,
        string? typeCodes = null,
        string? typeIdsCsv = null)
    {
        using var cn = Db.Open(_cs);

        var codes = (typeCodes ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant())
            .ToArray();

        var typeIds = ParseIntCsv(typeIdsCsv);

        var deptIds = visibleDepartmentIds.ToArray();
        if (deptIds.Length == 0) deptIds = new[] { -1 };

        // 1) Approved requests (excluding hidden employees)
        var sqlReq = @"
SELECT r.RequestId AS id,
       e.DisplayName AS title,
       r.StartDateTime AS start,
       r.EndDateTime AS [end],
       t.Code AS typeCode,
       t.ColorHex AS color,
       e.EmployeeId AS employeeId,
       e.DisplayName AS employeeName,
       r.Note AS note
FROM dbo.AvailabilityRequests r
JOIN dbo.Employees e ON e.EmployeeId=r.EmployeeId
LEFT JOIN dbo.EmployeeOverrides o ON o.EmployeeId = e.EmployeeId
JOIN dbo.AvailabilityTypes t ON t.TypeId=r.TypeId
WHERE r.Status='Approved'
  AND r.StartDateTime < @end
  AND r.EndDateTime > @start
  AND e.IsActive = 1
  AND ISNULL(o.IsHiddenOverride, 0) = 0
  AND (
        e.DepartmentId IN @deptIds
        OR (@includeNoDepartment = 1 AND e.DepartmentId IS NULL)
      )
";

        if (codes.Length > 0)
            sqlReq += " AND t.Code IN @codes ";

        if (typeIds.Length > 0)
            sqlReq += " AND t.TypeId IN @typeIds ";

        var reqRows = await cn.QueryAsync<CalendarEvent>(
            sqlReq,
            new { start, end, deptIds, codes, typeIds, includeNoDepartment = includeNoDepartment ? 1 : 0 });

        // 2) Schedules (all-day, excluding hidden employees)
        var startDate = start.Date;
        var endDate = end.Date;

        var sqlSched = @"
SELECT -s.ScheduleId AS id,
       e.DisplayName AS title,
       CAST(s.ScheduleDate AS datetime2) AS start,
       DATEADD(day, 1, CAST(s.ScheduleDate AS datetime2)) AS [end],
       t.Code AS typeCode,
       t.ColorHex AS color,
       e.EmployeeId AS employeeId,
       e.DisplayName AS employeeName,
       s.Note AS note
FROM dbo.EmployeeSchedules s
JOIN dbo.Employees e ON e.EmployeeId=s.EmployeeId
LEFT JOIN dbo.EmployeeOverrides o ON o.EmployeeId = e.EmployeeId
JOIN dbo.AvailabilityTypes t ON t.TypeId=s.TypeId
WHERE s.ScheduleDate >= @startDate
  AND s.ScheduleDate <  @endDate
  AND e.IsActive = 1
  AND ISNULL(o.IsHiddenOverride, 0) = 0
  AND (
        e.DepartmentId IN @deptIds
        OR (@includeNoDepartment = 1 AND e.DepartmentId IS NULL)
      )
";

        if (codes.Length > 0)
            sqlSched += " AND t.Code IN @codes ";

        if (typeIds.Length > 0)
            sqlSched += " AND t.TypeId IN @typeIds ";

        var schedRows = await cn.QueryAsync<CalendarEvent>(
            sqlSched,
            new { startDate, endDate, deptIds, codes, typeIds, includeNoDepartment = includeNoDepartment ? 1 : 0 });

        // Combine
        var all = reqRows.Concat(schedRows)
                         .OrderBy(x => x.employeeName)
                         .ThenBy(x => x.start)
                         .ToList();

        return all;
    }

    /// <summary>
    /// Today's snapshot: Approved requests + Schedules (μόνο αν ΔΕΝ υπάρχει approved request για τον ίδιο employee σήμερα)
    /// typeIdsCsv: "1,2,3"
    /// </summary>
    public async Task<List<AvailabilitySnapshot>> GetTodaySnapshot(
        IEnumerable<int> visibleDepartmentIds,
        bool includeNoDepartment,
        string? typeIdsCsv = null)
    {
        using var cn = Db.Open(_cs);

        var typeIds = ParseIntCsv(typeIdsCsv);

        var deptIds = visibleDepartmentIds.ToArray();
        if (deptIds.Length == 0) deptIds = new[] { -1 };

        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        // Approved requests active today (excluding hidden employees)
        var sqlReq = @"
SELECT t.Code AS TypeCode,
       t.Label AS TypeLabel,
       t.ColorHex AS ColorHex,
       e.EmployeeId,
       e.DisplayName,
       e.DepartmentId,
       r.EndDateTime
FROM dbo.AvailabilityRequests r
JOIN dbo.Employees e ON e.EmployeeId = r.EmployeeId
LEFT JOIN dbo.EmployeeOverrides o ON o.EmployeeId = e.EmployeeId
JOIN dbo.AvailabilityTypes t ON t.TypeId = r.TypeId
WHERE r.Status = 'Approved'
  AND r.StartDateTime < @tomorrow
  AND r.EndDateTime > @today
  AND e.IsActive = 1
  AND ISNULL(o.IsHiddenOverride, 0) = 0
  AND (
        e.DepartmentId IN @deptIds
        OR (@includeNoDepartment = 1 AND e.DepartmentId IS NULL)
      )
";

        if (typeIds.Length > 0)
            sqlReq += " AND t.TypeId IN @typeIds ";

        sqlReq += " ORDER BY t.SortOrder, t.Label, e.DisplayName";

        var reqRows = (await cn.QueryAsync<dynamic>(
            sqlReq,
            new { today, tomorrow, deptIds, typeIds, includeNoDepartment = includeNoDepartment ? 1 : 0 }
        )).ToList();

        // Track employees already “covered” by approved requests
        var coveredEmpIds = new HashSet<int>(reqRows.Select(r => (int)r.EmployeeId));

        // Schedules for today (only if not covered by an approved request, excluding hidden)
        var sqlSched = @"
SELECT t.Code AS TypeCode,
       t.Label AS TypeLabel,
       t.ColorHex AS ColorHex,
       e.EmployeeId,
       e.DisplayName,
       e.DepartmentId,
       CAST(@tomorrow AS datetime2) AS EndDateTime
FROM dbo.EmployeeSchedules s
JOIN dbo.Employees e ON e.EmployeeId = s.EmployeeId
LEFT JOIN dbo.EmployeeOverrides o ON o.EmployeeId = e.EmployeeId
JOIN dbo.AvailabilityTypes t ON t.TypeId = s.TypeId
WHERE s.ScheduleDate = @today
  AND e.IsActive = 1
  AND ISNULL(o.IsHiddenOverride, 0) = 0
  AND (
        e.DepartmentId IN @deptIds
        OR (@includeNoDepartment = 1 AND e.DepartmentId IS NULL)
      )
";

        if (typeIds.Length > 0)
            sqlSched += " AND t.TypeId IN @typeIds ";

        var schedRows = (await cn.QueryAsync<dynamic>(
            sqlSched,
            new { today, tomorrow, deptIds, typeIds, includeNoDepartment = includeNoDepartment ? 1 : 0 }
        )).ToList();

        // Merge schedules not covered
        foreach (var s in schedRows)
        {
            int empId = (int)s.EmployeeId;
            if (!coveredEmpIds.Contains(empId))
                reqRows.Add(s);
        }

        // Group by type
        var grouped = reqRows
            .GroupBy(r => new { TypeCode = (string)r.TypeCode, TypeLabel = (string)r.TypeLabel, ColorHex = (string)r.ColorHex })
            .Select(g => new AvailabilitySnapshot(
                g.Key.TypeCode,
                g.Key.TypeLabel,
                g.Key.ColorHex,
                g.Count(),
                g.Select(r => new SnapshotEmployee(
                    (int)r.EmployeeId,
                    (string)r.DisplayName,
                    GetInitials((string)r.DisplayName),
                    (int?)r.DepartmentId,
                    (DateTime?)r.EndDateTime
                )).ToList()
            ))
            .ToList();

        return grouped;
    }

    /// <summary>
    /// Weekly grid: Approved requests take precedence, schedules fill empty days.
    /// typeIdsCsv: "1,2,3" - when specified, shows ONLY employees with those event types
    /// Filters out hidden employees (IsHiddenOverride = 1)
    /// </summary>
    public async Task<List<WeeklyGridRow>> GetWeeklyGrid(
        DateTime weekStart,
        IEnumerable<int> visibleDepartmentIds,
        bool includeNoDepartment,
        string? typeIdsCsv = null)
    {
        using var cn = Db.Open(_cs);

        var typeIds = ParseIntCsv(typeIdsCsv);
        var hasTypeFilter = typeIds.Length > 0;

        var deptIds = visibleDepartmentIds.ToArray();
        if (deptIds.Length == 0) deptIds = new[] { -1 };

        var weekEnd = weekStart.AddDays(7);

        // Employees in scope (excluding hidden)
        var employeesSql = @"
SELECT e.EmployeeId, e.AdGuid, e.SamAccountName, e.DisplayName, e.Email, 
       e.DepartmentId, e.ManagerEmployeeId, e.IsActive, e.IsAdmin, e.IsApprover
FROM dbo.Employees e
LEFT JOIN dbo.EmployeeOverrides o ON o.EmployeeId = e.EmployeeId
WHERE e.IsActive = 1
  AND ISNULL(o.IsHiddenOverride, 0) = 0
  AND (
        e.DepartmentId IN @deptIds
        OR (@includeNoDepartment = 1 AND e.DepartmentId IS NULL)
      )
ORDER BY e.DisplayName";

        var allEmployees = (await cn.QueryAsync<Employee>(
            employeesSql, 
            new { deptIds, includeNoDepartment = includeNoDepartment ? 1 : 0 }
        )).ToList();

        // Approved requests for the week
        var sqlEvents = @"
SELECT r.EmployeeId,
       r.StartDateTime,
       r.EndDateTime,
       t.Code AS TypeCode,
       t.Label AS TypeLabel,
       t.ColorHex AS ColorHex,
       t.TypeId AS TypeId
FROM dbo.AvailabilityRequests r
JOIN dbo.Employees e ON e.EmployeeId = r.EmployeeId
LEFT JOIN dbo.EmployeeOverrides o ON o.EmployeeId = e.EmployeeId
JOIN dbo.AvailabilityTypes t ON t.TypeId = r.TypeId
WHERE r.Status = 'Approved'
  AND r.StartDateTime < @weekEnd
  AND r.EndDateTime > @weekStart
  AND e.IsActive = 1
  AND ISNULL(o.IsHiddenOverride, 0) = 0
  AND (
        e.DepartmentId IN @deptIds
        OR (@includeNoDepartment = 1 AND e.DepartmentId IS NULL)
      )
";

        var events = (await cn.QueryAsync<dynamic>(
            sqlEvents,
            new { weekStart, weekEnd, deptIds, includeNoDepartment = includeNoDepartment ? 1 : 0 }
        )).ToList();

        var eventsByEmployee = events
            .GroupBy(e => (int)e.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Schedules for the week
        var sqlSched = @"
SELECT s.EmployeeId,
       s.ScheduleDate,
       t.Code AS TypeCode,
       t.Label AS TypeLabel,
       t.ColorHex AS ColorHex,
       t.TypeId AS TypeId
FROM dbo.EmployeeSchedules s
JOIN dbo.Employees e ON e.EmployeeId = s.EmployeeId
LEFT JOIN dbo.EmployeeOverrides o ON o.EmployeeId = e.EmployeeId
JOIN dbo.AvailabilityTypes t ON t.TypeId = s.TypeId
WHERE s.ScheduleDate >= @weekStartDate
  AND s.ScheduleDate <  @weekEndDate
  AND e.IsActive = 1
  AND ISNULL(o.IsHiddenOverride, 0) = 0
  AND (
        e.DepartmentId IN @deptIds
        OR (@includeNoDepartment = 1 AND e.DepartmentId IS NULL)
      )
";

        var weekStartDate = weekStart.Date;
        var weekEndDate = weekEnd.Date;

        var sched = (await cn.QueryAsync<dynamic>(
            sqlSched,
            new { weekStartDate, weekEndDate, deptIds, includeNoDepartment = includeNoDepartment ? 1 : 0 }
        )).ToList();

        var schedByEmpDay = new Dictionary<(int empId, DateTime day), dynamic>();
        foreach (var s in sched)
        {
            var key = ((int)s.EmployeeId, ((DateTime)s.ScheduleDate).Date);
            schedByEmpDay[key] = s;
        }

        // Determine which employees to show
        IEnumerable<Employee> employeesToShow;

        if (hasTypeFilter)
        {
            // When type filter is active: show ONLY employees who have matching events/schedules
            var empIdsWithMatchingEvents = events
                .Where(e => typeIds.Contains((int)e.TypeId))
                .Select(e => (int)e.EmployeeId)
                .ToHashSet();

            var empIdsWithMatchingSchedules = sched
                .Where(s => typeIds.Contains((int)s.TypeId))
                .Select(s => (int)s.EmployeeId)
                .ToHashSet();

            var relevantEmpIds = empIdsWithMatchingEvents.Union(empIdsWithMatchingSchedules).ToHashSet();
            employeesToShow = allEmployees.Where(e => relevantEmpIds.Contains(e.EmployeeId));
        }
        else
        {
            // No type filter: show all employees
            employeesToShow = allEmployees;
        }

        var result = new List<WeeklyGridRow>();

        foreach (var emp in employeesToShow)
        {
            var row = new WeeklyGridRow
            {
                EmployeeId = emp.EmployeeId,
                DisplayName = emp.DisplayName,
                Initials = GetInitials(emp.DisplayName),
                DepartmentId = emp.DepartmentId,
                Days = new WeeklyGridCell[7]
            };

            for (int i = 0; i < 7; i++)
            {
                var day = weekStart.AddDays(i).Date;
                var dayEnd = day.AddDays(1);

                row.Days[i] = new WeeklyGridCell { Date = day };

                // 1) Requests take precedence (if any overlap)
                if (eventsByEmployee.TryGetValue(emp.EmployeeId, out var empEvents))
                {
                    var dayEvent = empEvents.FirstOrDefault(e =>
                        (DateTime)e.StartDateTime < dayEnd && (DateTime)e.EndDateTime > day);

                    if (dayEvent != null)
                    {
                        // If type filter active, only show matching types
                        if (!hasTypeFilter || typeIds.Contains((int)dayEvent.TypeId))
                        {
                            row.Days[i].TypeCode = dayEvent.TypeCode;
                            row.Days[i].TypeLabel = dayEvent.TypeLabel;
                            row.Days[i].ColorHex = dayEvent.ColorHex;
                        }
                        continue;
                    }
                }

                // 2) Schedule fills empty
                if (schedByEmpDay.TryGetValue((emp.EmployeeId, day), out var sd))
                {
                    // If type filter active, only show matching types
                    if (!hasTypeFilter || typeIds.Contains((int)sd.TypeId))
                    {
                        row.Days[i].TypeCode = sd.TypeCode;
                        row.Days[i].TypeLabel = sd.TypeLabel;
                        row.Days[i].ColorHex = sd.ColorHex;
                    }
                }
            }

            result.Add(row);
        }

        return result;
    }

    // ===================== SINGLE EMPLOYEE (για regular users) =====================

    /// <summary>
    /// Calendar events for a single employee
    /// </summary>
    public async Task<List<CalendarEvent>> GetApprovedEventsForEmployee(DateTime start, DateTime end, int employeeId)
    {
        using var cn = Db.Open(_cs);

        var sql = @"
SELECT r.RequestId AS id,
       e.DisplayName AS title,
       r.StartDateTime AS start,
       r.EndDateTime AS [end],
       t.Code AS typeCode,
       t.ColorHex AS color,
       e.EmployeeId AS employeeId,
       e.DisplayName AS employeeName,
       r.Note AS note
FROM dbo.AvailabilityRequests r
JOIN dbo.Employees e ON e.EmployeeId = r.EmployeeId
JOIN dbo.AvailabilityTypes t ON t.TypeId = r.TypeId
WHERE r.Status = 'Approved'
  AND r.EmployeeId = @employeeId
  AND r.StartDateTime < @end
  AND r.EndDateTime > @start

UNION ALL

SELECT -s.ScheduleId AS id,
       e.DisplayName AS title,
       CAST(s.ScheduleDate AS datetime2) AS start,
       DATEADD(day, 1, CAST(s.ScheduleDate AS datetime2)) AS [end],
       t.Code AS typeCode,
       t.ColorHex AS color,
       e.EmployeeId AS employeeId,
       e.DisplayName AS employeeName,
       s.Note AS note
FROM dbo.EmployeeSchedules s
JOIN dbo.Employees e ON e.EmployeeId = s.EmployeeId
JOIN dbo.AvailabilityTypes t ON t.TypeId = s.TypeId
WHERE s.EmployeeId = @employeeId
  AND s.ScheduleDate >= @start
  AND s.ScheduleDate < @end

ORDER BY start";

        var events = await cn.QueryAsync<CalendarEvent>(sql, new { start, end, employeeId });
        return events.ToList();
    }

    /// <summary>
    /// Today's snapshot for a single employee
    /// </summary>
    public async Task<List<AvailabilitySnapshot>> GetTodaySnapshotForEmployee(int employeeId)
    {
        using var cn = Db.Open(_cs);

        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        var sql = @"
SELECT t.Code AS TypeCode,
       t.Label AS TypeLabel,
       t.ColorHex AS ColorHex,
       e.EmployeeId,
       e.DisplayName,
       e.DepartmentId,
       r.EndDateTime
FROM dbo.AvailabilityRequests r
JOIN dbo.Employees e ON e.EmployeeId = r.EmployeeId
JOIN dbo.AvailabilityTypes t ON t.TypeId = r.TypeId
WHERE r.Status = 'Approved'
  AND r.EmployeeId = @employeeId
  AND r.StartDateTime < @tomorrow
  AND r.EndDateTime > @today

UNION ALL

SELECT t.Code AS TypeCode,
       t.Label AS TypeLabel,
       t.ColorHex AS ColorHex,
       e.EmployeeId,
       e.DisplayName,
       e.DepartmentId,
       CAST(@tomorrow AS datetime2) AS EndDateTime
FROM dbo.EmployeeSchedules s
JOIN dbo.Employees e ON e.EmployeeId = s.EmployeeId
JOIN dbo.AvailabilityTypes t ON t.TypeId = s.TypeId
WHERE s.EmployeeId = @employeeId
  AND s.ScheduleDate = @today
  AND NOT EXISTS (
      SELECT 1 FROM dbo.AvailabilityRequests r2
      WHERE r2.EmployeeId = @employeeId
        AND r2.Status = 'Approved'
        AND r2.StartDateTime < @tomorrow
        AND r2.EndDateTime > @today
  )";

        var rows = (await cn.QueryAsync<dynamic>(sql, new { employeeId, today, tomorrow })).ToList();

        if (!rows.Any())
            return new List<AvailabilitySnapshot>();

        var grouped = rows
            .GroupBy(r => new { TypeCode = (string)r.TypeCode, TypeLabel = (string)r.TypeLabel, ColorHex = (string)r.ColorHex })
            .Select(g => new AvailabilitySnapshot(
                g.Key.TypeCode,
                g.Key.TypeLabel,
                g.Key.ColorHex,
                g.Count(),
                g.Select(r => new SnapshotEmployee(
                    (int)r.EmployeeId,
                    (string)r.DisplayName,
                    GetInitials((string)r.DisplayName),
                    (int?)r.DepartmentId,
                    (DateTime?)r.EndDateTime
                )).ToList()
            ))
            .ToList();

        return grouped;
    }

    /// <summary>
    /// Weekly grid for a single employee
    /// </summary>
    public async Task<List<WeeklyGridRow>> GetWeeklyGridForEmployee(DateTime weekStart, int employeeId)
    {
        using var cn = Db.Open(_cs);

        var weekEnd = weekStart.AddDays(7);

        var emp = await cn.QuerySingleOrDefaultAsync<Employee>(@"
SELECT EmployeeId, AdGuid, SamAccountName, DisplayName, Email, DepartmentId, 
       ManagerEmployeeId, IsActive, IsAdmin, IsApprover
FROM dbo.Employees WHERE EmployeeId = @employeeId", new { employeeId });

        if (emp == null)
            return new List<WeeklyGridRow>();

        // Get approved requests
        var events = (await cn.QueryAsync<dynamic>(@"
SELECT r.StartDateTime, r.EndDateTime, t.Code AS TypeCode, t.Label AS TypeLabel, t.ColorHex
FROM dbo.AvailabilityRequests r
JOIN dbo.AvailabilityTypes t ON t.TypeId = r.TypeId
WHERE r.Status = 'Approved'
  AND r.EmployeeId = @employeeId
  AND r.StartDateTime < @weekEnd
  AND r.EndDateTime > @weekStart",
            new { employeeId, weekStart, weekEnd })).ToList();

        // Get schedules
        var schedules = (await cn.QueryAsync<dynamic>(@"
SELECT s.ScheduleDate, t.Code AS TypeCode, t.Label AS TypeLabel, t.ColorHex
FROM dbo.EmployeeSchedules s
JOIN dbo.AvailabilityTypes t ON t.TypeId = s.TypeId
WHERE s.EmployeeId = @employeeId
  AND s.ScheduleDate >= @weekStart
  AND s.ScheduleDate < @weekEnd",
            new { employeeId, weekStart, weekEnd })).ToList();

        var schedByDay = schedules.ToDictionary(s => ((DateTime)s.ScheduleDate).Date);

        var row = new WeeklyGridRow
        {
            EmployeeId = emp.EmployeeId,
            DisplayName = emp.DisplayName,
            Initials = GetInitials(emp.DisplayName),
            DepartmentId = emp.DepartmentId,
            Days = new WeeklyGridCell[7]
        };

        for (int i = 0; i < 7; i++)
        {
            var day = weekStart.AddDays(i).Date;
            var dayEnd = day.AddDays(1);

            row.Days[i] = new WeeklyGridCell { Date = day };

            // Check requests first
            var dayEvent = events.FirstOrDefault(e =>
                (DateTime)e.StartDateTime < dayEnd && (DateTime)e.EndDateTime > day);

            if (dayEvent != null)
            {
                row.Days[i].TypeCode = dayEvent.TypeCode;
                row.Days[i].TypeLabel = dayEvent.TypeLabel;
                row.Days[i].ColorHex = dayEvent.ColorHex;
                continue;
            }

            // Check schedules
            if (schedByDay.TryGetValue(day, out var sd))
            {
                row.Days[i].TypeCode = sd.TypeCode;
                row.Days[i].TypeLabel = sd.TypeLabel;
                row.Days[i].ColorHex = sd.ColorHex;
            }
        }

        return new List<WeeklyGridRow> { row };
    }

    private static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpper();

        return (parts[0][0].ToString() + parts[^1][0]).ToUpper();
    }
}

public sealed class WeeklyGridRow
{
    public int EmployeeId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Initials { get; set; } = "";
    public int? DepartmentId { get; set; }
    public WeeklyGridCell[] Days { get; set; } = new WeeklyGridCell[7];
}

public sealed class WeeklyGridCell
{
    public DateTime Date { get; set; }
    public string? TypeCode { get; set; }
    public string? TypeLabel { get; set; }
    public string? ColorHex { get; set; }
    public bool HasEvent => TypeCode != null;
}
