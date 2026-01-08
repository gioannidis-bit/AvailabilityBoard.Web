using AvailabilityBoard.Web.Data;
using AvailabilityBoard.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using System.Globalization;

var supported = new[] { new CultureInfo("el-GR"), new CultureInfo("en-US") };


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt =>
    {
        opt.LoginPath = "/Login";
        opt.AccessDeniedPath = "/Login";
        opt.Cookie.Name = "AvailabilityBoard.Auth";
        opt.SlidingExpiration = true;
        opt.ExpireTimeSpan = TimeSpan.FromHours(12);
    });

builder.Services.AddAuthorization(opt =>
{
    opt.AddPolicy("AdminOnly", p => p.RequireClaim("is_admin", "1"));
});

builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<LdapService>();
builder.Services.AddSingleton<EmployeeSyncService>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<EmailSender>();

var app = builder.Build();

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("el-GR"),
    SupportedCultures = supported,
    SupportedUICultures = supported
});

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Minimal JSON endpoints for calendar + lists
app.MapGet("/api/departments", async (Db db) => Results.Ok(await db.Departments.GetAll()))
   .RequireAuthorization();

app.MapGet("/api/types", async (Db db) => Results.Ok(await db.Types.GetAll()))
   .RequireAuthorization();

app.MapGet("/api/notifications/unread-count", async (Db db, HttpContext ctx) =>
{
    var empId = ctx.User.GetEmployeeId();
    var c = await db.Notifications.GetUnreadCount(empId);
    return Results.Ok(new { count = c });
}).RequireAuthorization();

app.MapGet("/api/events", async (Db db, HttpContext ctx,
    DateTime start, DateTime end,
    string? departmentId,
    string? typeCodes,
    bool myTeamOnly) =>
{
    var empId = ctx.User.GetEmployeeId();

    int? deptId = null;
    if (!string.IsNullOrWhiteSpace(departmentId) && int.TryParse(departmentId, out var parsed))
        deptId = parsed;

    var events = await db.Requests.GetApprovedEvents(
        start, end,
        deptId,
        typeCodes,
        myTeamOnly,
        empId
    );

    return Results.Ok(events);
}).RequireAuthorization();

app.MapPost("/admin/sync-ad", async (HttpContext ctx, LdapService ldap, EmployeeSyncService sync) =>
{
    // απλό auth: μόνο admins
    var isAdmin = (ctx.User.FindFirst("is_admin")?.Value ?? "0") == "1";
    if (!isAdmin) return Results.Forbid();

    int count = 0;
    foreach (var u in ldap.FetchAllUsers())
    {
        await sync.UpsertFromAd(u);
        count++;
    }

    return Results.Ok(new { synced = count });
}).RequireAuthorization();

app.MapRazorPages();

app.Run();

static class ClaimsExt
{
    public static int GetEmployeeId(this System.Security.Claims.ClaimsPrincipal user)
    {
        var v = user.Claims.FirstOrDefault(c => c.Type == "emp_id")?.Value;
        if (!int.TryParse(v, out var id)) throw new Exception("Missing emp_id claim");
        return id;
    }
}
