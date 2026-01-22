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

    // Αυτό θα το χρησιμοποιεί το view σου
    public Dictionary<int, string> ManagerNameByDeptId { get; set; } = new();

    // Για να κρατάμε το ManagerEmployeeId ανά Department (typeahead hidden input)
    public Dictionary<int, int> ManagerIdByDeptId { get; set; } = new();

    // Για typeahead (mgr picker)
    public List<Employee> Employees { get; set; } = new();

    // -------- Existing: Set Manager --------
    [BindProperty] public int DepartmentId { get; set; }
    [BindProperty] public int ManagerEmployeeId { get; set; }

    // -------- NEW: Create Department --------
    [BindProperty] public string? NewDepartmentName { get; set; }
    [BindProperty] public string? NewDepartmentColorHex { get; set; }
    [BindProperty] public int NewDepartmentSortOrder { get; set; } = 0;
    [BindProperty] public bool NewDepartmentIsActive { get; set; } = true;

    [TempData] public string? Message { get; set; }

    public async Task OnGet()
    {
        // Admin βλέπει και inactive
        Departments = await _db.Departments.GetAllIncludingInactive();

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

    // Existing POST: Set manager (κρατάμε όπως είναι για να μη σπάσει τίποτα)
    public async Task<IActionResult> OnPost()
    {
        if (DepartmentId <= 0)
        {
            Message = "Invalid DepartmentId.";
            return RedirectToPage();
        }

        if (ManagerEmployeeId <= 0)
        {
            Message = "Please select a manager before saving.";
            return RedirectToPage();
        }

        await _db.DepartmentManagers.Upsert(DepartmentId, ManagerEmployeeId);
        Message = "Saved manager.";
        return RedirectToPage();
    }

    // NEW POST handler: Create Department
    public async Task<IActionResult> OnPostCreate()
    {
        var name = (NewDepartmentName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "Department name is required.";
            return RedirectToPage();
        }

        string? color = (NewDepartmentColorHex ?? "").Trim();
        if (string.IsNullOrWhiteSpace(color))
            color = null;

        try
        {
            var id = await _db.Departments.Create(
                name: name,
                colorHex: color,
                defaultApproverEmployeeId: null,
                sortOrder: NewDepartmentSortOrder,
                isActive: NewDepartmentIsActive
            );

            Message = $"Created department: {name} (Id={id})";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }

        return RedirectToPage();
    }

    // NEW POST handler: Toggle Active
    // Flip server-side για να μη βασιζόμαστε σε bool parsing από hidden inputs.
    public async Task<IActionResult> OnPostToggleActive(int departmentId)
    {
        if (departmentId <= 0)
        {
            Message = "Invalid DepartmentId.";
            return RedirectToPage();
        }

        var dept = await _db.Departments.GetById(departmentId);
        if (dept is null)
        {
            Message = $"Department not found (Id={departmentId}).";
            return RedirectToPage();
        }

        var newState = !dept.IsActive;
        var rows = await _db.Departments.SetActive(departmentId, newState);
        Message = rows > 0
            ? $"Updated department status: {(newState ? "Active" : "Inactive")}."
            : "No rows updated. Check DepartmentId/DB.";
        return RedirectToPage();
    }
}
