using System.Security.Claims;

namespace AvailabilityBoard.Web;

public static class ClaimsPrincipalExtensions
{
    public static int GetEmployeeId(this ClaimsPrincipal user)
    {
        var claim = user.FindFirstValue("employee_id");
        if (int.TryParse(claim, out var id))
            return id;

        throw new UnauthorizedAccessException("Missing employee_id claim");
    }

    public static int? GetEmployeeIdOrNull(this ClaimsPrincipal user)
    {
        var claim = user.FindFirstValue("employee_id");
        if (int.TryParse(claim, out var id))
            return id;
        return null;
    }

    public static bool IsAdmin(this ClaimsPrincipal user)
    {
        return user.FindFirstValue("is_admin") == "1";
    }

    public static bool IsApprover(this ClaimsPrincipal user)
    {
        return user.FindFirstValue("is_approver") == "1" || 
               user.FindFirstValue("is_approver_group") == "1";
    }

    public static int? GetDepartmentId(this ClaimsPrincipal user)
    {
        var claim = user.FindFirstValue("department_id");
        if (int.TryParse(claim, out var id))
            return id;
        return null;
    }
}
