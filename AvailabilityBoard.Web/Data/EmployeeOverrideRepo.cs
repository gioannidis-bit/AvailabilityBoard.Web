using Dapper;

namespace AvailabilityBoard.Web.Data;

public sealed record EmployeeOverrideRow(
    int EmployeeId,
    int? DepartmentIdOverride,
    int? ManagerEmployeeIdOverride,
    bool? IsApproverOverride,
    bool? IsAdminOverride
);

public sealed class EmployeeOverrideRepo
{
    private readonly string _cs;
    public EmployeeOverrideRepo(string cs) => _cs = cs;

    public async Task<EmployeeOverrideRow?> Get(int employeeId)
    {
        using var cn = Db.Open(_cs);
        return await cn.QuerySingleOrDefaultAsync<EmployeeOverrideRow>(
            @"SELECT EmployeeId, DepartmentIdOverride, ManagerEmployeeIdOverride, IsApproverOverride, IsAdminOverride
              FROM dbo.EmployeeOverrides WHERE EmployeeId=@employeeId",
            new { employeeId });
    }

    public async Task Upsert(EmployeeOverrideRow row)
    {
        using var cn = Db.Open(_cs);
        await cn.ExecuteAsync(@"
IF EXISTS (SELECT 1 FROM dbo.EmployeeOverrides WHERE EmployeeId=@EmployeeId)
    UPDATE dbo.EmployeeOverrides
       SET DepartmentIdOverride=@DepartmentIdOverride,
           ManagerEmployeeIdOverride=@ManagerEmployeeIdOverride,
           IsApproverOverride=@IsApproverOverride,
           IsAdminOverride=@IsAdminOverride
     WHERE EmployeeId=@EmployeeId
ELSE
    INSERT INTO dbo.EmployeeOverrides(EmployeeId, DepartmentIdOverride, ManagerEmployeeIdOverride, IsApproverOverride, IsAdminOverride)
    VALUES(@EmployeeId, @DepartmentIdOverride, @ManagerEmployeeIdOverride, @IsApproverOverride, @IsAdminOverride)
", row);
    }
}
