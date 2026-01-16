using AvailabilityBoard.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AvailabilityBoard.Web.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class DepartmentMembersModel : PageModel
{
    private readonly Db _db;
    public DepartmentMembersModel(Db db) => _db = db;

    [BindProperty(SupportsGet = true)]
    public int DepartmentId { get; set; }

    public Department? Department { get; set; }
    public List<Employee> Employees { get; set; } = new();

    // employees που είναι "effective" μέλη αυτού του dept (override or base)
    public HashSet<int> SelectedEmployeeIds { get; set; } = new();

    // POST payload: list of checked employees
    [BindProperty]
    public List<int> MemberEmployeeIds { get; set; } = new();

    [TempData] public string? Message { get; set; }

    public async Task<IActionResult> OnGet()
    {
        if (DepartmentId <= 0) return RedirectToPage("/Admin/Departments");

        Department = await _db.Departments.GetById(DepartmentId);
        if (Department == null)
        {
            Message = "Department not found.";
            return RedirectToPage("/Admin/Departments");
        }

        Employees = await _db.Employees.ListTop(5000);

        // overrides (bulk)
        var overrides = await _db.Overrides.GetAll(); // χρειάζεται να υπάρχει στο EmployeeOverrideRepo (αν δεν υπάρχει -> επόμενο βήμα)
        var ovrByEmp = overrides.ToDictionary(x => x.EmployeeId, x => x);

        foreach (var e in Employees)
        {
            var effectiveDeptId = e.DepartmentId;

            if (ovrByEmp.TryGetValue(e.EmployeeId, out var o))
            {
                if (o.DepartmentIdOverride.HasValue)
                    effectiveDeptId = o.DepartmentIdOverride;
            }

            if (effectiveDeptId.HasValue && effectiveDeptId.Value == DepartmentId)
                SelectedEmployeeIds.Add(e.EmployeeId);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSave()
    {
        if (DepartmentId <= 0) return RedirectToPage("/Admin/Departments");

        Department = await _db.Departments.GetById(DepartmentId);
        if (Department == null)
        {
            Message = "Department not found.";
            return RedirectToPage("/Admin/Departments");
        }

        Employees = await _db.Employees.ListTop(5000);

        // current effective members
        var overrides = await _db.Overrides.GetAll(); // χρειάζεται να υπάρχει
        var ovrByEmp = overrides.ToDictionary(x => x.EmployeeId, x => x);

        var currentMembers = new HashSet<int>();
        foreach (var e in Employees)
        {
            var effectiveDeptId = e.DepartmentId;

            if (ovrByEmp.TryGetValue(e.EmployeeId, out var o))
            {
                if (o.DepartmentIdOverride.HasValue)
                    effectiveDeptId = o.DepartmentIdOverride;
            }

            if (effectiveDeptId.HasValue && effectiveDeptId.Value == DepartmentId)
                currentMembers.Add(e.EmployeeId);
        }

        var newMembers = new HashSet<int>(MemberEmployeeIds ?? new List<int>());

        // Apply changes:
        // - όσοι μπήκαν -> set override DepartmentIdOverride = DepartmentId
        // - όσοι βγήκαν -> remove override (set null) ΜΟΝΟ αν ήταν override-based.
        foreach (var empId in newMembers)
        {
            if (!currentMembers.Contains(empId))
            {
                await _db.Overrides.SetDepartmentOverride(empId, DepartmentId); // χρειάζεται να υπάρχει
            }
        }

        foreach (var empId in currentMembers)
        {
            if (!newMembers.Contains(empId))
            {
                // Αν ο employee ανήκει by base AD dept στο DepartmentId,
                // δεν πρέπει να τον "πετάξουμε" αλλού. Άρα:
                // - αν έχει override προς αυτό το dept => το καθαρίζουμε (null)
                // - αλλιώς δεν κάνουμε τίποτα (μένει member από AD)
                if (ovrByEmp.TryGetValue(empId, out var o)
                    && o.DepartmentIdOverride.HasValue
                    && o.DepartmentIdOverride.Value == DepartmentId)
                {
                    await _db.Overrides.SetDepartmentOverride(empId, null);
                }
            }
        }

        Message = "Saved members.";
        return RedirectToPage(new { DepartmentId });
    }
}
