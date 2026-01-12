using AvailabilityBoard.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AvailabilityBoard.Web.Pages.Notifications;

[Authorize]
public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    public List<dynamic> Items { get; set; } = new();

    public async Task OnGet()
    {
        Items = await _db.Notifications.GetLatest(User.GetEmployeeId(), 30);
    }

    public async Task<IActionResult> OnPost([FromForm] long NotificationId)
    {
        await _db.Notifications.MarkRead(NotificationId, User.GetEmployeeId());
        return RedirectToPage();
    }
}
