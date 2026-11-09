using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Requerimiento> Requerimientos => Set<Requerimiento>();
    public DbSet<Solicitud> Solicitudes => Set<Solicitud>();
    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();
    public DbSet<OkLog> OkLogs => Set<OkLog>();
    public DbSet<AuditoriaAcceso> Auditoria => Set<AuditoriaAcceso>();
    public DbSet<PasswordResetToken> ResetTokens => Set<PasswordResetToken>();
    public DbSet<Comentario> Comentarios => Set<Comentario>();

    public async Task<List<string>> JobsQueContienenAsync(string normalizado, int max, CancellationToken ct = default)
    {
        if (Database.IsSqlServer())
        {
            // Lo habitual es buscar por número de Job: se resuelve con el índice de Job sin leer proyecto y cliente.
            if (normalizado.Any(char.IsDigit))
            {
                // Primero "empieza con" (búsqueda directa en el índice); si no hay, "contiene".
                var porJob = await Requerimientos.AsNoTracking().Where(r => r.Job.StartsWith(normalizado)).Select(r => r.Job).Distinct().Take(max).ToListAsync(ct);
                if (porJob.Count == 0)
                    porJob = await Requerimientos.AsNoTracking().Where(r => r.Job.Contains(normalizado)).Select(r => r.Job).Distinct().Take(max).ToListAsync(ct);
                if (porJob.Count > 0) return porJob;
            }
            return await Requerimientos.AsNoTracking()
                .Where(r => EF.Functions.Collate(r.Job + " " + r.Proyecto + " " + r.Cliente, "Latin1_General_CI_AI").Contains(normalizado))
                .Select(r => r.Job).Distinct().Take(max).ToListAsync(ct);
        }
        // Otros proveedores (pruebas en memoria): se normaliza en la aplicación.
        var keys = await Requerimientos.AsNoTracking().Select(r => new { r.Job, r.Proyecto, r.Cliente }).Distinct().ToListAsync(ct);
        return keys.Where(k => Reglas.Norm($"{k.Job} {k.Proyecto} {k.Cliente}").Contains(normalizado)).Select(k => k.Job).Distinct().Take(max).ToList();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<Enum>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Usuario>(e =>
        {
            e.ToTable("Usuarios");
            e.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Property(x => x.DomainAccount).HasMaxLength(200);
            e.HasIndex(x => x.DomainAccount).IsUnique().HasFilter("[DomainAccount] IS NOT NULL");
            e.Property(x => x.PasswordHash).HasMaxLength(400);
            e.Property(x => x.Rut).HasMaxLength(12);
            e.HasIndex(x => x.Rut).IsUnique().HasFilter("[Rut] IS NOT NULL");
            e.Property(x => x.SolicitudMotivo).HasMaxLength(500);
            e.Property(x => x.Aprobacion).HasDefaultValue(Aprobacion.Aprobado).HasSentinel(Aprobacion.Aprobado);
            e.Property(x => x.Origen).HasDefaultValue(OrigenUsuario.Admin).HasSentinel(OrigenUsuario.Admin);
            e.Ignore(x => x.Pendiente);
            e.HasMany(x => x.Equipos).WithOne().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.EquiposOrdenados);
        });
        b.Entity<UsuarioEquipo>(e =>
        {
            e.ToTable("UsuarioEquipos");
            e.HasKey(x => new { x.UsuarioId, x.Equipo });
        });

        b.Entity<Solicitud>(e =>
        {
            e.ToTable("Solicitudes");
            e.Property(x => x.Codigo).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Codigo).IsUnique();
            e.Property(x => x.Nota).HasMaxLength(2000);
            e.HasOne(x => x.Solicitante).WithMany().HasForeignKey(x => x.SolicitanteId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.SolicitanteId, x.CreadaAt });
            e.HasIndex(x => x.CreadaAt);
        });

        b.Entity<Requerimiento>(e =>
        {
            e.ToTable("Requerimientos");
            e.Property(x => x.Pais).HasMaxLength(5);
            e.Property(x => x.SL).HasMaxLength(80);
            e.Property(x => x.Tecnica).HasMaxLength(80);
            e.Property(x => x.Cliente).HasMaxLength(200);
            e.Property(x => x.Proyecto).HasMaxLength(300);
            e.Property(x => x.Job).HasMaxLength(60);
            e.Property(x => x.Entregable).HasMaxLength(120);
            e.Property(x => x.Descripcion).HasMaxLength(1000);
            e.Property(x => x.Comercial).HasMaxLength(200);
            e.Property(x => x.Nota).HasMaxLength(4000);
            e.Property(x => x.OkNota).HasMaxLength(1000);
            e.Property(x => x.Horas).HasPrecision(6, 2);
            e.Property(x => x.AtrasoMotivo).HasMaxLength(120);
            e.Property(x => x.AtrasoCampo).HasMaxLength(20);
            e.Property(x => x.AtrasoDetalle).HasMaxLength(1000);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne(x => x.Analista).WithMany().HasForeignKey(x => x.AnalistaId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.OkById).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Solicitud).WithMany(s => s.Requerimientos).HasForeignKey(x => x.SolicitudId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.Tipo, x.Fecha });
            // Índices para años de historia: lo de cada analista por fecha, lo abierto (tablero, atrasos, ranking, menú) y el
            // resumen de OK de scripts por analista, sin recorrer los requerimientos ya terminados.
            e.HasIndex(x => new { x.AnalistaId, x.Fecha }).IncludeProperties(x => new { x.Estado, x.OkAt });
            e.HasIndex(x => new { x.Tipo, x.Fecha }, "IX_Requerimientos_Abiertos").HasFilter("[Estado] <> N'Ok'").IncludeProperties(x => new { x.AnalistaId, x.Estado, x.SolicitudId });
            e.HasIndex(x => new { x.AnalistaId, x.Fecha }, "IX_Requerimientos_AbiertosPorAnalista").HasFilter("[Estado] <> N'Ok'");
            e.HasIndex(x => x.Fecha);
            e.HasIndex(x => x.OkAt).HasFilter("[OkAt] IS NOT NULL");
            e.HasIndex(x => new { x.Tipo, x.AnalistaId }, "IX_Requerimientos_Tipo_Analista").IncludeProperties(x => x.OkAt);
            e.HasIndex(x => x.SolicitudId, "IX_Requerimientos_SinAsignar").HasFilter("[AnalistaId] IS NULL AND [SolicitudId] IS NOT NULL").IncludeProperties(x => x.Tipo);
            e.HasIndex(x => x.Job);
            e.HasIndex(x => x.SolicitudId);
            e.Ignore(x => x.EsComercial);
        });

        b.Entity<Notificacion>(e =>
        {
            e.ToTable("Notificaciones");
            e.Property(x => x.To).HasMaxLength(2000);
            e.Property(x => x.Cc).HasMaxLength(2000);
            e.Property(x => x.Subject).HasMaxLength(400);
            e.Property(x => x.Error).HasMaxLength(2000);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => x.CreatedAt);
            e.Ignore(x => x.ToList);
            e.Ignore(x => x.CcList);
        });

        b.Entity<AuditoriaAcceso>(e =>
        {
            e.ToTable("AuditoriaAccesos");
            e.Property(x => x.Usuario).HasMaxLength(200);
            e.Property(x => x.Accion).HasMaxLength(60);
            e.Property(x => x.Metodo).HasMaxLength(40);
            e.Property(x => x.Ip).HasMaxLength(60);
            e.Property(x => x.Detalle).HasMaxLength(1000);
            e.HasIndex(x => x.At);
        });

        b.Entity<PasswordResetToken>(e =>
        {
            e.ToTable("PasswordResetTokens");
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.Ip).HasMaxLength(60);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Comentario>(e =>
        {
            e.ToTable("Comentarios");
            e.Property(x => x.Texto).HasMaxLength(Reglas.ComentarioMax).IsRequired();
            e.Property(x => x.Menciones).HasMaxLength(1000);
            e.HasOne<Requerimiento>().WithMany().HasForeignKey(x => x.RequerimientoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Autor).WithMany().HasForeignKey(x => x.AutorId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.RequerimientoId, x.CreadoAt });
            e.Ignore(x => x.MencionIds);
        });

        b.Entity<OkLog>(e =>
        {
            e.ToTable("OkLog");
            e.HasIndex(x => x.ReqId);
        });
    }

    /// <summary>
    /// Un índice único violado (mismo correo, RUT o cuenta de dominio guardados a la vez por dos personas) llega como
    /// aviso para el usuario en vez de un error técnico.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try { return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } sql)
        {
            throw new ReglaException(sql.Message.Contains("Email") ? "Ya existe una cuenta con ese correo."
                : sql.Message.Contains("Rut") ? "Ese RUT ya está registrado."
                : sql.Message.Contains("DomainAccount") ? "Esa cuenta de dominio ya está asociada a otro usuario."
                : "Ya existe un registro con esos datos. Revisa y vuelve a intentarlo.");
        }
    }
}

public sealed class AppDbContextFactory(IDbContextFactory<AppDbContext> inner) : IAppDbContextFactory
{
    public IAppDbContext Create() => inner.CreateDbContext();
}
