using Dapper;

namespace AvailabilityBoard.Web.Data;

public sealed class EmployeeRepo
{
    private readonly string _cs;
    public EmployeeRepo(string cs) => _cs = cs;

    public async Task<Employee?> GetByAdGuid(Guid adGuid)
    {
        using var cn = Db.Open(_cs);
        return await cn.QuerySingleOrDefaultAsync<Employee>(
            @"SELECT EmployeeId, AdGuid, SamAccountName, DisplayName, Email, DepartmentId, ManagerEmployeeId, IsActive, IsAdmin, IsApprover
              FROM dbo.Employees WHERE AdGuid=@adGuid",
            new { adGuid });
    }

    public async Task<Employee?> GetBySam(string sam)
    {
        using var cn = Db.Open(_cs);
        return await cn.QuerySingleOrDefaultAsync<Employee>(
            @"SELECT EmployeeId, AdGuid, SamAccountName, DisplayName, Email, DepartmentId, ManagerEmployeeId, IsActive, IsAdmin, IsApprover
              FROM dbo.Employees WHERE SamAccountName=@sam",
            new { sam });
    }

    public async Task<int> Upsert(Guid adGuid, string sam, string displayName, string? email, int? departmentId, bool isActive)
    {
        using var cn = Db.Open(_cs);

        // update if exists
        var existingId = await cn.ExecuteScalarAsync<int?>(
            "SELECT EmployeeId FROM dbo.Employees WHERE AdGuid=@adGuid",
            new { adGuid });

        if (existingId.HasValue)
        {
            await cn.ExecuteAsync(
                @"UPDATE dbo.Employees
                  SET SamAccountName=@sam, DisplayName=@displayName, Email=@email, DepartmentId=@departmentId,
                      IsActive=@isActive, LastSyncedAt=SYSUTCDATETIME()
                  WHERE EmployeeId=@id",
                new { id = existingId.Value, sam, displayName, email, departmentId, isActive });

            return existingId.Value;
        }

        // insert
        var newId = await cn.ExecuteScalarAsync<int>(
            @"INSERT INTO dbo.Employees(AdGuid, SamAccountName, DisplayName, Email, DepartmentId, IsActive)
              OUTPUT INSERTED.EmployeeId
              VALUES(@adGuid, @sam, @displayName, @email, @departmentId, @isActive)",
            new { adGuid, sam, displayName, email, departmentId, isActive });

        return newId;
    }

    public async Task SetManager(int employeeId, int? managerEmployeeId)
    {
        using var cn = Db.Open(_cs);
        await cn.ExecuteAsync(
            "UPDATE dbo.Employees SET ManagerEmployeeId=@m WHERE EmployeeId=@id",
            new { id = employeeId, m = managerEmployeeId });
    }

    public async Task<List<Employee>> GetDirectReports(int managerEmployeeId)
    {
        using var cn = Db.Open(_cs);
        var rows = await cn.QueryAsync<Employee>(
            @"SELECT EmployeeId, AdGuid, SamAccountName, DisplayName, Email, DepartmentId, ManagerEmployeeId, IsActive, IsAdmin, IsApprover
              FROM dbo.Employees
              WHERE ManagerEmployeeId=@managerEmployeeId AND IsActive=1
              ORDER BY DisplayName",
            new { managerEmployeeId });
        return rows.ToList();
    }

    public async Task<Employee?> GetById(int employeeId)
    {
        using var cn = Db.Open(_cs);
        return await cn.QuerySingleOrDefaultAsync<Employee>(
            @"SELECT EmployeeId, AdGuid, SamAccountName, DisplayName, Email, DepartmentId, ManagerEmployeeId, IsActive, IsAdmin, IsApprover
          FROM dbo.Employees WHERE EmployeeId=@employeeId",
            new { employeeId });
    }

    public async Task SetRoleFlags(int employeeId, bool isAdmin, bool isApprover)
    {
        using var cn = Db.Open(_cs);
        await cn.ExecuteAsync(
            @"UPDATE dbo.Employees SET IsAdmin=@isAdmin, IsApprover=@isApprover WHERE EmployeeId=@employeeId",
            new { employeeId, isAdmin, isApprover });
    }

    public async Task<List<Employee>> GetApprovers()
    {
        using var cn = Db.Open(_cs);
        var rows = await cn.QueryAsync<Employee>(
            @"SELECT EmployeeId, AdGuid, SamAccountName, DisplayName, Email, DepartmentId, ManagerEmployeeId, IsActive, IsAdmin, IsApprover
          FROM dbo.Employees WHERE IsActive=1 AND IsApprover=1
          ORDER BY DisplayName");
        return rows.ToList();
    }

    public async Task<List<Employee>> ListTop(int top = 200)
    {
        using var cn = Db.Open(_cs);
        var rows = await cn.QueryAsync<Employee>(
            $@"SELECT TOP (@top) EmployeeId, AdGuid, SamAccountName, DisplayName, Email,
                  DepartmentId, ManagerEmployeeId, IsActive, IsAdmin, IsApprover
           FROM dbo.Employees
           WHERE IsActive=1
           ORDER BY DisplayName",
            new { top });
        return rows.ToList();
    }

    public async Task<List<Employee>> Search(string q, int top = 50)
    {
        q = (q ?? "").Trim();
        using var cn = Db.Open(_cs);

        if (q.Length == 0)
            return await ListTop(top);

        // basic LIKE search (γρήγορο MVP). Αν έχεις πολλούς users, βάζουμε full-text μετά.
        var like = "%" + q.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";

        var rows = await cn.QueryAsync<Employee>(
            @"SELECT TOP (@top) EmployeeId, AdGuid, SamAccountName, DisplayName, Email,
                 DepartmentId, ManagerEmployeeId, IsActive, IsAdmin, IsApprover
          FROM dbo.Employees
          WHERE IsActive=1
            AND (DisplayName LIKE @like OR SamAccountName LIKE @like OR Email LIKE @like)
          ORDER BY DisplayName",
            new { like, top });
        return rows.ToList();
    }
}
