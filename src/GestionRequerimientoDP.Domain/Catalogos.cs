namespace GestionRequerimientoDP.Domain;

/// <summary>Catálogos fijos (§6.3 del handoff). Si deben editarse sin despliegue, migrarlos a tablas.</summary>
public static class Catalogos
{
    /// <summary>Service lines (campo SL, antes BU).</summary>
    public static readonly IReadOnlyList<string> SLS = ["CEX", "BHT", "MSU", "AUM", "UU", "Observer", "CHP"];

    /// <summary>Técnicas / áreas de campo.</summary>
    public static readonly IReadOnlyList<string> CAMPOS = ["CATI", "F2F", "MS", "IDP", "IVR", "IIS", "Online", "WhatsApp", "EFM"];

    /// <summary>Entregables de Procesamiento.</summary>
    public static readonly IReadOnlyList<string> ENT_SOL = ["Proceso de datos", "Base de datos cliente Excel", "Base de datos SPSS", "Tablero", "Otros"];

    /// <summary>Tipos de requerimiento de Script.</summary>
    public static readonly IReadOnlyList<string> ENT_SCRIPT = ["Programación", "Configurar nueva ola", "Cargar base de datos", "Cuotas", "Mailing"];

    /// <summary>Entregables de Codificación.</summary>
    public static readonly IReadOnlyList<string> ENT_COD = ["Codificación preguntas abiertas", "Codificación de marcas", "Codificación de ocupación", "Libro de códigos", "Otros"];

    /// <summary>BU históricas de la planilla (Prog DP.xlsx).</summary>
    public static readonly IReadOnlyList<string> BUS = ["BHT", "CEX Chile (Ex Loyalty)", "Ops (Operaciones)", "Ipsos UU (Ex Marketing Cualitativo)"];

    public static readonly IReadOnlyDictionary<AtrasoResponsable, IReadOnlyList<string>> MOTIVOS = new Dictionary<AtrasoResponsable, IReadOnlyList<string>>
    {
        [AtrasoResponsable.Operaciones] = ["Sobrecarga del equipo", "Error o retrabajo interno", "Complejidad subestimada", "Problema técnico / sistemas"],
        [AtrasoResponsable.Comercial] = ["Materiales entregados tarde", "Materiales incompletos o con errores", "Cambio de especificaciones"],
        [AtrasoResponsable.Campo] = ["Cierre de campo tardío", "Base de campo con errores", "Cuotas no cumplidas"],
    };

    public const string MotivoMaterialesTarde = "Materiales entregados tarde";

    public static IReadOnlyList<string> EntregablesDe(Equipo t) => t switch
    {
        Equipo.Codificacion => ENT_COD,
        Equipo.Script => ENT_SCRIPT,
        _ => ENT_SOL,
    };
}
