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

        var empId = await _db.Employees.Upsert(
            adGuid: u.AdGuid,
            sam: u.SamAccountName,
            displayName: u.DisplayName,
            email: u.Email,
            departmentId: deptId,
            isActive: true
        );

        // Manager linking: if we can resolve later, we set it in another step (see Login handler)
        return empId;
    }
}
