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
            e.HasIndex(x => x.AnalistaId);
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
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<OkLog>(e =>
        {
            e.ToTable("OkLog");
            e.HasIndex(x => x.ReqId);
        });
    }
}

public sealed class AppDbContextFactory(IDbContextFactory<AppDbContext> inner) : IAppDbContextFactory
{
    public IAppDbContext Create() => inner.CreateDbContext();
}
