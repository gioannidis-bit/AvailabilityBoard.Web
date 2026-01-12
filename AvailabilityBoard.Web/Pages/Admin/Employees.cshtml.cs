using AvailabilityBoard.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AvailabilityBoard.Web.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class EmployeesModel : PageModel
{
    private readonly Db _db;

    public EmployeesModel(Db db) => _db = db;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int? EmployeeId { get; set; }

    public List<Employee> Results { get; set; } = new();
    public Employee? Selected { get; set; }

    public List<Department> Departments { get; set; } = new();
    public List<Employee> EmployeeChoices { get; set; } = new();

    // Για να δείχνουμε όνομα manager override στο textbox (typeahead)
    public string? ManagerOverrideDisplay { get; set; }

    // Override fields
    [BindProperty] public int EmployeeIdPost { get; set; }  // not used; we use EmployeeId from form
    [BindProperty] public int EmployeeIdForm { get; set; }
    [BindProperty] public int? DepartmentIdOverride { get; set; }
    [BindProperty] public int? ManagerEmployeeIdOverride { get; set; }
    [BindProperty] public bool? IsAdminOverride { get; set; }
    [BindProperty] public bool? IsApproverOverride { get; set; }

    // Effective role toggles
    [BindProperty] public bool IsAdmin { get; set; }
    [BindProperty] public bool IsApprover { get; set; }

    public bool EffectiveIsAdmin { get; set; }
    public bool EffectiveIsApprover { get; set; }

    public string? Message { get; set; }

    public async Task OnGet()
    {
        Results = await _db.Employees.Search(Q ?? "", 80);
        Departments = await _db.Departments.GetAll();
        EmployeeChoices = await _db.Employees.ListTop(400);

        if (EmployeeId.HasValue)
        {
            Selected = await _db.Employees.GetById(EmployeeId.Value);
            if (Selected != null)
            {
                var ov = await _db.Overrides.Get(Selected.EmployeeId);
                DepartmentIdOverride = ov?.DepartmentIdOverride;
                ManagerEmployeeIdOverride = ov?.ManagerEmployeeIdOverride;
                IsAdminOverride = ov?.IsAdminOverride;
                IsApproverOverride = ov?.IsApproverOverride;

                // Effective roles = base flags + override if exists
                EffectiveIsAdmin = ov?.IsAdminOverride ?? Selected.IsAdmin;
                EffectiveIsApprover = ov?.IsApproverOverride ?? Selected.IsApprover;

                // display string για typeahead textbox
                if (ManagerEmployeeIdOverride.HasValue)
                {
                    var mgr = EmployeeChoices.FirstOrDefault(x => x.EmployeeId == ManagerEmployeeIdOverride.Value)
                              ?? await _db.Employees.GetById(ManagerEmployeeIdOverride.Value);
                    if (mgr != null)
                        ManagerOverrideDisplay = $"{mgr.DisplayName} ({mgr.SamAccountName})";
                }
            }
        }
    }

    public async Task<IActionResult> OnPost(
        [FromForm] int EmployeeId,
        [FromForm] string? Q,
        [FromForm] bool? IsAdmin,
        [FromForm] bool? IsApprover,
        [FromForm] int? DepartmentIdOverride,
        [FromForm] int? ManagerEmployeeIdOverride,
        [FromForm] bool? IsAdminOverride,
        [FromForm] bool? IsApproverOverride
    )
    {
        // normalize 0 => null (από typeahead hidden input)
        if (ManagerEmployeeIdOverride.HasValue && ManagerEmployeeIdOverride.Value <= 0)
            ManagerEmployeeIdOverride = null;

        // Save base role flags (from switches)
        await _db.Employees.SetRoleFlags(EmployeeId, IsAdmin == true, IsApprover == true);

        // Save overrides row (even if all null, we keep a row; you can choose to delete instead)
        await _db.Overrides.Upsert(new EmployeeOverrideRow(
            EmployeeId: EmployeeId,
            DepartmentIdOverride: DepartmentIdOverride,
            ManagerEmployeeIdOverride: ManagerEmployeeIdOverride,
            IsApproverOverride: IsApproverOverride,
            IsAdminOverride: IsAdminOverride
        ));

        return Redirect($"/Admin/Employees?employeeId={EmployeeId}&q={Uri.EscapeDataString(Q ?? "")}&saved=1");
    }
}
