using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Domain;
using GestionRequerimientoDP.Web.Auth;

namespace GestionRequerimientoDP.Web.Endpoints;

/// <summary>Descargas .xlsx (§3.4). Los filtros de seguridad se aplican en los servicios.</summary>
public static class ExportEndpoints
{
    const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static Equipo? ParseUnidad(string? s) => s switch
    {
        "proc" => Equipo.Procesamiento, "script" => Equipo.Script, "cod" => Equipo.Codificacion, _ => null,
    };

    static DateOnly? ParseDate(string? s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", out var d) ? d : null;

    static IResult FileOr(ExportFile? f) => f is null ? Results.NoContent() : Results.File(f.Content, Xlsx, f.FileName);

    public static void MapExportEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/export").RequireAuthorization();

        g.MapGet("/planificacion", async (HttpContext http, IExportService export, IClock clock, string? unidad, string? semana) =>
        {
            var me = Claims.ToUsuario(http.User)!;
            var week = Fechas.MondayOf(ParseDate(semana) ?? clock.Today);
            return FileOr(await export.PlanificacionAsync(me, ParseUnidad(unidad) ?? Equipo.Procesamiento, week));
        }).RequireAuthorization(Policies.Lider);

        g.MapGet("/mi-semana", async (HttpContext http, IExportService export, IClock clock, string? semana) =>
        {
            var me = Claims.ToUsuario(http.User)!;
            return FileOr(await export.MiSemanaAsync(me, Fechas.MondayOf(ParseDate(semana) ?? clock.Today)));
        }).RequireAuthorization(Policies.Analista);

        g.MapGet("/historico", async (HttpContext http, IExportService export, IClock clock, string? modo, string? @ref, int? analista) =>
        {
            var me = Claims.ToUsuario(http.User)!;
            var p = Periodo.De(modo == "mes" ? PeriodoModo.Mes : PeriodoModo.Semana, ParseDate(@ref) ?? clock.Today);
            return FileOr(await export.HistoricoAsync(me, p, analista));
        }).RequireAuthorization(Policies.Lider);

        g.MapGet("/atrasos", async (HttpContext http, IExportService export, IClock clock, string? modo, string? @ref, int? analista, string? unidad) =>
        {
            var me = Claims.ToUsuario(http.User)!;
            var p = Periodo.De(modo == "mes" ? PeriodoModo.Mes : PeriodoModo.Semana, ParseDate(@ref) ?? clock.Today);
            return FileOr(await export.AtrasosAsync(me, p, analista, ParseUnidad(unidad)));
        }).RequireAuthorization(Policies.Lider);

        g.MapGet("/solicitudes", async (HttpContext http, IExportService export, string? unidad, string? estado) =>
        {
            var me = Claims.ToUsuario(http.User)!;
            return FileOr(await export.SolicitudesAsync(me, ParseUnidad(unidad), estado != "all"));
        }).RequireAuthorization(Policies.Lider);
    }
}
