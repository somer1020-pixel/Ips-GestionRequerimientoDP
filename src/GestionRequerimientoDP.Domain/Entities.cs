namespace GestionRequerimientoDP.Domain;

public class Usuario
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Email { get; set; } = "";
    public Rol Rol { get; set; }
    public bool Activo { get; set; } = true;
    /// <summary>Cuenta de dominio (DOMINIO\usuario) para Windows Authentication.</summary>
    public string? DomainAccount { get; set; }
    /// <summary>Hash de contraseña (ASP.NET Core PasswordHasher). Nulo = solo acceso de demostración / Windows Auth.</summary>
    public string? PasswordHash { get; set; }
    /// <summary>RUT normalizado "12345678-K" (opcional). Permite ingresar con RUT en vez de correo.</summary>
    public string? Rut { get; set; }
    public Aprobacion Aprobacion { get; set; } = Aprobacion.Aprobado;
    public OrigenUsuario Origen { get; set; } = OrigenUsuario.Admin;
    /// <summary>Motivo o área informada al solicitar acceso.</summary>
    public string SolicitudMotivo { get; set; } = "";
    public DateTimeOffset? CreadoAt { get; set; }
    public DateTimeOffset? UltimoAcceso { get; set; }

    public bool Pendiente => Aprobacion == Aprobacion.Pendiente;
    /// <summary>Analista: equipos a los que pertenece. Líder: equipos a cargo (vacío = todos).</summary>
    public List<UsuarioEquipo> Equipos { get; set; } = [];

    public bool Tiene(Equipo e) => Equipos.Any(x => x.Equipo == e);
    public IEnumerable<Equipo> EquiposOrdenados => Equipos.Select(x => x.Equipo).OrderBy(x => x);
}

public class UsuarioEquipo
{
    public int UsuarioId { get; set; }
    public Equipo Equipo { get; set; }
}

public class Solicitud
{
    public int Id { get; set; }
    /// <summary>Correlativo S-####.</summary>
    public string Codigo { get; set; } = "";
    public int SolicitanteId { get; set; }
    public Usuario? Solicitante { get; set; }
    public DateTimeOffset CreadaAt { get; set; }
    public DateTimeOffset? DescargadaAt { get; set; }
    public string Nota { get; set; } = "";
    public List<Requerimiento> Requerimientos { get; set; } = [];
}

/// <summary>Un requerimiento equivale a un entregable (una fila de la planilla).</summary>
public class Requerimiento
{
    public int Id { get; set; }
    public Equipo Tipo { get; set; }
    /// <summary>Día de entrega (proc / cod) o fecha compromiso (script).</summary>
    public DateOnly Fecha { get; set; }
    public string Pais { get; set; } = "CL";
    /// <summary>SL / BU (campo "bu" del mockup).</summary>
    public string SL { get; set; } = "";
    public string Tecnica { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string Proyecto { get; set; } = "";
    public string Job { get; set; } = "";
    public string Entregable { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Comercial { get; set; } = "";
    public int? AnalistaId { get; set; }
    public Usuario? Analista { get; set; }
    public Estado Estado { get; set; }

    // Seguimiento
    public DateTimeOffset Creado { get; set; }
    public DateTimeOffset Actualizado { get; set; }
    public DateOnly? MaterialesAt { get; set; }
    public string Nota { get; set; } = "";

    // Cierre
    public DateTimeOffset? OkAt { get; set; }
    public int? OkById { get; set; }
    public string OkNota { get; set; } = "";
    public decimal? Horas { get; set; }
    public int? Registros { get; set; }
    public MetodoCodificacion? Metodo { get; set; }

    // Atraso
    public AtrasoResponsable? AtrasoResp { get; set; }
    public string AtrasoMotivo { get; set; } = "";
    public string AtrasoCampo { get; set; } = "";
    public string AtrasoDetalle { get; set; } = "";

    // Solicitud y alertas
    public int? SolicitudId { get; set; }
    public Solicitud? Solicitud { get; set; }
    public DateOnly? AlertAt { get; set; }

    public byte[]? RowVersion { get; set; }

    public bool EsComercial => SolicitudId != null;

    /// <summary>Limpia los datos de cierre y de atraso (al reabrir o volver a pendiente / en curso). Conserva MaterialesAt.</summary>
    public void LimpiarOk()
    {
        OkAt = null; OkById = null; OkNota = "";
        Horas = null; Registros = null; Metodo = null;
        AtrasoResp = null; AtrasoMotivo = ""; AtrasoDetalle = ""; AtrasoCampo = "";
    }
}

public class Notificacion
{
    public int Id { get; set; }
    public NotificacionKind Kind { get; set; }
    /// <summary>Correos separados por ';'.</summary>
    public string To { get; set; } = "";
    public string Cc { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>JSON con los ids de requerimientos.</summary>
    public string ReqIds { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public NotificacionStatus Status { get; set; }
    public string Error { get; set; } = "";

    public IEnumerable<string> ToList => Split(To);
    public IEnumerable<string> CcList => Split(Cc);
    static IEnumerable<string> Split(string s) => s.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public class OkLog
{
    public long Id { get; set; }
    public int ReqId { get; set; }
    public OkAccion Accion { get; set; }
    public int? UsuarioId { get; set; }
    public DateTimeOffset At { get; set; }
    public string Payload { get; set; } = "{}";
}
