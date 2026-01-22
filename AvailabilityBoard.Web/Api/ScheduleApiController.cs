using AvailabilityBoard.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AvailabilityBoard.Web.Api;

[ApiController]
[Route("api/schedules")]
[Authorize]
public sealed class ScheduleApiController : ControllerBase
{
    private readonly Db _db;
    public ScheduleApiController(Db db) => _db = db;

    public sealed record ScheduleBlockDto(
        int TypeId,
        string? Start,
        string? End,
        string? CustomerName,
        string? OutActivity,
        string? Note);

    public sealed record ReplaceDayDto(
        int EmployeeId,
        DateTime Date,
        List<ScheduleBlockDto> Blocks);

    public sealed record DeleteDayDto(int EmployeeId, DateTime Date);

    [HttpGet("day")]
    public async Task<IActionResult> GetDay([FromQuery] int employeeId, [FromQuery] DateTime date)
    {
        if (employeeId <= 0) return BadRequest("employeeId");

        if (!await CanEditEmployeeSchedule(employeeId))
            return Forbid();

        var blocks = await _db.Schedules.GetDayBlocks(employeeId, date);
        if (blocks.Count == 0) return Ok(new { exists = false, blocks = Array.Empty<object>() });

        return Ok(new
        {
            exists = true,
            employeeId,
            date = date.Date.ToString("yyyy-MM-dd"),
            blocks = blocks.Select(b => new
            {
                scheduleBlockId = b.ScheduleBlockId,
                typeId = b.TypeId,
                typeCode = b.TypeCode,
                typeLabel = b.TypeLabel,
                colorHex = b.ColorHex,
                start = b.StartTime?.ToString(@"hh\:mm"),
                end = b.EndTime?.ToString(@"hh\:mm"),
                customerName = b.CustomerName,
                outActivity = b.OutActivity,
                note = b.Note
            })
        });
    }

    [HttpPost("replace-day")]
    public async Task<IActionResult> ReplaceDay([FromBody] ReplaceDayDto dto)
    {
        if (dto.EmployeeId <= 0) return BadRequest("employeeId");
        if (dto.Blocks == null) return BadRequest("blocks");

        if (!await CanEditEmployeeSchedule(dto.EmployeeId))
            return Forbid();

        var actorId = User.GetEmployeeId();

        static TimeSpan? ParseTime(string? hhmm)
        {
            if (string.IsNullOrWhiteSpace(hhmm)) return null;
            return TimeSpan.TryParse(hhmm, out var ts) ? ts : null;
        }

        // Basic validation/sanitization
        var blocks = dto.Blocks
            .Where(b => b != null)
            .Select(b =>
            {
                var st = ParseTime(b.Start);
                var en = ParseTime(b.End);
                return (TypeId: b.TypeId,
                        StartTime: st,
                        EndTime: en,
                        CustomerName: b.CustomerName,
                        OutActivity: b.OutActivity,
                        Note: b.Note);
            })
            .Where(b => b.TypeId > 0)
            .ToList();

        // If user submits empty, treat as delete
        if (blocks.Count == 0)
        {
            await _db.Schedules.DeleteDay(dto.EmployeeId, dto.Date);
            return Ok(new { ok = true });
        }

        // Optional: ensure end > start when both provided
        foreach (var b in blocks)
        {
            if (b.StartTime.HasValue && b.EndTime.HasValue && b.EndTime.Value <= b.StartTime.Value)
                return BadRequest("end must be after start");
        }

        await _db.Schedules.ReplaceDayBlocks(dto.EmployeeId, dto.Date, blocks, actorId);

        return Ok(new { ok = true });
    }

    [HttpPost("delete-day")]
    public async Task<IActionResult> DeleteDay([FromBody] DeleteDayDto dto)
    {
        if (dto.EmployeeId <= 0) return BadRequest("employeeId");

        if (!await CanEditEmployeeSchedule(dto.EmployeeId))
            return Forbid();

        await _db.Schedules.DeleteDay(dto.EmployeeId, dto.Date);
        return Ok(new { ok = true });
    }

    private async Task<bool> CanEditEmployeeSchedule(int targetEmployeeId)
    {
        if (User.IsAdmin()) return true;

        var actorId = User.GetEmployeeId();
        var target = await _db.Employees.GetById(targetEmployeeId);
        if (target == null) return false;

        var ovr = await _db.Overrides.Get(targetEmployeeId);
        var targetDeptId = ovr?.DepartmentIdOverride ?? target.DepartmentId;
        if (!targetDeptId.HasValue) return false;

        var mgrId = await _db.DepartmentManagers.GetManagerEmployeeId(targetDeptId.Value);
        if (mgrId.HasValue && mgrId.Value == actorId) return true;

        var approvable = await _db.DepartmentAccess.GetApprovableDepartmentIds(actorId);
        return approvable.Contains(targetDeptId.Value);
    }
}