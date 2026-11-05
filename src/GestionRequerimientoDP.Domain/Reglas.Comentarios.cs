namespace GestionRequerimientoDP.Domain;

/// <summary>Un trozo de un comentario: texto normal o una mención (@Nombre) a un usuario.</summary>
public sealed record TrozoComentario(string Texto, int? UsuarioId);

public static partial class Reglas
{
    public const int ComentarioMax = 2000;

    /// <summary>
    /// Ubica las menciones "@Nombre Apellido" de un texto entre los candidatos (sin distinguir mayúsculas). Se prueban primero
    /// los nombres más largos para que "@Ana María" no se tome como "@Ana", y después del nombre no puede seguir una letra o
    /// un número ("@Analía" no menciona a "Ana").
    /// </summary>
    public static List<(int Inicio, int Largo, Usuario Usuario)> UbicarMenciones(string texto, IEnumerable<Usuario> candidatos)
    {
        var res = new List<(int, int, Usuario)>();
        if (string.IsNullOrEmpty(texto) || !texto.Contains('@')) return res;
        var tomado = new bool[texto.Length];
        foreach (var u in candidatos.Where(u => !string.IsNullOrWhiteSpace(u.Nombre)).OrderByDescending(u => u.Nombre.Length))
        {
            var token = "@" + u.Nombre.Trim();
            var i = 0;
            while ((i = texto.IndexOf(token, i, StringComparison.CurrentCultureIgnoreCase)) >= 0)
            {
                var fin = i + token.Length;
                var libre = !tomado.AsSpan(i, token.Length).Contains(true);
                var borde = fin >= texto.Length || !char.IsLetterOrDigit(texto[fin]);
                if (libre && borde)
                {
                    res.Add((i, token.Length, u));
                    for (var k = i; k < fin; k++) tomado[k] = true;
                }
                i = fin;
            }
        }
        return res.OrderBy(m => m.Item1).ToList();
    }

    /// <summary>Usuarios mencionados en el texto (sin repetir, en orden de aparición).</summary>
    public static List<Usuario> Menciones(string texto, IEnumerable<Usuario> candidatos) =>
        UbicarMenciones(texto, candidatos).Select(m => m.Usuario).DistinctBy(u => u.Id).ToList();

    /// <summary>Divide el comentario en texto y menciones para mostrarlo resaltado (solo se resaltan las menciones guardadas).</summary>
    public static List<TrozoComentario> Trozos(string texto, IEnumerable<Usuario> mencionados)
    {
        var res = new List<TrozoComentario>();
        var pos = 0;
        foreach (var (ini, largo, u) in UbicarMenciones(texto, mencionados))
        {
            if (ini > pos) res.Add(new(texto[pos..ini], null));
            res.Add(new(texto.Substring(ini, largo), u.Id));
            pos = ini + largo;
        }
        if (pos < texto.Length) res.Add(new(texto[pos..], null));
        return res;
    }
}
