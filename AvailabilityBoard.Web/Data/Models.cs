namespace AvailabilityBoard.Web.Data;

public sealed record Department(int DepartmentId, string Name);

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

public sealed record AvailabilityType(int TypeId, string Code, string Label);

public sealed record RequestRow(
    long RequestId,
    int EmployeeId,
    string EmployeeName,
    int TypeId,
    string TypeCode,
    string TypeLabel,
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
    int employeeId
);
