using AvailabilityBoard.Web.Data;

namespace AvailabilityBoard.Web.Services;

public sealed class EmployeeSyncService
{
    private readonly Db _db;
    public EmployeeSyncService(Db db) => _db = db;

    public async Task<int> UpsertFromAd(LdapUser u)
    {
        int? deptId = null;
        if (!string.IsNullOrWhiteSpace(u.Department))
            deptId = await _db.Departments.Ensure(u.Department.Trim());

        // Check if there's a department override
        var existing = await _db.Employees.GetByAdGuid(u.AdGuid);
        if (existing != null)
        {
            var ovr = await _db.Overrides.Get(existing.EmployeeId);
            if (ovr?.DepartmentIdOverride != null)
                deptId = ovr.DepartmentIdOverride; // Keep override
        }

        var empId = await _db.Employees.Upsert(
            adGuid: u.AdGuid,
            sam: u.SamAccountName,
            displayName: u.DisplayName,
            email: u.Email,
            departmentId: deptId,
            isActive: true
        );

        return empId;
    }
}
