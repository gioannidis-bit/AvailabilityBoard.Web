using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.Claims;

namespace AvailabilityBoard.Web.Services;

public sealed class LdapUser
{
    public required Guid AdGuid { get; init; }
    public required string SamAccountName { get; init; }
    public required string DisplayName { get; init; }
    public string? Email { get; init; }
    public string? Department { get; init; }
    public string? ManagerDn { get; init; }
    public List<string> MemberOf { get; init; } = new();
}

public sealed class LdapService
{
    private readonly IConfiguration _cfg;

    public LdapService(IConfiguration cfg) => _cfg = cfg;

    private (string server, int port, bool useLdaps, string baseDn, string filter) GetCfg()
    {
        var section = _cfg.GetSection("Ldap");
        return (
            section["Server"] ?? throw new Exception("Ldap:Server missing"),
            int.Parse(section["Port"] ?? "389"),
            bool.Parse(section["UseLdaps"] ?? "false"),
            section["BaseDn"] ?? throw new Exception("Ldap:BaseDn missing"),
            section["UserSearchFilter"] ?? "(&(objectClass=user)(sAMAccountName={0}))"
        );
    }

    private string[] GetUserSearchBasesOrDefault(string fallbackBaseDn)
    {
        var bases = _cfg.GetSection("Ldap:UserSearchBases").Get<string[]>()
                    ?? Array.Empty<string>();

        bases = bases
            .Select(s => (s ?? "").Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        return bases.Length > 0 ? bases : new[] { fallbackBaseDn };
    }

    private string[] GetGroupSearchBasesOrDefault(string fallbackBaseDn)
    {
        var bases = _cfg.GetSection("Ldap:GroupSearchBases").Get<string[]>()
                    ?? Array.Empty<string>();

        bases = bases
            .Select(s => (s ?? "").Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        return bases.Length > 0 ? bases : new[] { fallbackBaseDn };
    }

    public LdapUser AuthenticateAndFetchUser(string username, string password)
    {
        var (server, port, useLdaps, baseDn, _) = GetCfg();

        // 1) Normalize: τι θα χρησιμοποιήσουμε για search στο AD
        // - HITSa\gioannidis => gioannidis
        // - gioannidis@hit.com.gr => gioannidis (και κρατάμε και το UPN)
        var raw = username.Trim();
        var samForSearch = raw;

        if (raw.Contains('\\'))
            samForSearch = raw.Split('\\', 2)[1];
        else if (raw.Contains('@'))
            samForSearch = raw.Split('@', 2)[0];

        // 2) Credential για bind
        NetworkCredential cred;
        if (raw.Contains('\\'))
        {
            var parts = raw.Split('\\', 2);
            cred = new NetworkCredential(parts[1], password, parts[0]); // user, pass, domain
        }
        else
        {
            // UPN (user@domain) ή απλό user
            cred = new NetworkCredential(raw, password);
        }

        var identifier = new LdapDirectoryIdentifier(server, port, fullyQualifiedDnsHostName: false, connectionless: false);
        using var conn = new LdapConnection(identifier)
        {
            AuthType = AuthType.Negotiate
        };

        conn.SessionOptions.ProtocolVersion = 3;
        conn.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

        if (useLdaps)
        {
            conn.SessionOptions.SecureSocketLayer = true;
            conn.SessionOptions.VerifyServerCertificate += (_, __) => true; // MVP
        }

        conn.Credential = cred;
        conn.Bind();

        // 3) Search filter που πιάνει ΚΑΙ SAM ΚΑΙ UPN/mail
        var samEsc = EscapeLdapFilterValue(samForSearch);
        var upnEsc = raw.Contains('@') ? EscapeLdapFilterValue(raw) : null;

        var filter = upnEsc == null
            ? $"(&(objectClass=user)(objectCategory=person)(sAMAccountName={samEsc}))"
            : $"(&(objectClass=user)(objectCategory=person)(|(sAMAccountName={samEsc})(userPrincipalName={upnEsc})(mail={upnEsc})))";

        var attrs = _cfg.GetSection("Ldap:Attributes").Get<string[]>() ??
                    new[] { "objectGUID", "displayName", "mail", "department", "manager", "memberOf", "sAMAccountName" };

        var req = new SearchRequest(baseDn, filter, SearchScope.Subtree, attrs);
        var resp = (SearchResponse)conn.SendRequest(req);

        var entry = resp.Entries.Cast<SearchResultEntry>().FirstOrDefault()
                    ?? throw new Exception("User not found in AD (filter matched nothing).");

        return MapUser(entry);
    }

    public LdapUser? FetchUserByDn(string bindUsername, string bindPassword, string userDn)
    {
        var (server, port, useLdaps, _, _) = GetCfg();

        var identifier = new LdapDirectoryIdentifier(server, port, fullyQualifiedDnsHostName: false, connectionless: false);
        using var conn = new LdapConnection(identifier) { AuthType = AuthType.Negotiate };

        conn.SessionOptions.ProtocolVersion = 3;
        conn.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

        if (useLdaps)
        {
            conn.SessionOptions.SecureSocketLayer = true;
            conn.SessionOptions.VerifyServerCertificate += (_, __) => true;
        }

        conn.Credential = new NetworkCredential(bindUsername, bindPassword);
        conn.Bind();

        var attrs = new[] { "objectGUID", "displayName", "mail", "department", "manager", "memberOf", "sAMAccountName" };
        var req = new SearchRequest(userDn, "(objectClass=*)", SearchScope.Base, attrs);

        var resp = (SearchResponse)conn.SendRequest(req);
        var entry = resp.Entries.Cast<SearchResultEntry>().FirstOrDefault();
        if (entry == null) return null;

        return MapUser(entry);
    }

    private static LdapUser MapUser(SearchResultEntry entry)
    {
        var guidBytes = (byte[])entry.Attributes["objectGUID"][0];
        var adGuid = new Guid(guidBytes);

        string? GetStr(string attr)
            => entry.Attributes.Contains(attr) ? entry.Attributes[attr][0]?.ToString() : null;

        var memberOf = new List<string>();
        if (entry.Attributes.Contains("memberOf"))
            memberOf.AddRange(entry.Attributes["memberOf"].GetValues(typeof(string)).Cast<string>());

        return new LdapUser
        {
            AdGuid = adGuid,
            SamAccountName = GetStr("sAMAccountName") ?? "",
            DisplayName = GetStr("displayName") ?? entry.DistinguishedName,
            Email = GetStr("mail"),
            Department = GetStr("department"),
            ManagerDn = GetStr("manager"),
            MemberOf = memberOf
        };
    }

    public IEnumerable<LdapUser> FetchAllUsers()
    {
        var (server, port, useLdaps, baseDn, _) = GetCfg();

        var bindUserRaw = _cfg["Ldap:BindUser"] ?? throw new Exception("Ldap:BindUser missing");
        var bindPass = _cfg["Ldap:BindPassword"] ?? throw new Exception("Ldap:BindPassword missing");
        var filter = _cfg["Ldap:AllUsersFilter"] ?? "(&(objectCategory=person)(objectClass=user))";

        // Support bind formats:
        // - DOMAIN\\user
        // - user@domain
        // - (DN) CN=...,OU=...,DC=...
        NetworkCredential bindCred;
        if (bindUserRaw.Contains('\\'))
        {
            var parts = bindUserRaw.Split('\\', 2);
            bindCred = new NetworkCredential(parts[1], bindPass, parts[0]);
        }
        else
        {
            bindCred = new NetworkCredential(bindUserRaw, bindPass);
        }

        var identifier = new LdapDirectoryIdentifier(server, port, fullyQualifiedDnsHostName: false, connectionless: false);
        using var conn = new LdapConnection(identifier) { AuthType = AuthType.Negotiate };

        conn.SessionOptions.ProtocolVersion = 3;
        conn.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

        if (useLdaps)
        {
            conn.SessionOptions.SecureSocketLayer = true;
            conn.SessionOptions.VerifyServerCertificate += (_, __) => true; // production: validate cert
        }

        try
        {
            conn.Credential = bindCred;
            conn.Bind();
        }
        catch (LdapException ex) when (ex.ErrorCode == 49)
        {
            throw new Exception("AD Sync failed: invalid BindUser/BindPassword (LDAP error 49). Check Ldap:BindUser and Ldap:BindPassword.", ex);
        }
        catch (LdapException ex)
        {
            throw new Exception($"AD Sync failed: LDAP error {ex.ErrorCode}.", ex);
        }

        var attrs = _cfg.GetSection("Ldap:Attributes").Get<string[]>() ??
                    new[] { "objectGUID", "displayName", "mail", "department", "manager", "memberOf", "sAMAccountName" };

        // Αν ορίσεις Ldap:UserSearchBases, κάνουμε search μόνο σε αυτά τα OUs.
        // Διαφορετικά, πέφτουμε στο Ldap:BaseDn.
        var searchBases = GetUserSearchBasesOrDefault(baseDn);

        // De-dupe (σε περίπτωση overlap/λάθους config)
        var seen = new HashSet<Guid>();

        // Paged search (για domains με πολλούς χρήστες)
        var pageSize = int.TryParse(_cfg["Ldap:PageSize"], out var ps) ? Math.Clamp(ps, 50, 2000) : 500;

        foreach (var sb in searchBases)
        {
            var cookie = Array.Empty<byte>();
            while (true)
            {
                var req = new SearchRequest(sb, filter, SearchScope.Subtree, attrs);
                req.Controls.Add(new PageResultRequestControl(pageSize) { Cookie = cookie });

                var resp = (SearchResponse)conn.SendRequest(req);

                foreach (SearchResultEntry entry in resp.Entries)
                {
                    var u = MapUser(entry);
                    if (seen.Add(u.AdGuid))
                        yield return u;
                }

                var pageResp = resp.Controls.OfType<PageResultResponseControl>().FirstOrDefault();
                if (pageResp?.Cookie == null || pageResp.Cookie.Length == 0)
                    break;

                cookie = pageResp.Cookie;
            }
        }
    }

    // Προαιρετικό: αν θέλεις να κάνεις UI για επιλογή groups (π.χ. για AdminGroups/ApproverGroups)
    // χωρίς να ψάχνεις όλο το domain, εδώ περιορίζεις από Ldap:GroupSearchBases.
    public IEnumerable<(string Name, string? SamAccountName, string DistinguishedName)> FetchAllGroups()
    {
        var (server, port, useLdaps, baseDn, _) = GetCfg();

        var bindUserRaw = _cfg["Ldap:BindUser"] ?? throw new Exception("Ldap:BindUser missing");
        var bindPass = _cfg["Ldap:BindPassword"] ?? throw new Exception("Ldap:BindPassword missing");

        NetworkCredential bindCred;
        if (bindUserRaw.Contains('\\'))
        {
            var parts = bindUserRaw.Split('\\', 2);
            bindCred = new NetworkCredential(parts[1], bindPass, parts[0]);
        }
        else
        {
            bindCred = new NetworkCredential(bindUserRaw, bindPass);
        }

        var identifier = new LdapDirectoryIdentifier(server, port, fullyQualifiedDnsHostName: false, connectionless: false);
        using var conn = new LdapConnection(identifier) { AuthType = AuthType.Negotiate };

        conn.SessionOptions.ProtocolVersion = 3;
        conn.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

        if (useLdaps)
        {
            conn.SessionOptions.SecureSocketLayer = true;
            conn.SessionOptions.VerifyServerCertificate += (_, __) => true;
        }

        try
        {
            conn.Credential = bindCred;
            conn.Bind();
        }
        catch (LdapException ex) when (ex.ErrorCode == 49)
        {
            throw new Exception("AD Group fetch failed: invalid BindUser/BindPassword (LDAP error 49).", ex);
        }

        var bases = GetGroupSearchBasesOrDefault(baseDn);
        var attrs = new[] { "cn", "sAMAccountName" };
        var filter = "(objectClass=group)";

        foreach (var sb in bases)
        {
            var req = new SearchRequest(sb, filter, SearchScope.Subtree, attrs);
            var resp = (SearchResponse)conn.SendRequest(req);
            foreach (SearchResultEntry entry in resp.Entries)
            {
                var name = entry.Attributes.Contains("cn") ? entry.Attributes["cn"][0]?.ToString() : entry.DistinguishedName;
                var sam = entry.Attributes.Contains("sAMAccountName") ? entry.Attributes["sAMAccountName"][0]?.ToString() : null;
                yield return (name ?? entry.DistinguishedName, sam, entry.DistinguishedName);
            }
        }
    }

    private static string EscapeLdapFilterValue(string value)
        => value.Replace("\\", "\\5c").Replace("*", "\\2a").Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");
}
