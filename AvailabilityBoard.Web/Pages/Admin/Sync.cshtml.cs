using AvailabilityBoard.Web.Data;
using AvailabilityBoard.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AvailabilityBoard.Web.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class SyncModel : PageModel
{
    private readonly Db _db;
    private readonly LdapService _ldap;
    private readonly EmployeeSyncService _sync;
    private readonly IConfiguration _cfg;

    public SyncModel(Db db, LdapService ldap, EmployeeSyncService sync, IConfiguration cfg)
    {
        _db = db;
        _ldap = ldap;
        _sync = sync;
        _cfg = cfg;
    }

    public dynamic? LastSync { get; set; }
    public List<dynamic> RecentSyncs { get; set; } = new();
    public string? Message { get; set; }
    public int SyncIntervalMinutes { get; set; }

    public async Task OnGet()
    {
        LastSync = await _db.SyncLogs.GetLastSync();
        RecentSyncs = await _db.SyncLogs.GetRecentSyncs(20);
        SyncIntervalMinutes = _cfg.GetValue("AdSync:IntervalMinutes", 2);
    }

    public async Task<IActionResult> OnPost(string Action)
    {
        if (Action == "sync")
        {
            var syncId = await _db.SyncLogs.StartSync();
            int added = 0, updated = 0, deactivated = 0;

            try
            {
                var adUsers = _ldap.FetchAllUsers().ToList();
                var adGuids = adUsers.Select(u => u.AdGuid).ToHashSet();

                var adminGroups = _cfg.GetSection("Roles:AdminGroups").Get<string[]>() ?? Array.Empty<string>();
                var approverGroups = _cfg.GetSection("Roles:ApproverGroups").Get<string[]>() ?? Array.Empty<string>();

                bool InGroup(List<string> memberOf, string[] groups) =>
                    groups.Any(g => memberOf.Any(dn =>
                        dn.Contains("CN=" + g + ",", StringComparison.OrdinalIgnoreCase)));

                foreach (var u in adUsers)
                {
                    var existing = await _db.Employees.GetByAdGuid(u.AdGuid);
                    var empId = await _sync.UpsertFromAd(u);

                    if (existing == null)
                        added++;
                    else
                        updated++;

                    var ovr = await _db.Overrides.Get(empId);
                    var isAdmin = ovr?.IsAdminOverride ?? InGroup(u.MemberOf, adminGroups);
                    var isApprover = ovr?.IsApproverOverride ?? InGroup(u.MemberOf, approverGroups);

                    await _db.Employees.SetRoleFlags(empId, isAdmin, isApprover);
                }

                var dbActiveGuids = await _db.Employees.GetAllActiveAdGuids();
                var toDeactivate = dbActiveGuids.Except(adGuids).ToList();

                foreach (var guid in toDeactivate)
                {
                    await _db.Employees.Deactivate(guid, "AD_SYNC_MANUAL");
                    deactivated++;
                }

                await _db.SyncLogs.CompleteSync(syncId, true, added, updated, deactivated, null);
                Message = $"Sync completed. Added: {added}, Updated: {updated}, Deactivated: {deactivated}";
            }
            catch (Exception ex)
            {
                await _db.SyncLogs.CompleteSync(syncId, false, added, updated, deactivated, ex.Message);
                Message = $"Sync failed: {ex.Message}";
            }
        }

        LastSync = await _db.SyncLogs.GetLastSync();
        RecentSyncs = await _db.SyncLogs.GetRecentSyncs(20);
        SyncIntervalMinutes = _cfg.GetValue("AdSync:IntervalMinutes", 2);

        return Page();
    }
}
