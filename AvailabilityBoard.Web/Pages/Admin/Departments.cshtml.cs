using AvailabilityBoard.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AvailabilityBoard.Web.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class DepartmentsModel : PageModel
{
    private readonly Db _db;
    public DepartmentsModel(Db db) => _db = db;

    public List<Department> Departments { get; set; } = new();

    // Αυτό θα το χρησιμοποιεί το view σου (όπως στο screenshot)
    public Dictionary<int, string> ManagerNameByDeptId { get; set; } = new();

    // Για να κρατάμε το ManagerEmployeeId ανά Department (χρησιμοποιείται από το typeahead hidden input)
    public Dictionary<int, int> ManagerIdByDeptId { get; set; } = new();

    // Αυτό θα το χρησιμοποιεί το view σου (foreach Model.Employees)
    public List<Employee> Employees { get; set; } = new();

    [BindProperty] public int DepartmentId { get; set; }
    [BindProperty] public int ManagerEmployeeId { get; set; }

    [TempData] public string? Message { get; set; }

    public async Task OnGet()
    {
        Departments = await _db.Departments.GetAll();
        Employees = await _db.Employees.ListTop(1000);

        // map EmployeeId -> DisplayName
        var empName = Employees.ToDictionary(e => e.EmployeeId, e => e.DisplayName);

        // bulk load dept managers
        var managers = await _db.DepartmentManagers.GetAll();
        foreach (var row in managers)
        {
            ManagerIdByDeptId[row.DepartmentId] = row.ManagerEmployeeId;
            if (empName.TryGetValue(row.ManagerEmployeeId, out var name))
                ManagerNameByDeptId[row.DepartmentId] = name;
            else
                ManagerNameByDeptId[row.DepartmentId] = $"EmployeeId={row.ManagerEmployeeId}";
        }
    }

    public async Task<IActionResult> OnPost()
    {
        if (ManagerEmployeeId <= 0)
        {
            Message = "Please select a manager before saving.";
            return RedirectToPage();
        }
        await _db.DepartmentManagers.Upsert(DepartmentId, ManagerEmployeeId);
        Message = "Saved.";
        return RedirectToPage();
    }
}
