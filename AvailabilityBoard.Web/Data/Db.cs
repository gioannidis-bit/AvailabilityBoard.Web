using Microsoft.Data.SqlClient;

namespace AvailabilityBoard.Web.Data;

public sealed class Db
{
    private readonly string _cs;
    public Db(IConfiguration cfg)
    {
        _cs = cfg.GetConnectionString("Default") ?? throw new Exception("Missing connection string Default");
        Departments = new DepartmentRepo(_cs);
        Employees = new EmployeeRepo(_cs);
        Types = new AvailabilityTypeRepo(_cs);
        Requests = new RequestRepo(_cs);
        Notifications = new NotificationRepo(_cs);
        DepartmentManagers = new DepartmentManagerRepo(_cs);
        Overrides = new EmployeeOverrideRepo(_cs);
        SyncLogs = new AdSyncLogRepo(_cs);
        DepartmentAccess = new DepartmentAccessRepo(_cs);

        // ✅ Scheduling
        Schedules = new ScheduleRepo(_cs);
    }

    public DepartmentRepo Departments { get; }
    public EmployeeRepo Employees { get; }
    public AvailabilityTypeRepo Types { get; }
    public RequestRepo Requests { get; }
    public NotificationRepo Notifications { get; }
    public DepartmentManagerRepo DepartmentManagers { get; }
    public EmployeeOverrideRepo Overrides { get; }
    public AdSyncLogRepo SyncLogs { get; }
    public DepartmentAccessRepo DepartmentAccess { get; }

    // ✅ Scheduling
    public ScheduleRepo Schedules { get; }

    public static SqlConnection Open(string cs)
    {
        var cn = new SqlConnection(cs);
        cn.Open();
        return cn;
    }
}
