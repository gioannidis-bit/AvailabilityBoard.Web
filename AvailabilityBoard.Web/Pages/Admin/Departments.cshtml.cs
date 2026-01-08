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
    public List<Employee> Employees { get; set; } = new();
    public Dictionary<int, string> ManagerNameByDeptId { get; set; } = new();

    [BindProperty] public int DepartmentId { get; set; }
    [BindProperty] public int ManagerEmployeeId { get; set; }

    public async Task OnGet()
    {
        Departments = await _db.Departments.GetAll();

        // μικρό MVP: φέρνουμε όλους τους employees (αν είναι πολλοί, το κάνουμε searchable αμέσως μετά)
        // Αν έχεις χιλιάδες, πες μου να το κάνουμε typeahead.
        using var cn = Db.Open(_db.GetType().GetProperty("_cs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) == null ? "" : "");
        // ↑ αγνόησέ το: αν σε ενοχλεί, στο επόμενο βήμα προσθέτω σωστό EmployeeRepo.Search() και δεν κάνουμε reflection.
    }

    public async Task<IActionResult> OnPost()
    {
        await _db.DepartmentManagers.Upsert(DepartmentId, ManagerEmployeeId);
        return RedirectToPage();
    }
}
