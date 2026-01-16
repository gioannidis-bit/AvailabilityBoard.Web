using Dapper;

namespace AvailabilityBoard.Web.Data;

public sealed record DepartmentAccessRow(
    int AccessId,
    int EmployeeId,
    int DepartmentId,
    bool CanView,
    bool CanApprove,
    DateTime GrantedAt
);

public sealed class DepartmentAccessRepo
{
    private readonly string _cs;
    public DepartmentAccessRepo(string cs) => _cs = cs;

    /// <summary>
    /// Επιστρέφει τα department IDs που μπορεί να δει ο employee
    /// (το δικό του + όσα έχει explicit access)
    /// </summary>
    public async Task<List<int>> GetViewableDepartmentIds(int employeeId, int? ownDepartmentId)
    {
        using var cn = Db.Open(_cs);

        var extra = await cn.QueryAsync<int>(
            @"SELECT DepartmentId FROM dbo.EmployeeDepartmentAccess 
              WHERE EmployeeId = @employeeId AND CanView = 1",
            new { employeeId });

        var result = extra.ToList();

        // Πάντα βλέπει το δικό του department
        if (ownDepartmentId.HasValue && !result.Contains(ownDepartmentId.Value))
            result.Add(ownDepartmentId.Value);

        return result;
    }

    /// <summary>
    /// Επιστρέφει τα department IDs όπου μπορεί να κάνει approve
    /// </summary>
    public async Task<List<int>> GetApprovableDepartmentIds(int employeeId)
    {
        using var cn = Db.Open(_cs);
        var rows = await cn.QueryAsync<int>(
            @"SELECT DepartmentId FROM dbo.EmployeeDepartmentAccess 
              WHERE EmployeeId = @employeeId AND CanApprove = 1",
            new { employeeId });
        return rows.ToList();
    }

    public async Task<List<DepartmentAccessRow>> GetByEmployee(int employeeId)
    {
        using var cn = Db.Open(_cs);
        var rows = await cn.QueryAsync<DepartmentAccessRow>(
            @"SELECT AccessId, EmployeeId, DepartmentId, CanView, CanApprove, GrantedAt
              FROM dbo.EmployeeDepartmentAccess
              WHERE EmployeeId = @employeeId",
            new { employeeId });
        return rows.ToList();
    }

    public async Task Grant(int employeeId, int departmentId, bool canView, bool canApprove, int? grantedBy)
    {
        using var cn = Db.Open(_cs);
        await cn.ExecuteAsync(@"
IF EXISTS (SELECT 1 FROM dbo.EmployeeDepartmentAccess WHERE EmployeeId=@employeeId AND DepartmentId=@departmentId)
    UPDATE dbo.EmployeeDepartmentAccess 
    SET CanView=@canView, CanApprove=@canApprove, GrantedAt=SYSUTCDATETIME(), GrantedByEmployeeId=@grantedBy
    WHERE EmployeeId=@employeeId AND DepartmentId=@departmentId
ELSE
    INSERT INTO dbo.EmployeeDepartmentAccess(EmployeeId, DepartmentId, CanView, CanApprove, GrantedByEmployeeId)
    VALUES(@employeeId, @departmentId, @canView, @canApprove, @grantedBy)
", new { employeeId, departmentId, canView, canApprove, grantedBy });
    }

    public async Task Revoke(int employeeId, int departmentId)
    {
        using var cn = Db.Open(_cs);
        await cn.ExecuteAsync(
            @"DELETE FROM dbo.EmployeeDepartmentAccess 
              WHERE EmployeeId=@employeeId AND DepartmentId=@departmentId",
            new { employeeId, departmentId });
    }
}
