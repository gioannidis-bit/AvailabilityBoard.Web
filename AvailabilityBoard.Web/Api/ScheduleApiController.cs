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

    public sealed record UpsertScheduleDto(int EmployeeId, DateTime Date, int TypeId, string? Note);
    public sealed record DeleteScheduleDto(int EmployeeId, DateTime Date);

    [HttpGet("day")]
    public async Task<IActionResult> GetDay([FromQuery] int employeeId, [FromQuery] DateTime date)
    {
        if (employeeId <= 0) return BadRequest("employeeId");

        if (!await CanEditEmployeeSchedule(employeeId))
            return Forbid();

        var row = await _db.Schedules.GetDay(employeeId, date);
        if (row == null)
        {
            return Ok(new { exists = false });
        }

        return Ok(new
        {
            exists = true,
            scheduleId = row.ScheduleId,
            employeeId = row.EmployeeId,
            date = row.ScheduleDate.ToString("yyyy-MM-dd"),
            typeId = row.TypeId,
            typeCode = row.TypeCode,
            typeLabel = row.TypeLabel,
            colorHex = row.ColorHex,
            note = row.Note
        });
    }

    [HttpPost("upsert")]
    public async Task<IActionResult> Upsert([FromBody] UpsertScheduleDto dto)
    {
        if (dto.EmployeeId <= 0) return BadRequest("employeeId");
        if (dto.TypeId <= 0) return BadRequest("typeId");

        if (!await CanEditEmployeeSchedule(dto.EmployeeId))
            return Forbid();

        var actorId = User.GetEmployeeId();
        await _db.Schedules.Upsert(dto.EmployeeId, dto.Date, dto.TypeId, dto.Note, actorId);
        return Ok(new { ok = true });
    }

    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] DeleteScheduleDto dto)
    {
        if (dto.EmployeeId <= 0) return BadRequest("employeeId");

        if (!await CanEditEmployeeSchedule(dto.EmployeeId))
            return Forbid();

        await _db.Schedules.Delete(dto.EmployeeId, dto.Date);
        return Ok(new { ok = true });
    }

    private async Task<bool> CanEditEmployeeSchedule(int targetEmployeeId)
    {
        // 1) Admin (με override support από το υπάρχον claim/policy σου)
        if (User.IsAdmin())
            return true;

        // 2) Department manager ή Approver access
        var actorId = User.GetEmployeeId();

        var target = await _db.Employees.GetById(targetEmployeeId);
        if (target == null) return false;

        // effective department του target (λαμβάνει override υπόψη)
        var ovr = await _db.Overrides.Get(targetEmployeeId);
        var targetDeptId = ovr?.DepartmentIdOverride ?? target.DepartmentId;
        if (!targetDeptId.HasValue) return false;

        // Department manager
        var mgrId = await _db.DepartmentManagers.GetManagerEmployeeId(targetDeptId.Value);
        if (mgrId.HasValue && mgrId.Value == actorId)
            return true;

        // Explicit department approve access
        var approvable = await _db.DepartmentAccess.GetApprovableDepartmentIds(actorId);
        return approvable.Contains(targetDeptId.Value);
    }
}
