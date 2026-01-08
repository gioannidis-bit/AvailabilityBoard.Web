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
        var bindUser = _cfg["Ldap:BindUser"] ?? throw new Exception("Ldap:BindUser missing");
        var bindPass = _cfg["Ldap:BindPassword"] ?? throw new Exception("Ldap:BindPassword missing");
        var filter = _cfg["Ldap:AllUsersFilter"] ?? "(&(objectCategory=person)(objectClass=user))";

        var identifier = new LdapDirectoryIdentifier(server, port, fullyQualifiedDnsHostName: false, connectionless: false);
        using var conn = new LdapConnection(identifier) { AuthType = AuthType.Negotiate };

        conn.SessionOptions.ProtocolVersion = 3;
        conn.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

        if (useLdaps)
        {
            conn.SessionOptions.SecureSocketLayer = true;
            conn.SessionOptions.VerifyServerCertificate += (_, __) => true; // production: validate cert
        }

        conn.Credential = new NetworkCredential(bindUser, bindPass);
        conn.Bind();

        var attrs = _cfg.GetSection("Ldap:Attributes").Get<string[]>() ??
                    new[] { "objectGUID", "displayName", "mail", "department", "manager", "memberOf", "sAMAccountName" };

        // Paged search (για domains με πολλούς χρήστες)
        var pageSize = 500;
        var cookie = Array.Empty<byte>();

        while (true)
        {
            var req = new SearchRequest(baseDn, filter, SearchScope.Subtree, attrs);
            req.Controls.Add(new PageResultRequestControl(pageSize) { Cookie = cookie });

            var resp = (SearchResponse)conn.SendRequest(req);

            foreach (SearchResultEntry entry in resp.Entries)
                yield return MapUser(entry);

            var pageResp = resp.Controls.OfType<PageResultResponseControl>().FirstOrDefault();
            if (pageResp == null || pageResp.Cookie == null || pageResp.Cookie.Length == 0)
                break;

            cookie = pageResp.Cookie;
        }
    }

    private static string EscapeLdapFilterValue(string value)
        => value.Replace("\\", "\\5c").Replace("*", "\\2a").Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");
}
