namespace AvailabilityBoard.Web.Data;

public sealed record Department(
    int DepartmentId, 
    string Name,
    string? ColorHex = null,
    bool IsActive = true,
    int? DefaultApproverEmployeeId = null,
    int SortOrder = 0
);

public sealed record Employee(
    int EmployeeId,
    Guid AdGuid,
    string SamAccountName,
    string DisplayName,
    string? Email,
    int? DepartmentId,
    int? ManagerEmployeeId,
    bool IsActive,
    bool IsAdmin,
    bool IsApprover
);

public sealed record AvailabilityType(
    int TypeId, 
    string Code, 
    string Label,
    string ColorHex = "#6c757d",
    string? IconClass = null,
    int SortOrder = 0
);

public sealed record RequestRow(
    long RequestId,
    int EmployeeId,
    string EmployeeName,
    int TypeId,
    string TypeCode,
    string TypeLabel,
    string TypeColorHex,
    DateTime StartDateTime,
    DateTime EndDateTime,
    string Status,
    string? Note,
    int? ApproverEmployeeId,
    string? ApproverName,
    DateTime SubmittedAt,
    DateTime? DecisionAt,
    string? DecisionNote
);

public sealed record CalendarEvent(
    long id,
    string title,
    DateTime start,
    DateTime end,
    string typeCode,
    string color,
    int employeeId,
    string employeeName,
    string? note
);

// Για το Today's Snapshot
public sealed record AvailabilitySnapshot(
    string TypeCode,
    string TypeLabel,
    string ColorHex,
    int Count,
    List<SnapshotEmployee> Employees
);

public sealed record SnapshotEmployee(
    int EmployeeId,
    string DisplayName,
    string Initials,
    int? DepartmentId,
    DateTime? ReturnsAt
);
