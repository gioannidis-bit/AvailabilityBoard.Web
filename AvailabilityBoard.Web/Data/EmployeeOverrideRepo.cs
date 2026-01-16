using Dapper;

namespace AvailabilityBoard.Web.Data;

// Αυτό το record το χρησιμοποιεί ήδη το Employees.cshtml.cs (new EmployeeOverrideRow(...))
public sealed record EmployeeOverrideRow(
    int EmployeeId,
    int? DepartmentIdOverride,
    int? ManagerEmployeeIdOverride,
    bool? IsApproverOverride,
    bool? IsAdminOverride,
    bool? IsHiddenOverride = null
);

// Χρήσιμο για Get/GetAll (ίδιο shape)
public sealed record EmployeeOverride(
    int EmployeeId,
    int? DepartmentIdOverride,
    int? ManagerEmployeeIdOverride,
    bool? IsApproverOverride,
    bool? IsAdminOverride,
    bool? IsHiddenOverride = null
);

public sealed class EmployeeOverrideRepo
{
    private readonly string _cs;
    public EmployeeOverrideRepo(string cs) => _cs = cs;

    public async Task<EmployeeOverride?> Get(int employeeId)
    {
        using var cn = Db.Open(_cs);
        return await cn.QuerySingleOrDefaultAsync<EmployeeOverride>(@"
SELECT EmployeeId,
       DepartmentIdOverride,
       ManagerEmployeeIdOverride,
       IsApproverOverride,
       IsAdminOverride,
       IsHiddenOverride
FROM dbo.EmployeeOverrides
WHERE EmployeeId = @employeeId
", new { employeeId });
    }

    public async Task<List<EmployeeOverride>> GetAll()
    {
        using var cn = Db.Open(_cs);
        var rows = await cn.QueryAsync<EmployeeOverride>(@"
SELECT EmployeeId,
       DepartmentIdOverride,
       ManagerEmployeeIdOverride,
       IsApproverOverride,
       IsAdminOverride,
       IsHiddenOverride
FROM dbo.EmployeeOverrides
");
        return rows.ToList();
    }

    // ✅ Αυτό έλειπε: Upsert που ζητάει το Employees.cshtml.cs
    public async Task Upsert(EmployeeOverrideRow row)
    {
        using var cn = Db.Open(_cs);

        await cn.ExecuteAsync(@"
IF EXISTS (SELECT 1 FROM dbo.EmployeeOverrides WHERE EmployeeId = @EmployeeId)
BEGIN
    UPDATE dbo.EmployeeOverrides
       SET DepartmentIdOverride = @DepartmentIdOverride,
           ManagerEmployeeIdOverride = @ManagerEmployeeIdOverride,
           IsApproverOverride = @IsApproverOverride,
           IsAdminOverride = @IsAdminOverride,
           IsHiddenOverride = @IsHiddenOverride
     WHERE EmployeeId = @EmployeeId;
END
ELSE
BEGIN
    INSERT INTO dbo.EmployeeOverrides
        (EmployeeId, DepartmentIdOverride, ManagerEmployeeIdOverride, IsApproverOverride, IsAdminOverride, IsHiddenOverride)
    VALUES
        (@EmployeeId, @DepartmentIdOverride, @ManagerEmployeeIdOverride, @IsApproverOverride, @IsAdminOverride, @IsHiddenOverride);
END
", row);
    }

    // Χρησιμοποιείται από DepartmentMembers
    public async Task SetDepartmentOverride(int employeeId, int? departmentIdOverride)
    {
        using var cn = Db.Open(_cs);

        if (departmentIdOverride.HasValue)
        {
            await cn.ExecuteAsync(@"
IF EXISTS (SELECT 1 FROM dbo.EmployeeOverrides WHERE EmployeeId = @employeeId)
BEGIN
    UPDATE dbo.EmployeeOverrides
       SET DepartmentIdOverride = @departmentIdOverride
     WHERE EmployeeId = @employeeId;
END
ELSE
BEGIN
    INSERT INTO dbo.EmployeeOverrides(EmployeeId, DepartmentIdOverride)
    VALUES(@employeeId, @departmentIdOverride);
END
", new { employeeId, departmentIdOverride });

            return;
        }

        // Clear override
        await cn.ExecuteAsync(
            @"UPDATE dbo.EmployeeOverrides SET DepartmentIdOverride = NULL WHERE EmployeeId = @employeeId;",
            new { employeeId });

        // Αν δεν υπάρχει ΚΑΝΕΝΑ άλλο override, καθάρισε τη γραμμή
        await cn.ExecuteAsync(@"
DELETE FROM dbo.EmployeeOverrides
 WHERE EmployeeId = @employeeId
   AND DepartmentIdOverride IS NULL
   AND ManagerEmployeeIdOverride IS NULL
   AND IsApproverOverride IS NULL
   AND IsAdminOverride IS NULL
   AND IsHiddenOverride IS NULL;
", new { employeeId });
    }

    // (Optional helpers για μελλοντικό UI)
    public async Task SetManagerOverride(int employeeId, int? managerEmployeeIdOverride)
    {
        using var cn = Db.Open(_cs);

        if (managerEmployeeIdOverride.HasValue)
        {
            await cn.ExecuteAsync(@"
IF EXISTS (SELECT 1 FROM dbo.EmployeeOverrides WHERE EmployeeId = @employeeId)
BEGIN
    UPDATE dbo.EmployeeOverrides
       SET ManagerEmployeeIdOverride = @managerEmployeeIdOverride
     WHERE EmployeeId = @employeeId;
END
ELSE
BEGIN
    INSERT INTO dbo.EmployeeOverrides(EmployeeId, ManagerEmployeeIdOverride)
    VALUES(@employeeId, @managerEmployeeIdOverride);
END
", new { employeeId, managerEmployeeIdOverride });

            return;
        }

        await cn.ExecuteAsync(
            @"UPDATE dbo.EmployeeOverrides SET ManagerEmployeeIdOverride = NULL WHERE EmployeeId=@employeeId;",
            new { employeeId });

        await cn.ExecuteAsync(@"
DELETE FROM dbo.EmployeeOverrides
 WHERE EmployeeId = @employeeId
   AND DepartmentIdOverride IS NULL
   AND ManagerEmployeeIdOverride IS NULL
   AND IsApproverOverride IS NULL
   AND IsAdminOverride IS NULL
   AND IsHiddenOverride IS NULL;
", new { employeeId });
    }

    public async Task SetApproverOverride(int employeeId, bool? isApproverOverride)
    {
        using var cn = Db.Open(_cs);

        if (isApproverOverride.HasValue)
        {
            await cn.ExecuteAsync(@"
IF EXISTS (SELECT 1 FROM dbo.EmployeeOverrides WHERE EmployeeId = @employeeId)
BEGIN
    UPDATE dbo.EmployeeOverrides
       SET IsApproverOverride = @isApproverOverride
     WHERE EmployeeId = @employeeId;
END
ELSE
BEGIN
    INSERT INTO dbo.EmployeeOverrides(EmployeeId, IsApproverOverride)
    VALUES(@employeeId, @isApproverOverride);
END
", new { employeeId, isApproverOverride });

            return;
        }

        await cn.ExecuteAsync(
            @"UPDATE dbo.EmployeeOverrides SET IsApproverOverride = NULL WHERE EmployeeId=@employeeId;",
            new { employeeId });

        await cn.ExecuteAsync(@"
DELETE FROM dbo.EmployeeOverrides
 WHERE EmployeeId = @employeeId
   AND DepartmentIdOverride IS NULL
   AND ManagerEmployeeIdOverride IS NULL
   AND IsApproverOverride IS NULL
   AND IsAdminOverride IS NULL
   AND IsHiddenOverride IS NULL;
", new { employeeId });
    }

    public async Task SetAdminOverride(int employeeId, bool? isAdminOverride)
    {
        using var cn = Db.Open(_cs);

        if (isAdminOverride.HasValue)
        {
            await cn.ExecuteAsync(@"
IF EXISTS (SELECT 1 FROM dbo.EmployeeOverrides WHERE EmployeeId = @employeeId)
BEGIN
    UPDATE dbo.EmployeeOverrides
       SET IsAdminOverride = @isAdminOverride
     WHERE EmployeeId = @employeeId;
END
ELSE
BEGIN
    INSERT INTO dbo.EmployeeOverrides(EmployeeId, IsAdminOverride)
    VALUES(@employeeId, @isAdminOverride);
END
", new { employeeId, isAdminOverride });

            return;
        }

        await cn.ExecuteAsync(
            @"UPDATE dbo.EmployeeOverrides SET IsAdminOverride = NULL WHERE EmployeeId=@employeeId;",
            new { employeeId });

        await cn.ExecuteAsync(@"
DELETE FROM dbo.EmployeeOverrides
 WHERE EmployeeId = @employeeId
   AND DepartmentIdOverride IS NULL
   AND ManagerEmployeeIdOverride IS NULL
   AND IsApproverOverride IS NULL
   AND IsAdminOverride IS NULL
   AND IsHiddenOverride IS NULL;
", new { employeeId });
    }
}
