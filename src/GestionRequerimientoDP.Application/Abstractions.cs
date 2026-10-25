using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application;

/// <summary>Unidad de trabajo sobre la base de datos. Implementada por el DbContext de Infrastructure.</summary>
public interface IAppDbContext : IDisposable, IAsyncDisposable
{
    DbSet<Usuario> Usuarios { get; }
    DbSet<Requerimiento> Requerimientos { get; }
    DbSet<Solicitud> Solicitudes { get; }
    DbSet<Notificacion> Notificaciones { get; }
    DbSet<OkLog> OkLogs { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Crea contextos de corta duración (recomendado en Blazor Server, donde un circuito vive mucho).</summary>
public interface IAppDbContextFactory
{
    IAppDbContext Create();
}

/// <summary>Hoy y ahora en hora de Chile. Se construye sobre TimeProvider para poder simularlo en los tests.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
    DateOnly Today { get; }
}

public sealed class ChileClock(TimeProvider time) : IClock
{
    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(time.GetUtcNow(), Fechas.Chile);
    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
}

/// <summary>Difusión de cambios entre circuitos (tiempo real). Todos los circuitos corren en el mismo proceso.</summary>
public interface IChangeNotifier
{
    event Action<ChangeKind>? Changed;
    void Publish(ChangeKind kind);
}

public enum ChangeKind { Requerimientos, Solicitudes, Notificaciones, Usuarios }

public sealed class ChangeNotifier : IChangeNotifier
{
    public event Action<ChangeKind>? Changed;
    public void Publish(ChangeKind kind) => Changed?.Invoke(kind);
}

/// <summary>Configuración de reglas (appsettings → "Reglas").</summary>
public sealed class ReglasOptions
{
    public const string Section = "Reglas";
    public int ProcCapacity { get; set; } = 10;
    public int CodCapacity { get; set; } = 10;
    public int ScriptWip { get; set; } = 3;
    public int StaleDays { get; set; } = 2;
    public bool NotifyAssign { get; set; } = true;
    public bool NotifyOverdue { get; set; } = true;
    public bool CompactCards { get; set; }

    public int CapacityOf(Equipo unidad) => unidad == Equipo.Codificacion ? CodCapacity : ProcCapacity;
}

/// <summary>Usuario autenticado, resuelto desde los claims.</summary>
public sealed record UsuarioActual(int Id, string Nombre, Rol Rol, IReadOnlyList<Equipo> Equipos)
{
    public bool EsLider => Rol == Rol.Lider;
    public bool EsAnalista => Rol == Rol.Analista;
    public bool EsComercial => Rol == Rol.Comercial;
    public static UsuarioActual From(Usuario u) => new(u.Id, u.Nombre, u.Rol, u.EquiposOrdenados.ToList());
}

/// <summary>Error de regla de negocio con mensaje para el usuario.</summary>
public sealed class ReglaException(string message) : Exception(message);

/// <summary>Acción no permitida para el rol o el usuario.</summary>
public sealed class PermisoException(string message = "No tienes permiso para esta acción.") : Exception(message);

public static class AppInfo
{
    public const string Nombre = "GestionRequerimientoDP";
    public const string Marca = "GR";
    public const string Subtitulo = "Scripting, Procesamiento de datos y Codificación";
}
