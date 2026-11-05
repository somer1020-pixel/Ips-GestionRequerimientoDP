namespace GestionRequerimientoDP.Domain;

/// <summary>Comercial y PM (PM de Operaciones) solicitan trabajo a Operaciones: no tienen carga propia.</summary>
public enum Rol { Lider, Analista, Comercial, PM }

/// <summary>Equipo / unidad de Operaciones. Para un requerimiento es su tipo (proc / script / cod en el mockup).</summary>
public enum Equipo { Procesamiento, Script, Codificacion }

public enum Estado { Pendiente, EnCurso, Ok }

public enum MetodoCodificacion { IA, Manual }

public enum AtrasoResponsable { Operaciones, Comercial, Campo }

public enum NotificacionKind { Asignacion, Atraso, Solicitud, Acceso, SolicitudAsignada }

public enum NotificacionStatus { Pendiente, Enviado, Error, Omitido }

public enum OkAccion { Ok, Reabrir, CambioEstado, Asignacion }
