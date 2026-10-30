using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Infrastructure.Data;
using GestionRequerimientoDP.Infrastructure.Email;
using GestionRequerimientoDP.Infrastructure.Excel;
using GestionRequerimientoDP.Infrastructure.Jobs;
using GestionRequerimientoDP.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GestionRequerimientoDP.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionName = "GestionRequerimientoDP";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var cs = config.GetConnectionString(ConnectionName);
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                $"Falta la cadena de conexión 'ConnectionStrings:{ConnectionName}'. En el servidor: appsettings.Production.json junto a la DLL o la variable de entorno ConnectionStrings__{ConnectionName}. En desarrollo: 'dotnet user-secrets' (ver README).");

        services.AddDbContextFactory<AppDbContext>(o => o.UseSqlServer(cs, sql => sql.EnableRetryOnFailure(3)));
        services.AddSingleton<IAppDbContextFactory, AppDbContextFactory>();
        services.AddSingleton<IExcelGateway, ClosedXmlGateway>();
        services.AddSingleton<IPasswordService, PasswordService>();

        services.Configure<SmtpOptions>(config.GetSection(SmtpOptions.Section));
        if (!string.IsNullOrWhiteSpace(config[$"{SmtpOptions.Section}:Host"])) services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else services.AddSingleton<IEmailSender, LogEmailSender>();

        if (config.GetValue("Jobs:Enabled", true))
        {
            services.AddHostedService<EmailDispatcher>();
            services.AddHostedService<OverdueCheckJob>();
        }
        return services;
    }

    /// <summary>Aplica migraciones y, si corresponde, carga los datos semilla.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider sp, bool migrate, bool seed, ILogger log, CancellationToken ct = default)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        if (migrate) await db.Database.MigrateAsync(ct);
        if (seed && await DbSeeder.SeedAsync(db, sp.GetRequiredService<IClock>(), sp.GetRequiredService<IPasswordService>(), ct))
            log.LogInformation("Datos semilla cargados.");
    }
}

/// <summary>Para "dotnet ef" sin levantar la app. Usa la variable GRDP_CONNECTION o una instancia local.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("GRDP_CONNECTION")
                 ?? "Server=localhost;Database=GestionRequerimientoDP;Trusted_Connection=True;TrustServerCertificate=True";
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options);
    }
}
