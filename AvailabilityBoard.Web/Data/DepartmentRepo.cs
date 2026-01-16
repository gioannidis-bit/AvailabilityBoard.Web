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
            @"SELECT DepartmentId, Name, ColorHex, IsActive, DefaultApproverEmployeeId, SortOrder
              FROM dbo.Departments
              WHERE IsActive = 1
              ORDER BY SortOrder, Name");
        return rows.ToList();
    }

    // ΝΕΟ: για Admin screens (να δείχνει και inactive)
    public async Task<List<Department>> GetAllIncludingInactive()
    {
        using var cn = Db.Open(_cs);
        var rows = await cn.QueryAsync<Department>(
            @"SELECT DepartmentId, Name, ColorHex, IsActive, DefaultApproverEmployeeId, SortOrder
              FROM dbo.Departments
              ORDER BY SortOrder, Name");
        return rows.ToList();
    }

    public async Task<Department?> GetById(int departmentId)
    {
        using var cn = Db.Open(_cs);
        return await cn.QuerySingleOrDefaultAsync<Department>(
            @"SELECT DepartmentId, Name, ColorHex, IsActive, DefaultApproverEmployeeId, SortOrder
              FROM dbo.Departments WHERE DepartmentId=@departmentId",
            new { departmentId });
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

    // ΝΕΟ: Create department in-app (για admin UI)
    public async Task<int> Create(string name, string? colorHex, int? defaultApproverEmployeeId, int sortOrder, bool isActive = true)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Department name is required");

        using var cn = Db.Open(_cs);

        // Απλό anti-duplicate
        var exists = await cn.ExecuteScalarAsync<int?>(
            "SELECT DepartmentId FROM dbo.Departments WHERE Name=@name",
            new { name });

        if (exists.HasValue)
            throw new InvalidOperationException($"Department '{name}' already exists.");

        var id = await cn.ExecuteScalarAsync<int>(
            @"INSERT INTO dbo.Departments(Name, ColorHex, IsActive, DefaultApproverEmployeeId, SortOrder)
              OUTPUT INSERTED.DepartmentId
              VALUES(@name, @colorHex, @isActive, @defaultApproverEmployeeId, @sortOrder)",
            new
            {
                name,
                colorHex,
                isActive = isActive ? 1 : 0,
                defaultApproverEmployeeId,
                sortOrder
            });

        return id;
    }

    public async Task Update(int departmentId, string name, string? colorHex, int? defaultApproverEmployeeId, int sortOrder)
    {
        using var cn = Db.Open(_cs);
        await cn.ExecuteAsync(
            @"UPDATE dbo.Departments
              SET Name=@name, ColorHex=@colorHex, DefaultApproverEmployeeId=@defaultApproverEmployeeId, SortOrder=@sortOrder
              WHERE DepartmentId=@departmentId",
            new { departmentId, name, colorHex, defaultApproverEmployeeId, sortOrder });
    }

    public async Task SetActive(int departmentId, bool isActive)
    {
        using var cn = Db.Open(_cs);
        await cn.ExecuteAsync(
            "UPDATE dbo.Departments SET IsActive=@isActive WHERE DepartmentId=@departmentId",
            new { departmentId, isActive });
    }
}
