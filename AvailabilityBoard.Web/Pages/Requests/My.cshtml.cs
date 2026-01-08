using AvailabilityBoard.Web.Data;
using AvailabilityBoard.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace AvailabilityBoard.Web.Pages.Requests;

[Authorize]
public class MyModel : PageModel
{
    private readonly Db _db;
    private readonly NotificationService _notif;

    public MyModel(Db db, NotificationService notif)
    {
        _db = db;
        _notif = notif;
    }

    public List<AvailabilityType> Types { get; set; } = new();
    public List<RequestRow> MyRequests { get; set; } = new();

    [BindProperty] public string TypeCode { get; set; } = "OFFICE";
    [BindProperty] public DateTime Start { get; set; }
    [BindProperty] public DateTime End { get; set; }
    [BindProperty] public string? Note { get; set; }

    public string? Error { get; set; }
    public string? Success { get; set; }

    public async Task OnGet()
    {
        Types = await _db.Types.GetAll();
        MyRequests = await _db.Requests.GetMyRequests(User.GetEmployeeId());
    }

    public async Task<IActionResult> OnPost()
    {
        try
        {
            Types = await _db.Types.GetAll();
            var t = await _db.Types.GetByCode(TypeCode.Trim().ToUpperInvariant());
            if (t == null) throw new Exception("Unknown type");

            var empId = User.GetEmployeeId();
            var me = await _db.Employees.GetBySam(User.FindFirstValue("sam") ?? "")
                     ?? throw new Exception("Employee not synced");

            // route to manager if exists, else null (can be handled by approver group later)
            var ov = await _db.Overrides.Get(empId);

            // effective department
            var effectiveDeptId = ov?.DepartmentIdOverride ?? me.DepartmentId;

            // approver routing
            int? approverId =
                ov?.ManagerEmployeeIdOverride
                ?? (effectiveDeptId.HasValue ? await _db.DepartmentManagers.GetManagerEmployeeId(effectiveDeptId.Value) : null)
                ?? me.ManagerEmployeeId;

            var reqId = await _db.Requests.CreateRequest(empId, t.TypeId, Start, End, Note, approverId);

            if (approverId.HasValue)
            {
                await _notif.Notify(
                    approverId.Value,
                    "Νέο αίτημα διαθεσιμότητας",
                    $"{me.DisplayName}: {t.Label} ({Start:g} – {End:g})",
                    "/Approvals",
                    null
                );
            }
            else
            {
                // fallback: ενημέρωσε όλους τους approvers (ώστε να μη χάνεται)
                var approvers = await _db.Employees.GetApprovers();
                await _notif.NotifyMany(
                    approvers.Select(a => (a.EmployeeId, a.Email)),
                    "Νέο αίτημα διαθεσιμότητας (χωρίς manager)",
                    $"{me.DisplayName}: {t.Label} ({Start:g} – {End:g})",
                    "/Approvals"
                );
            }

            Success = $"Υποβλήθηκε αίτημα (ID {reqId}).";
            MyRequests = await _db.Requests.GetMyRequests(empId);
            return Page();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            MyRequests = await _db.Requests.GetMyRequests(User.GetEmployeeId());
            return Page();
        }
    }
}
