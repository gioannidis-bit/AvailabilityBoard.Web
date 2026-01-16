using AvailabilityBoard.Web.Data;
using System.Diagnostics;

namespace AvailabilityBoard.Web.Services;

public sealed class AdSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<AdSyncBackgroundService> _log;

    public AdSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration cfg,
        ILogger<AdSyncBackgroundService> log)
    {
        _scopeFactory = scopeFactory;
        _cfg = cfg;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Αρχικό delay για να bootάρει το app
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        var intervalMinutes = _cfg.GetValue("AdSync:IntervalMinutes", 2);
        var interval = TimeSpan.FromMinutes(intervalMinutes);

        _log.LogInformation("AD Sync service started. Interval: {Interval} minutes", intervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSyncAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "AD Sync failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task RunSyncAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var ldap = scope.ServiceProvider.GetRequiredService<LdapService>();
        var db = scope.ServiceProvider.GetRequiredService<Db>();
        var syncService = scope.ServiceProvider.GetRequiredService<EmployeeSyncService>();
        var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var sw = Stopwatch.StartNew();
        var syncId = await db.SyncLogs.StartSync();

        int added = 0, updated = 0, deactivated = 0;

        try
        {
            // 1. Φέρε όλα τα AD GUIDs που είναι active στο AD
            var adUsers = ldap.FetchAllUsers().ToList();
            var adGuids = adUsers.Select(u => u.AdGuid).ToHashSet();

            _log.LogInformation("AD returned {Count} active users", adUsers.Count);

            // 2. Role mappings από config
            var adminGroups = cfg.GetSection("Roles:AdminGroups").Get<string[]>() ?? Array.Empty<string>();
            var approverGroups = cfg.GetSection("Roles:ApproverGroups").Get<string[]>() ?? Array.Empty<string>();

            bool InGroup(List<string> memberOf, string[] groups) =>
                groups.Any(g => memberOf.Any(dn => 
                    dn.Contains("CN=" + g + ",", StringComparison.OrdinalIgnoreCase)));

            // 3. Upsert κάθε AD user
            foreach (var u in adUsers)
            {
                ct.ThrowIfCancellationRequested();

                var existing = await db.Employees.GetByAdGuid(u.AdGuid);
                var empId = await syncService.UpsertFromAd(u);

                if (existing == null)
                    added++;
                else
                    updated++;

                // Set role flags (αν δεν υπάρχει override)
                var ovr = await db.Overrides.Get(empId);
                var isAdmin = ovr?.IsAdminOverride ?? InGroup(u.MemberOf, adminGroups);
                var isApprover = ovr?.IsApproverOverride ?? InGroup(u.MemberOf, approverGroups);

                await db.Employees.SetRoleFlags(empId, isAdmin, isApprover);
            }

            // 4. Soft-delete: όσοι είναι στη DB αλλά όχι στο AD
            var dbActiveGuids = await db.Employees.GetAllActiveAdGuids();
            var toDeactivate = dbActiveGuids.Except(adGuids).ToList();

            foreach (var guid in toDeactivate)
            {
                ct.ThrowIfCancellationRequested();
                await db.Employees.Deactivate(guid, "AD_SYNC");
                deactivated++;
            }

            // 5. Manager linking (second pass - μετά που έχουν μπει όλοι)
            foreach (var u in adUsers.Where(x => !string.IsNullOrEmpty(x.ManagerDn)))
            {
                var emp = await db.Employees.GetByAdGuid(u.AdGuid);
                if (emp == null) continue;

                // Check αν υπάρχει manual override για manager
                var ovr = await db.Overrides.Get(emp.EmployeeId);
                if (ovr?.ManagerEmployeeIdOverride != null) continue; // skip, has override

                // Βρες τον manager από DN
                var mgrGuid = adUsers.FirstOrDefault(m => 
                    string.Equals(m.SamAccountName, ExtractSamFromDn(u.ManagerDn), StringComparison.OrdinalIgnoreCase)
                    || adUsers.Any(x => x.AdGuid.ToString() == u.ManagerDn))?.AdGuid;

                if (mgrGuid.HasValue)
                {
                    var mgr = await db.Employees.GetByAdGuid(mgrGuid.Value);
                    if (mgr != null)
                        await db.Employees.SetManager(emp.EmployeeId, mgr.EmployeeId);
                }
            }

            sw.Stop();
            await db.SyncLogs.CompleteSync(syncId, true, added, updated, deactivated, null);

            _log.LogInformation(
                "AD Sync completed in {Ms}ms. Added: {Added}, Updated: {Updated}, Deactivated: {Deactivated}",
                sw.ElapsedMilliseconds, added, updated, deactivated);
        }
        catch (Exception ex)
        {
            await db.SyncLogs.CompleteSync(syncId, false, added, updated, deactivated, ex.Message);
            throw;
        }
    }

    private static string? ExtractSamFromDn(string? dn)
    {
        if (string.IsNullOrEmpty(dn)) return null;
        // CN=John Doe,OU=Users,DC=... -> δεν είναι SAM, χρειάζεται lookup
        // Απλοποίηση: επιστρέφουμε null, το manager linking γίνεται με GUID matching
        return null;
    }
}
