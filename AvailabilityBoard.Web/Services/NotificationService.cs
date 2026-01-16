using AvailabilityBoard.Web.Data;

namespace AvailabilityBoard.Web.Services;

public sealed class NotificationService
{
    private readonly Db _db;
    private readonly EmailSender _email;

    public NotificationService(Db db, EmailSender email)
    {
        _db = db;
        _email = email;
    }

    public async Task Notify(int toEmployeeId, string title, string body, string? url, string? emailTo = null)
    {
        await _db.Notifications.Add(toEmployeeId, title, body, url);
        if (!string.IsNullOrWhiteSpace(emailTo))
            await _email.TrySend(emailTo, title, body);
    }

    public async Task NotifyMany(IEnumerable<(int empId, string? email)> targets, string title, string body, string? url)
    {
        foreach (var t in targets)
            await Notify(t.empId, title, body, url, t.email);
    }

    /// <summary>
    /// Notify the approver when a new request is submitted
    /// </summary>
    public async Task NotifyNewRequest(int approverEmployeeId, string employeeName, string typeLabel, DateTime start, DateTime end)
    {
        var approver = await _db.Employees.GetById(approverEmployeeId);
        if (approver == null) return;

        var title = $"Νέο αίτημα: {employeeName}";
        var body = $"{employeeName} υπέβαλε αίτημα για {typeLabel} από {start:d} έως {end:d}";

        await Notify(approverEmployeeId, title, body, "/Approvals", approver.Email);
    }

    /// <summary>
    /// Notify the employee when their request is decided
    /// </summary>
    public async Task NotifyRequestDecision(int employeeId, bool approved, string? decisionNote, string approverName)
    {
        var emp = await _db.Employees.GetById(employeeId);
        if (emp == null) return;

        var status = approved ? "εγκρίθηκε" : "απορρίφθηκε";
        var title = $"Το αίτημά σας {status}";
        var body = $"Το αίτημά σας {status} από {approverName}.";

        if (!string.IsNullOrWhiteSpace(decisionNote))
            body += $" Σχόλιο: {decisionNote}";

        await Notify(employeeId, title, body, "/Requests/My", emp.Email);
    }
}
