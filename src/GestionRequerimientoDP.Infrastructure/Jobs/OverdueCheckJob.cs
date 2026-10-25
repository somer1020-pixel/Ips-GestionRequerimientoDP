using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GestionRequerimientoDP.Infrastructure.Jobs;

/// <summary>Revisión diaria de atrasos sin gestión a las 09:00 de Chile (y una vez al iniciar). Equivale a checkOverdue(false).</summary>
public sealed class OverdueCheckJob(IServiceScopeFactory scopes, TimeProvider time, ILogger<OverdueCheckJob> log) : BackgroundService
{
    static readonly TimeOnly Hora = new(9, 0);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(DelayToNext(time.GetUtcNow()), time, stoppingToken);
            await RunAsync(stoppingToken);
        }
    }

    public static TimeSpan DelayToNext(DateTimeOffset utcNow)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, Fechas.Chile);
        var next = DateOnly.FromDateTime(local.DateTime).ToDateTime(Hora);
        if (next <= local.DateTime) next = next.AddDays(1);
        var nextUtc = new DateTimeOffset(next, Fechas.Chile.GetUtcOffset(next));
        var d = nextUtc - utcNow;
        return d < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : d;
    }

    async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var n = await scope.ServiceProvider.GetRequiredService<INotificationService>().CheckOverdueAsync(false, ct);
            if (n > 0) log.LogInformation("Avisos de atraso encolados: {N}", n);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Error en la revisión de atrasos"); }
    }
}
