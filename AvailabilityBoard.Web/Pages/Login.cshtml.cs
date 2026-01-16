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
    public string? ReturnUrl { get; set; }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPost(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? "/";

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            Error = "Username and password are required.";
            return Page();
        }

        try
        {
            // 1. Authenticate against AD
            var adUser = _ldap.AuthenticateAndFetchUser(Username, Password);

            // 2. Upsert in DB
            var empId = await _sync.UpsertFromAd(adUser);

            // 3. Get employee record with overrides
            var emp = await _db.Employees.GetById(empId);
            if (emp == null)
            {
                Error = "Employee record not found.";
                return Page();
            }

            // 4. Apply overrides
            var ovr = await _db.Overrides.Get(empId);
            var effectiveDeptId = ovr?.DepartmentIdOverride ?? emp.DepartmentId;
            var effectiveIsAdmin = ovr?.IsAdminOverride ?? emp.IsAdmin;
            var effectiveIsApprover = ovr?.IsApproverOverride ?? emp.IsApprover;

            // 5. Check if user is a department manager
            var managedDepts = await _db.DepartmentManagers.GetManagedDepartmentIds(empId);
            var isDeptManager = managedDepts.Count > 0;

            // 6. Check approver group membership
            var approverGroups = _cfg.GetSection("Roles:ApproverGroups").Get<string[]>() ?? Array.Empty<string>();
            var isInApproverGroup = approverGroups.Any(g =>
                adUser.MemberOf.Any(dn => dn.Contains("CN=" + g + ",", StringComparison.OrdinalIgnoreCase)));

            // 7. Build claims
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, emp.DisplayName),
                new(ClaimTypes.NameIdentifier, emp.AdGuid.ToString()),
                new("employee_id", empId.ToString()),
                new("sam", emp.SamAccountName),
                new("is_admin", effectiveIsAdmin ? "1" : "0"),
                new("is_approver", effectiveIsApprover ? "1" : "0"),
                new("is_approver_group", isInApproverGroup ? "1" : "0"),
                new("is_dept_manager", isDeptManager ? "1" : "0"),
            };

            if (effectiveDeptId.HasValue)
                claims.Add(new Claim("department_id", effectiveDeptId.Value.ToString()));

            if (!string.IsNullOrEmpty(emp.Email))
                claims.Add(new Claim(ClaimTypes.Email, emp.Email));

            // 8. Sign in
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
                });

            // 9. Try to link manager (if not already linked)
            if (!string.IsNullOrEmpty(adUser.ManagerDn) && emp.ManagerEmployeeId == null)
            {
                try
                {
                    var mgrUser = _ldap.FetchUserByDn(Username, Password, adUser.ManagerDn);
                    if (mgrUser != null)
                    {
                        var mgr = await _db.Employees.GetByAdGuid(mgrUser.AdGuid);
                        if (mgr != null)
                            await _db.Employees.SetManager(empId, mgr.EmployeeId);
                    }
                }
                catch
                {
                    // Ignore manager linking errors
                }
            }

            return LocalRedirect(ReturnUrl);
        }
        catch (Exception ex)
        {
            Error = ex.Message.Contains("LDAP") || ex.Message.Contains("credentials")
                ? "Invalid username or password."
                : ex.Message;

            return Page();
        }
    }
}
