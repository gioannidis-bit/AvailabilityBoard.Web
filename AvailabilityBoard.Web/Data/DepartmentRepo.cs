using Dapper;

namespace AvailabilityBoard.Web.Data;

public sealed class DepartmentRepo
{
    private readonly string _cs;
    public DepartmentRepo(string cs) => _cs = cs;

    public async Task<List<Department>> GetAll()
    {
        using var cn = Db.Open(_cs);
        var rows = await cn.QueryAsync<Department>(
            "SELECT DepartmentId, Name FROM dbo.Departments ORDER BY Name");
        return rows.ToList();
    }

    public async Task<int> Ensure(string name)
    {
        using var cn = Db.Open(_cs);

        var id = await cn.ExecuteScalarAsync<int?>(
            "SELECT DepartmentId FROM dbo.Departments WHERE Name=@name",
            new { name });

        if (id.HasValue) return id.Value;

        return await cn.ExecuteScalarAsync<int>(
            "INSERT INTO dbo.Departments(Name) OUTPUT INSERTED.DepartmentId VALUES(@name)",
            new { name });
    }
}
