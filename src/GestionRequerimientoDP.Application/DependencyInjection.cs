using GestionRequerimientoDP.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionRequerimientoDP.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, ChileClock>();
        services.AddSingleton<IChangeNotifier, ChangeNotifier>();

        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IRequerimientoService, RequerimientoService>();
        services.AddScoped<IOkService, OkService>();
        services.AddScoped<IScriptAssignmentService, ScriptAssignmentService>();
        services.AddScoped<PlanificacionService>();
        services.AddScoped<IPlanificacionService>(sp => sp.GetRequiredService<PlanificacionService>());
        services.AddScoped<IBalanceService>(sp => sp.GetRequiredService<PlanificacionService>());
        services.AddScoped<ImportExportService>();
        services.AddScoped<IImportService>(sp => sp.GetRequiredService<ImportExportService>());
        services.AddScoped<IExportService>(sp => sp.GetRequiredService<ImportExportService>());
        services.AddScoped<ISolicitudService, SolicitudService>();
        services.AddScoped<KpiService>();
        services.AddScoped<IKpiService>(sp => sp.GetRequiredService<KpiService>());
        services.AddScoped<ISummaryService>(sp => sp.GetRequiredService<KpiService>());
        services.AddScoped<IUsuarioService, UsuarioService>();
        services.AddScoped<IConsultaService, ConsultaService>();
        services.AddScoped<IAccesoService, AccesoService>();
        services.AddScoped<IAuditoriaService, AuditoriaService>();
        return services;
    }
}
