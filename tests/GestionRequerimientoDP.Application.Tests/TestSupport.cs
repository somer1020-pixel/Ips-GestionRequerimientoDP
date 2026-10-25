using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Domain;
using GestionRequerimientoDP.Infrastructure.Data;
using GestionRequerimientoDP.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GestionRequerimientoDP.Application.Tests;

/// <summary>TimeProvider fijo en hora de Chile.</summary>
public sealed class FixedTime(DateOnly day, int hour = 10) : TimeProvider
{
    public DateTimeOffset Value { get; set; } = Fechas.FromLocal(day, hour, 0).ToUniversalTime();
    public override DateTimeOffset GetUtcNow() => Value;
}

public sealed class TestFactory(string name) : IAppDbContextFactory
{
    readonly DbContextOptions<AppDbContext> _o = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options;
    public IAppDbContext Create() => new AppDbContext(_o);
    public AppDbContext CreateConcrete() => new(_o);
}

/// <summary>Servicios reales sobre una BD en memoria, con "hoy" configurable.</summary>
public sealed class TestApp
{
    public TestFactory Factory { get; } = new("t" + Guid.NewGuid().ToString("N"));
    public FixedTime Time { get; }
    public IClock Clock { get; }
    public ReglasOptions Reglas { get; } = new();
    public ChangeNotifier Changes { get; } = new();
    public NotificationService Notif { get; }
    public RequerimientoService Reqs { get; }
    public OkService Ok { get; }
    public SolicitudService Sols { get; }
    public ScriptAssignmentService Scripts { get; }
    public PlanificacionService Plan { get; }

    public TestApp(DateOnly today)
    {
        Time = new FixedTime(today);
        Clock = new ChileClock(Time);
        var opts = new StaticOptions(Reglas);
        Notif = new NotificationService(Factory, Clock, opts, Changes);
        Reqs = new RequerimientoService(Factory, Clock, Changes, Notif);
        Ok = new OkService(Factory, Clock, Changes);
        Sols = new SolicitudService(Factory, Clock, Changes);
        Scripts = new ScriptAssignmentService(Factory, Clock, Changes, Notif);
        Plan = new PlanificacionService(Factory, Clock, opts, Notif, Changes);
    }

    public async Task SeedAsync()
    {
        await using var db = Factory.Create();
        await DbSeeder.SeedAsync(db, Clock, new PasswordService());
    }

    public async Task<UsuarioActual> UserAsync(string email)
    {
        await using var db = Factory.Create();
        var u = await db.Usuarios.Include(x => x.Equipos).FirstAsync(x => x.Email == email);
        return UsuarioActual.From(u);
    }

    sealed class StaticOptions(ReglasOptions v) : IOptionsMonitor<ReglasOptions>
    {
        public ReglasOptions CurrentValue => v;
        public ReglasOptions Get(string? name) => v;
        public IDisposable? OnChange(Action<ReglasOptions, string?> listener) => null;
    }
}

public static class Make
{
    static int _id = 1000;
    public static Usuario Analista(int id, params Equipo[] eq) => new()
    {
        Id = id, Nombre = "A" + id, Email = $"a{id}@x.cl", Rol = Rol.Analista, Activo = true,
        Equipos = eq.Select(e => new UsuarioEquipo { UsuarioId = id, Equipo = e }).ToList(),
    };

    public static Requerimiento Req(Equipo tipo, DateOnly fecha, int? analista, string job = "J1", Estado estado = Estado.Pendiente) => new()
    {
        Id = ++_id, Tipo = tipo, Fecha = fecha, AnalistaId = analista, Job = job, Estado = estado,
        Creado = Fechas.FromLocal(fecha.AddDays(-3), 9, 0), Actualizado = Fechas.FromLocal(fecha.AddDays(-3), 9, 0),
    };
}
