using System.Net.Mail;

namespace AvailabilityBoard.Web.Services;

public sealed class EmailSender
{
    private readonly IConfiguration _cfg;
    public EmailSender(IConfiguration cfg) => _cfg = cfg;

    public async Task TrySend(string to, string subject, string body)
    {
        var s = _cfg.GetSection("Smtp");
        var enabled = bool.Parse(s["Enabled"] ?? "false");
        if (!enabled) return;

        var host = s["Host"] ?? throw new Exception("Smtp:Host missing");
        var port = int.Parse(s["Port"] ?? "25");
        var from = s["From"] ?? throw new Exception("Smtp:From missing");

        using var client = new SmtpClient(host, port);
        using var msg = new MailMessage(from, to, subject, body);
        await client.SendMailAsync(msg);
    }
}
