using AvailabilityBoard.Web.Data;
using AvailabilityBoard.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AvailabilityBoard.Web.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class IndexModel : PageModel
{
    private readonly LdapService _ldap;
    private readonly EmployeeSyncService _sync;
    private readonly Db _db;
    private readonly IConfiguration _cfg;

    public IndexModel(LdapService ldap, EmployeeSyncService sync, Db db, IConfiguration cfg)
    {
        _ldap = ldap;
        _sync = sync;
        _db = db;
        _cfg = cfg;
    }

    [BindProperty] public string Action { get; set; } = "";
    public string? Message { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPost()
    {
        if (Action == "sync")
        {
            var adminGroups = _cfg.GetSection("Roles:AdminGroups").Get<string[]>() ?? Array.Empty<string>();
            var approverGroups = _cfg.GetSection("Roles:ApproverGroups").Get<string[]>() ?? Array.Empty<string>();

            bool InGroup(List<string> memberOf, string[] groups) =>
                groups.Any(g => memberOf.Any(dn => dn.Contains("CN=" + g + ",", StringComparison.OrdinalIgnoreCase)));

            int count = 0;
            foreach (var u in _ldap.FetchAllUsers())
            {
                var empId = await _sync.UpsertFromAd(u);

                var isAdmin = InGroup(u.MemberOf, adminGroups);
                var isApprover = InGroup(u.MemberOf, approverGroups);

                await _db.Employees.SetRoleFlags(empId, isAdmin, isApprover);
                count++;
            }

            Message = $"Sync OK. Users synced: {count}.";
        }

        return Page();
    }
}
