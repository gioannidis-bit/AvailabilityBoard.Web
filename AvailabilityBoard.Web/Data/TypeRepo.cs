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
            "SELECT TypeId, Code, Label FROM dbo.AvailabilityTypes ORDER BY Label");
        return rows.ToList();
    }

    public async Task<AvailabilityType?> GetByCode(string code)
    {
        using var cn = Db.Open(_cs);
        return await cn.QuerySingleOrDefaultAsync<AvailabilityType>(
            "SELECT TypeId, Code, Label FROM dbo.AvailabilityTypes WHERE Code=@code",
            new { code });
    }
}
