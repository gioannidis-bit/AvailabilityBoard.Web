using Dapper;

namespace AvailabilityBoard.Web.Data;

public sealed class AvailabilityTypeRepo
{
    private readonly string _cs;
    public AvailabilityTypeRepo(string cs) => _cs = cs;

    public async Task<List<AvailabilityType>> GetAll()
    {
        using var cn = Db.Open(_cs);
        var rows = await cn.QueryAsync<AvailabilityType>(
            @"SELECT TypeId, Code, Label, ColorHex, IconClass, SortOrder 
              FROM dbo.AvailabilityTypes 
              ORDER BY SortOrder, Label");
        return rows.ToList();
    }

    public async Task<AvailabilityType?> GetByCode(string code)
    {
        using var cn = Db.Open(_cs);
        return await cn.QuerySingleOrDefaultAsync<AvailabilityType>(
            @"SELECT TypeId, Code, Label, ColorHex, IconClass, SortOrder 
              FROM dbo.AvailabilityTypes WHERE Code=@code",
            new { code });
    }

    public async Task<AvailabilityType?> GetById(int typeId)
    {
        using var cn = Db.Open(_cs);
        return await cn.QuerySingleOrDefaultAsync<AvailabilityType>(
            @"SELECT TypeId, Code, Label, ColorHex, IconClass, SortOrder 
              FROM dbo.AvailabilityTypes WHERE TypeId=@typeId",
            new { typeId });
    }

    public async Task UpdateColor(int typeId, string colorHex)
    {
        using var cn = Db.Open(_cs);
        await cn.ExecuteAsync(
            "UPDATE dbo.AvailabilityTypes SET ColorHex=@colorHex WHERE TypeId=@typeId",
            new { typeId, colorHex });
    }

    public async Task Upsert(string code, string label, string colorHex, int sortOrder)
    {
        using var cn = Db.Open(_cs);
        await cn.ExecuteAsync(@"
IF EXISTS (SELECT 1 FROM dbo.AvailabilityTypes WHERE Code=@code)
    UPDATE dbo.AvailabilityTypes SET Label=@label, ColorHex=@colorHex, SortOrder=@sortOrder WHERE Code=@code
ELSE
    INSERT INTO dbo.AvailabilityTypes(Code, Label, ColorHex, SortOrder) VALUES(@code, @label, @colorHex, @sortOrder)
", new { code, label, colorHex, sortOrder });
    }
}
