using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Domain;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace GestionRequerimientoDP.Infrastructure.Email;

public interface IEmailSender
{
    /// <summary>true = envío real; false = simulado (sin SMTP configurado).</summary>
    bool IsReal { get; }
    /// <summary>Resumen de la configuración para mostrar al líder (servidor, puerto, cifrado, remitente).</summary>
    string Descripcion { get; }
    Task SendAsync(IReadOnlyList<string> to, IReadOnlyList<string> cc, string subject, string body, CancellationToken ct);
}

public sealed class SmtpOptions
{
    public const string Section = "Smtp";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    /// <summary>
    /// Cifrado de la conexión. Vacío = según UseStartTls. "Ninguna" (relay interno sin TLS), "StartTls",
    /// "StartTlsSiDisponible" o "Ssl" (TLS desde el inicio, p. ej. puerto 465).
    /// </summary>
    public string Seguridad { get; set; } = "";

    public string SeguridadLabel => Socket switch
    {
        SecureSocketOptions.None => "sin cifrado",
        SecureSocketOptions.StartTls => "STARTTLS obligatorio",
        SecureSocketOptions.SslOnConnect => "TLS desde el inicio",
        _ => "STARTTLS si el servidor lo ofrece",
    };

    public SecureSocketOptions Socket => Seguridad.Trim().ToLowerInvariant() switch
    {
        "ninguna" or "none" => SecureSocketOptions.None,
        "starttls" => SecureSocketOptions.StartTls,
        "starttlssidisponible" or "auto" => SecureSocketOptions.StartTlsWhenAvailable,
        "ssl" or "tls" => SecureSocketOptions.SslOnConnect,
        _ => UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
    };
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "gestionrequerimientodp@empresa.cl";
    public string FromName { get; set; } = AppInfo.Nombre;
}

/// <summary>Envío SMTP con MailKit (Exchange / Office 365 / relay corporativo).</summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public bool IsReal => true;
    public string Descripcion
    {
        get
        {
            var o = options.Value;
            return $"{o.Host}:{o.Port} · {o.SeguridadLabel}{(string.IsNullOrEmpty(o.User) ? " · sin autenticación" : $" · usuario {o.User}")} · remitente {o.From}";
        }
    }

    public async Task SendAsync(IReadOnlyList<string> to, IReadOnlyList<string> cc, string subject, string body, CancellationToken ct)
    {
        var o = options.Value;
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(o.FromName, o.From));
        foreach (var t in to) msg.To.Add(MailboxAddress.Parse(t));
        foreach (var c in cc) msg.Cc.Add(MailboxAddress.Parse(c));
        msg.Subject = subject;
        msg.Body = new TextPart("plain") { Text = body };
        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(o.Host, o.Port, o.Socket, ct);
        if (!string.IsNullOrEmpty(o.User)) await smtp.AuthenticateAsync(o.User, o.Password, ct);
        await smtp.SendAsync(msg, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}

/// <summary>Sin SMTP configurado: registra el correo en el log y lo da por enviado (simulado).</summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> log) : IEmailSender
{
    public bool IsReal => false;
    public string Descripcion => "Simulado: no hay servidor SMTP configurado (Smtp:Host vacío). Los correos no salen.";

    public Task SendAsync(IReadOnlyList<string> to, IReadOnlyList<string> cc, string subject, string body, CancellationToken ct)
    {
        log.LogInformation("Correo simulado → {To} (cc {Cc}): {Subject}", string.Join(", ", to), string.Join(", ", cc), subject);
        return Task.CompletedTask;
    }
}

/// <summary>Despacha la cola de la tabla Notificaciones y reintenta los errores.</summary>
public sealed class EmailDispatcher(IServiceScopeFactory scopes, IEmailSender sender, IClock clock, IChangeNotifier changes, TimeProvider time, ILogger<EmailDispatcher> log)
    : BackgroundService
{
    const int MaxIntentos = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), time);
        do
        {
            try { await DispatchOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Error al despachar correos"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> DispatchOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<IAppDbContextFactory>().Create();
        var pending = await db.Notificaciones
            .Where(n => n.Status == NotificacionStatus.Pendiente || (n.Status == NotificacionStatus.Error && n.Error.Length < 2000 && !n.Error.StartsWith("[5]")))
            .OrderBy(n => n.CreatedAt).Take(50).ToListAsync(ct);
        if (pending.Count == 0) return 0;
        var activos = (await db.Usuarios.AsNoTracking().Where(u => u.Activo).Select(u => u.Email).ToListAsync(ct))
            .Select(e => e.ToLowerInvariant()).ToHashSet();
        var conocidos = (await db.Usuarios.AsNoTracking().Select(u => u.Email).ToListAsync(ct)).Select(e => e.ToLowerInvariant()).ToHashSet();
        // No se envía a usuarios inactivos (los correos externos se mantienen).
        bool Ok(string e) => !conocidos.Contains(e.ToLowerInvariant()) || activos.Contains(e.ToLowerInvariant());

        foreach (var n in pending)
        {
            var to = n.ToList.Where(Ok).ToList();
            var cc = n.CcList.Where(Ok).ToList();
            if (to.Count == 0) { n.Status = NotificacionStatus.Omitido; n.Error = "Destinatarios inactivos"; continue; }
            try
            {
                await sender.SendAsync(to, cc, n.Subject, n.Body, ct);
                n.Status = NotificacionStatus.Enviado;
                n.SentAt = clock.Now;
                n.Error = sender.IsReal ? "" : "Simulado (sin SMTP configurado)";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var intento = n.Error.StartsWith('[') && int.TryParse(n.Error[1..n.Error.IndexOf(']')], out var k) ? k + 1 : 1;
                n.Status = NotificacionStatus.Error;
                n.Error = $"[{Math.Min(intento, MaxIntentos)}] {ex.Message}";
                log.LogWarning(ex, "No se pudo enviar el correo {Id}", n.Id);
            }
        }
        await db.SaveChangesAsync(ct);
        changes.Publish(ChangeKind.Notificaciones);
        return pending.Count;
    }
}
