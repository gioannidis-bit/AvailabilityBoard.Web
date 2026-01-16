using AvailabilityBoard.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AvailabilityBoard.Web.Pages;

[Authorize]
public class IndexModel : PageModel
{
    private readonly Db _db;

    public IndexModel(Db db) => _db = db;

    public bool IsAdmin { get; set; }
    public bool IsApprover { get; set; }
    public bool CanManage { get; set; }  // Can see filters and manage schedules
    public int CurrentEmployeeId { get; set; }

    public async Task OnGetAsync()
    {
        var empId = User.GetEmployeeId();
        CurrentEmployeeId = empId;
        
        var emp = await _db.Employees.GetById(empId);
        if (emp != null)
        {
            IsAdmin = emp.IsAdmin;
            IsApprover = emp.IsApprover;
            
            // Check if user is a department manager
            var managedDepts = await _db.DepartmentManagers.GetManagedDepartmentIds(empId);
            var isDeptManager = managedDepts.Any();
            
            // Can manage = admin, approver, or department manager
            CanManage = IsAdmin || IsApprover || isDeptManager;
        }
    }
}
