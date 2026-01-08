using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AvailabilityBoard.Web.Pages;

public class LogoutModel : PageModel
{
    public async Task OnGet()
    {
        await HttpContext.SignOutAsync();
        Response.Redirect("/Login");
    }
}
