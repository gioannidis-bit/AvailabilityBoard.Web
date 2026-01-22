using AvailabilityBoard.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AvailabilityBoard.Web.Api;

[ApiController]
[Route("api")]
[Authorize]
public class DashboardApiController : ControllerBase
{
    private readonly Db _db;

    public DashboardApiController(Db db) => _db = db;

    private static int[] ParseIntCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<int>();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
                  .Where(v => v.HasValue)
                  .Select(v => v!.Value)
                  .ToArray();
    }

    private async Task<(bool canManage, bool isAdmin)> GetUserPermissions(int empId, Employee emp)
    {
        var managedDepts = await _db.DepartmentManagers.GetManagedDepartmentIds(empId);
        var isDeptManager = managedDepts.Any();
        var canManage = emp.IsAdmin || emp.IsApprover || isDeptManager;
        return (canManage, emp.IsAdmin);
    }

    // details για tooltip: δουλεύει τόσο για "single" όσο και για πολλαπλά blocks
    private static string? BuildCellDetails(WeeklyGridCell c)
    {
        if (c.Blocks == null || c.Blocks.Count == 0) return null;

        string Fmt(TimeSpan? t) => t.HasValue ? t.Value.ToString(@"hh\:mm") : "";

        var lines = new List<string>();

        foreach (var b in c.Blocks)
        {
            var time = (b.StartTime.HasValue || b.EndTime.HasValue)
                ? $"{Fmt(b.StartTime)}-{Fmt(b.EndTime)}".Trim('-')
                : "All-day";

            var head = $"{time} {b.TypeLabel}".Trim();

            var extras = new List<string>();
            if (!string.IsNullOrWhiteSpace(b.CustomerName)) extras.Add($"Customer: {b.CustomerName}");
            if (!string.IsNullOrWhiteSpace(b.OutActivity)) extras.Add($"Reason: {b.OutActivity}");
            if (!string.IsNullOrWhiteSpace(b.Note)) extras.Add(b.Note!);

            if (extras.Count == 0)
                lines.Add(head);
            else
                lines.Add(head + " • " + string.Join(" • ", extras));
        }

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    [HttpGet("departments")]
    public async Task<IActionResult> GetDepartments()
    {
        var depts = await _db.Departments.GetAll();
        return Ok(depts.Select(d => new { d.DepartmentId, d.Name, d.ColorHex }));
    }

    [HttpGet("types")]
    public async Task<IActionResult> GetTypes()
    {
        var types = await _db.Types.GetAll();
        return Ok(types.Select(t => new { t.TypeId, t.Code, t.Label, t.ColorHex }));
    }

    [HttpGet("events")]
    public async Task<IActionResult> GetEvents(
        [FromQuery] DateTime start,
        [FromQuery] DateTime end,
        [FromQuery] string? typeCodes = null,
        [FromQuery] string? typeIds = null,
        [FromQuery] string? deptIds = null)
    {
        var empId = User.GetEmployeeId();
        var emp = await _db.Employees.GetById(empId);
        if (emp == null) return Unauthorized();

        var (canManage, isAdmin) = await GetUserPermissions(empId, emp);

        if (!canManage)
        {
            var myEvents = await _db.Requests.GetApprovedEventsForEmployee(start, end, empId);
            return Ok(myEvents.Select(e => new
            {
                e.id,
                title = $"{e.employeeName} - {e.typeCode}",
                e.start,
                e.end,
                e.typeCode,
                backgroundColor = e.color,
                borderColor = e.color,
                extendedProps = new { e.employeeId, e.employeeName, e.note, e.typeCode }
            }));
        }

        List<int> visibleDepts;
        if (isAdmin)
        {
            var allDepts = await _db.Departments.GetAll();
            visibleDepts = allDepts.Select(d => d.DepartmentId).ToList();
        }
        else
        {
            visibleDepts = await _db.DepartmentAccess.GetViewableDepartmentIds(empId, emp.DepartmentId);
        }

        var requestedDeptIds = ParseIntCsv(deptIds);
        bool includeNoDepartment = isAdmin && requestedDeptIds.Length == 0;

        if (requestedDeptIds.Length > 0)
            visibleDepts = visibleDepts.Intersect(requestedDeptIds).ToList();

        if (visibleDepts.Count == 0 && !includeNoDepartment)
            return Ok(Array.Empty<object>());

        var events = await _db.Requests.GetApprovedEvents(
            start, end, visibleDepts, includeNoDepartment,
            typeCodes: typeCodes, typeIdsCsv: typeIds);

        return Ok(events.Select(e => new
        {
            e.id,
            title = $"{e.employeeName} - {e.typeCode}",
            e.start,
            e.end,
            e.typeCode,
            backgroundColor = e.color,
            borderColor = e.color,
            extendedProps = new { e.employeeId, e.employeeName, e.note, e.typeCode }
        }));
    }

    [HttpGet("snapshot")]
    public async Task<IActionResult> GetSnapshot(
        [FromQuery] string? deptIds = null,
        [FromQuery] string? typeIds = null)
    {
        var empId = User.GetEmployeeId();
        var emp = await _db.Employees.GetById(empId);
        if (emp == null) return Unauthorized();

        var (canManage, isAdmin) = await GetUserPermissions(empId, emp);

        if (!canManage)
        {
            var mySnapshot = await _db.Requests.GetTodaySnapshotForEmployee(empId);
            return Ok(mySnapshot);
        }

        List<int> visibleDepts;
        if (isAdmin)
        {
            var allDepts = await _db.Departments.GetAll();
            visibleDepts = allDepts.Select(d => d.DepartmentId).ToList();
        }
        else
        {
            visibleDepts = await _db.DepartmentAccess.GetViewableDepartmentIds(empId, emp.DepartmentId);
        }

        var requestedDeptIds = ParseIntCsv(deptIds);
        bool includeNoDepartment = isAdmin && requestedDeptIds.Length == 0;

        if (requestedDeptIds.Length > 0)
            visibleDepts = visibleDepts.Intersect(requestedDeptIds).ToList();

        if (visibleDepts.Count == 0 && !includeNoDepartment)
            return Ok(Array.Empty<object>());

        var snapshot = await _db.Requests.GetTodaySnapshot(visibleDepts, includeNoDepartment, typeIdsCsv: typeIds);
        return Ok(snapshot);
    }

    // ✅ UPDATED: weekly-grid επιστρέφει details στα days
    [HttpGet("weekly-grid")]
    public async Task<IActionResult> GetWeeklyGrid(
        [FromQuery] DateTime? weekStart = null,
        [FromQuery] string? deptIds = null,
        [FromQuery] string? typeIds = null)
    {
        var empId = User.GetEmployeeId();
        var emp = await _db.Employees.GetById(empId);
        if (emp == null) return Unauthorized();

        var ws = weekStart ?? StartOfWeek(DateTime.Today);
        var (canManage, isAdmin) = await GetUserPermissions(empId, emp);

        if (!canManage)
        {
            var grid = await _db.Requests.GetWeeklyGridForEmployee(ws, empId);
            return Ok(new
            {
                weekStart = ws,
                weekEnd = ws.AddDays(7),
                days = Enumerable.Range(0, 7).Select(i => ws.AddDays(i).ToString("yyyy-MM-dd")).ToArray(),
                dayNames = Enumerable.Range(0, 7).Select(i => ws.AddDays(i).ToString("ddd d/M")).ToArray(),
                rows = grid.Select(r => new
                {
                    r.EmployeeId,
                    r.DisplayName,
                    r.Initials,
                    r.DepartmentId,
                    days = r.Days.Select(d => d.HasEvent
                        ? new
                        {
                            d.TypeCode,
                            d.TypeLabel,
                            d.ColorHex,
                            blocks = d.Blocks?.Select(b => new
                            {
                                b.TypeId,
                                b.TypeCode,
                                b.TypeLabel,
                                b.ColorHex,
                                start = b.StartTime?.ToString(@"hh\:mm"),
                                end = b.EndTime?.ToString(@"hh\:mm"),
                                b.CustomerName,
                                b.OutActivity,
                                b.Note
                            }),
                            details = BuildCellDetails(d)
                        }
                        : null)
                })
            });
        }

        List<int> visibleDepts;
        if (isAdmin)
        {
            var allDepts = await _db.Departments.GetAll();
            visibleDepts = allDepts.Select(d => d.DepartmentId).ToList();
        }
        else
        {
            visibleDepts = await _db.DepartmentAccess.GetViewableDepartmentIds(empId, emp.DepartmentId);
        }

        var requestedDeptIds = ParseIntCsv(deptIds);
        bool includeNoDepartment = isAdmin && requestedDeptIds.Length == 0;

        if (requestedDeptIds.Length > 0)
            visibleDepts = visibleDepts.Intersect(requestedDeptIds).ToList();

        if (visibleDepts.Count == 0 && !includeNoDepartment)
            return Ok(new { weekStart = ws, rows = Array.Empty<object>() });

        var gridData = await _db.Requests.GetWeeklyGrid(ws, visibleDepts, includeNoDepartment, typeIdsCsv: typeIds);

        return Ok(new
        {
            weekStart = ws,
            weekEnd = ws.AddDays(7),
            days = Enumerable.Range(0, 7).Select(i => ws.AddDays(i).ToString("yyyy-MM-dd")).ToArray(),
            dayNames = Enumerable.Range(0, 7).Select(i => ws.AddDays(i).ToString("ddd d/M")).ToArray(),
            rows = gridData.Select(r => new
            {
                r.EmployeeId,
                r.DisplayName,
                r.Initials,
                r.DepartmentId,
                days = r.Days.Select(d => d.HasEvent
                    ? new
                    {
                        d.TypeCode,
                        d.TypeLabel,
                        d.ColorHex,
                        blocks = d.Blocks?.Select(b => new
                        {
                            b.TypeId,
                            b.TypeCode,
                            b.TypeLabel,
                            b.ColorHex,
                            start = b.StartTime?.ToString(@"hh\:mm"),
                            end = b.EndTime?.ToString(@"hh\:mm"),
                            b.CustomerName,
                            b.OutActivity,
                            b.Note
                        }),
                        details = BuildCellDetails(d)
                    }
                    : null)
            })
        });
    }

    [HttpGet("employees/search")]
    public async Task<IActionResult> SearchEmployees([FromQuery] string q)
    {
        var results = await _db.Employees.Search(q ?? "", 20);
        return Ok(results.Select(e => new { e.EmployeeId, e.DisplayName, e.SamAccountName, e.Email, e.DepartmentId }));
    }

    [HttpGet("notifications/count")]
    public async Task<IActionResult> GetNotificationCount()
    {
        var empId = User.GetEmployeeId();
        var count = await _db.Notifications.GetUnreadCount(empId);
        return Ok(new { count });
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }
}
