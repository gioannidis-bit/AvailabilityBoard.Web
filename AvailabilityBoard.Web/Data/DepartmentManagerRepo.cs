using Dapper;

namespace AvailabilityBoard.Web.Data;

public sealed class DepartmentManagerRepo
{
    private readonly string _cs;
    public DepartmentManagerRepo(string cs) => _cs = cs;

    public async Task<int?> GetManagerEmployeeId(int departmentId)
    {
        using var cn = Db.Open(_cs);
        return await cn.ExecuteScalarAsync<int?>(
            "SELECT ManagerEmployeeId FROM dbo.DepartmentManagers WHERE DepartmentId=@departmentId",
            new { departmentId });
    }

    public async Task Upsert(int departmentId, int managerEmployeeId)
    {
        using var cn = Db.Open(_cs);
        await cn.ExecuteAsync(@"
IF EXISTS (SELECT 1 FROM dbo.DepartmentManagers WHERE DepartmentId=@departmentId)
    UPDATE dbo.DepartmentManagers SET ManagerEmployeeId=@managerEmployeeId WHERE DepartmentId=@departmentId
ELSE
    INSERT INTO dbo.DepartmentManagers(DepartmentId, ManagerEmployeeId) VALUES(@departmentId, @managerEmployeeId)
", new { departmentId, managerEmployeeId });
    }
}
