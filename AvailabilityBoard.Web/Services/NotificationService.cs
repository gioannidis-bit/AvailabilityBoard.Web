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
}
