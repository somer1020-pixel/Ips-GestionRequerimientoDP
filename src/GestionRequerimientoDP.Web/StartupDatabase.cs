using System.Net;
using GestionRequerimientoDP.Infrastructure;
using Microsoft.Data.SqlClient;

namespace GestionRequerimientoDP.Web;

/// <summary>Migraciones y datos semilla al iniciar. Ante un error devuelve una página HTML con la causa.</summary>
public static class StartupDatabase
{
    public static async Task<string?> InitializeAsync(WebApplication app)
    {
        try
        {
            await app.Services.InitializeDatabaseAsync(
                migrate: app.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()),
                seed: app.Configuration.GetValue("Database:Seed", app.Environment.IsDevelopment()),
                app.Logger);
            return null;
        }
        catch (Exception ex)
        {
            app.Logger.LogCritical(ex, "No se pudo inicializar la base de datos");
            return Page(app, ex);
        }
    }

    static string Page(WebApplication app, Exception ex)
    {
        var cs = new SqlConnectionStringBuilder(app.Configuration.GetConnectionString(DependencyInjection.ConnectionName) ?? "");
        var auth = cs.IntegratedSecurity ? "Windows (Trusted_Connection)" : $"usuario SQL «{cs.UserID}»";
        var detail = app.Environment.IsDevelopment()
            ? $"<pre>{WebUtility.HtmlEncode(Root(ex).Message)}</pre>"
            : "<p>Revisa el log del servidor para ver el detalle.</p>";
        return $$"""
            <!DOCTYPE html><html lang="es"><head><meta charset="utf-8"><title>GestionRequerimientoDP · Base de datos</title>
            <style>body{font-family:"IBM Plex Sans",system-ui,"Segoe UI",sans-serif;background:#f4f6fa;color:#151a3d;margin:0}
            header{background:#121b5a;color:#fff;padding:14px 24px;font-weight:600}main{max-width:820px;margin:32px auto;padding:0 16px}
            .card{background:#fff;border:1px solid #d6dbe8;border-radius:6px;padding:20px}h2{margin:0 0 12px;color:#b42318}
            pre{white-space:pre-wrap;background:#fdeceb;color:#b42318;padding:12px;border-radius:6px;font-size:13px}li{margin:6px 0}code{background:#eceff6;padding:1px 5px;border-radius:4px}</style></head>
            <body><header>GestionRequerimientoDP</header><main><div class="card">
            <h2>No se pudo conectar a la base de datos</h2>
            <p>Servidor: <code>{{WebUtility.HtmlEncode(cs.DataSource)}}</code> · Base: <code>{{WebUtility.HtmlEncode(cs.InitialCatalog)}}</code> · Autenticación: {{WebUtility.HtmlEncode(auth)}}</p>
            {{detail}}
            <p>Revisa:</p><ul>
            <li>Que el servicio de SQL Server esté iniciado y que el nombre del servidor sea el mismo que usas en SSMS (por ejemplo <code>localhost</code> o <code>localhost\SQLEXPRESS</code>).</li>
            <li>Si usas usuario y contraseña: que el usuario exista, la contraseña sea correcta y el servidor tenga habilitada la autenticación de SQL Server (modo mixto).</li>
            <li>Que la cadena incluya <code>TrustServerCertificate=True</code> si el servidor usa un certificado propio.</li>
            <li>La cadena se configura en <code>ConnectionStrings:GestionRequerimientoDP</code> (secretos de usuario o <code>appsettings.Development.json</code>). Después de corregirla, detén la app (Shift+F5) y vuelve a iniciarla.</li>
            </ul></div></main></body></html>
            """;
    }

    static Exception Root(Exception ex) => ex.InnerException is { } i && ex is not SqlException ? Root(i) : ex;
}
