using AvailabilityBoard.Web.Data;
using AvailabilityBoard.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ===== SERVICES =====

// Data
builder.Services.AddSingleton<Db>();

// Services
builder.Services.AddSingleton<LdapService>();
builder.Services.AddScoped<EmployeeSyncService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddSingleton<EmailSender>();

// Background AD Sync
if (builder.Configuration.GetValue("AdSync:Enabled", true))
{
    builder.Services.AddHostedService<AdSyncBackgroundService>();
}

// Auth
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireClaim("is_admin", "1"));
    options.AddPolicy("ApproverOnly", policy => policy.RequireAssertion(ctx =>
        ctx.User.HasClaim("is_approver", "1") || ctx.User.HasClaim("is_approver_group", "1")));
});

// MVC
builder.Services.AddRazorPages();
builder.Services.AddControllers();

var app = builder.Build();

// ===== MIDDLEWARE =====

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

app.Run();
