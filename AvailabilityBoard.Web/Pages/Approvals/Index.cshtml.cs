using AvailabilityBoard.Web.Data;
using AvailabilityBoard.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace AvailabilityBoard.Web.Pages.Approvals;

[Authorize]
public class IndexModel : PageModel
{
    private readonly Db _db;
    private readonly NotificationService _notif;

    public IndexModel(Db db, NotificationService notif)
    {
        _db = db;
        _notif = notif;
    }

    public List<RequestRow> Pending { get; set; } = new();
    public string? Message { get; set; }

    [BindProperty] public long RequestId { get; set; }
    [BindProperty] public string Action { get; set; } = "";
    [BindProperty] public string? DecisionNote { get; set; }

    public async Task OnGet()
    {
        var empId = User.GetEmployeeId();
        bool approverGroup = (User.FindFirstValue("is_approver_group") ?? "0") == "1";
        Pending = await _db.Requests.GetPendingForManager(empId, approverGroup);
    }

    public async Task<IActionResult> OnPost()
    {
        var empId = User.GetEmployeeId();
        bool approverGroup = (User.FindFirstValue("is_approver_group") ?? "0") == "1";

        var approve = string.Equals(Action, "approve", StringComparison.OrdinalIgnoreCase);
        await _db.Requests.Decide(RequestId, empId, approve, DecisionNote);

        // Notify employee (simple: look up request again not implemented; MVP approach: notify via generic)
        // For clean MVP, we skip detailed lookup here and just refresh list.
        Message = approve ? "Approved." : "Rejected.";

        Pending = await _db.Requests.GetPendingForManager(empId, approverGroup);
        return Page();
    }
}
