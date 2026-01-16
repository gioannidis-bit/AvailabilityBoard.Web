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

    public async Task<List<(int DepartmentId, int ManagerEmployeeId)>> GetAll()
    {
        using var cn = Db.Open(_cs);
        var rows = await cn.QueryAsync<(int DepartmentId, int ManagerEmployeeId)>(
            "SELECT DepartmentId, ManagerEmployeeId FROM dbo.DepartmentManagers");
        return rows.ToList();
    }

    /// <summary>
    /// Επιστρέφει τα dept IDs όπου ο employee είναι manager
    /// </summary>
    public async Task<List<int>> GetManagedDepartmentIds(int managerEmployeeId)
    {
        using var cn = Db.Open(_cs);
        var rows = await cn.QueryAsync<int>(
            "SELECT DepartmentId FROM dbo.DepartmentManagers WHERE ManagerEmployeeId=@managerEmployeeId",
            new { managerEmployeeId });
        return rows.ToList();
    }
}
