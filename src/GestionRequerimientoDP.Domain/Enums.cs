namespace GestionRequerimientoDP.Domain;

/// <summary>Comercial y PM (PM de Operaciones) solicitan trabajo a Operaciones: no tienen carga propia.</summary>
/// <remarks>Admin = Administrador: gestiona como un líder a cargo de todos los equipos y es el único que administra usuarios;
/// solo recibe correos de lo que le asignan, del atraso diario y de solicitudes de acceso.</remarks>
public enum Rol { Lider, Analista, Comercial, PM, Admin }

/// <summary>Equipo / unidad de Operaciones. Para un requerimiento es su tipo (proc / script / cod en el mockup).</summary>
public enum Equipo { Procesamiento, Script, Codificacion }

public enum Estado { Pendiente, EnCurso, Ok }

public enum MetodoCodificacion { IA, Manual }

public enum AtrasoResponsable { Operaciones, Comercial, Campo }

public enum NotificacionKind { Asignacion, Atraso, Solicitud, Acceso, SolicitudAsignada }

public enum NotificacionStatus { Pendiente, Enviado, Error, Omitido }

public enum OkAccion { Ok, Reabrir, CambioEstado, Asignacion }
