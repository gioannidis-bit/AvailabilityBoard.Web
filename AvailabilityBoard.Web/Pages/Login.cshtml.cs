using AvailabilityBoard.Web.Data;
using AvailabilityBoard.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace AvailabilityBoard.Web.Pages;

public class LoginModel : PageModel
{
    private readonly LdapService _ldap;
    private readonly EmployeeSyncService _sync;
    private readonly Db _db;
    private readonly IConfiguration _cfg;

    public LoginModel(LdapService ldap, EmployeeSyncService sync, Db db, IConfiguration cfg)
    {
        _ldap = ldap;
        _sync = sync;
        _db = db;
        _cfg = cfg;
    }

    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public string? Error { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPost()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                Error = "Δώσε username/password.";
                return Page();
            }

            var adUser = _ldap.AuthenticateAndFetchUser(Username.Trim(), Password);

            // Upsert employee and department
            var empId = await _sync.UpsertFromAd(adUser);

            // Try link manager (optional)
            if (!string.IsNullOrWhiteSpace(adUser.ManagerDn))
            {
                // For MVP we reuse user's own credentials to resolve manager DN.
                // In production, better use a service account in config.
                var mgr = _ldap.FetchUserByDn(Username.Trim(), Password, adUser.ManagerDn);
                if (mgr != null)
                {
                    var mgrId = await _sync.UpsertFromAd(mgr);
                    await _db.Employees.SetManager(empId, mgrId);
                }
            }

            // Roles by AD group membership (simple)
            bool isAdmin = IsInAnyConfiguredGroup(adUser.MemberOf, "Roles:AdminGroups");
            bool isApproverGroup = IsInAnyConfiguredGroup(adUser.MemberOf, "Roles:ApproverGroups");

            var claims = new List<Claim>
            {
                new Claim("emp_id", empId.ToString()),
                new Claim(ClaimTypes.Name, adUser.DisplayName),
                new Claim("sam", adUser.SamAccountName),
                new Claim("is_admin", isAdmin ? "1" : "0"),
                new Claim("is_approver_group", isApproverGroup ? "1" : "0")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return RedirectToPage("/Index");
        }
        catch (Exception ex)
        {
            Error = "Login failed: " + ex.Message;
            return Page();
        }
    }

    private bool IsInAnyConfiguredGroup(List<string> memberOfDns, string configPath)
    {
        var groups = _cfg.GetSection(configPath).Get<string[]>() ?? Array.Empty<string>();
        if (groups.Length == 0) return false;

        // memberOf contains DNs; we match by CN=GroupName
        foreach (var g in groups)
        {
            var needle = "CN=" + g + ",";
            if (memberOfDns.Any(dn => dn.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }
}
