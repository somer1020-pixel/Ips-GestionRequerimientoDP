using GestionRequerimientoDP.Domain;

namespace GestionRequerimientoDP.Application.Motores;

/// <summary>Movimiento propuesto: un job completo pasa de un analista a otro (From null = sin asignar).</summary>
public sealed record BalanceMove(string Job, IReadOnlyList<int> Ids, int? From, int To);

/// <summary>Propuesta de balanceo semanal (§3.1). No modifica los requerimientos recibidos.</summary>
public static class BalanceEngine
{
    public static List<BalanceMove> Suggest(Equipo unidad, DateOnly week, IReadOnlyList<Usuario> analysts, IEnumerable<Requerimiento> reqs, int capacity)
    {
        var team = analysts.Where(a => a.Activo && a.Rol == Rol.Analista && a.Tiene(unidad)).ToList();
        if (team.Count == 0) return [];

        var all = reqs.OrderBy(r => r.Id).ToList();
        var items = all.Where(r => r.Tipo == unidad && Fechas.MondayOf(r.Fecha) == week).ToList();
        var scripts = all.Where(r => r.Tipo == Equipo.Script && Fechas.MondayOf(r.Fecha) == week).ToList();
        // asignación de trabajo (propuesta) por id
        var asg = items.ToDictionary(r => r.Id, r => r.AnalistaId);

        int Load(int id) => items.Count(r => asg[r.Id] == id) + scripts.Count(r => r.AnalistaId == id);
        Usuario MinA() => team.OrderBy(a => Load(a.Id)).First();

        var moves = new List<(string Key, string Job, List<int> Ids, int? From, int To)>();
        void Rec(string job, List<int> ids, int? from, int to)
        {
            var key = job + "|" + from;
            var i = moves.FindIndex(m => m.Key == key);
            if (i >= 0) moves[i] = moves[i] with { To = to };
            else moves.Add((key, job, ids, from, to));
        }

        // 1. Sin analista: cada job va completo al menos cargado.
        foreach (var g in items.Where(r => r.AnalistaId == null).GroupBy(r => r.Job))
        {
            var to = MinA().Id;
            foreach (var r in g) asg[r.Id] = to;
            Rec(g.Key, g.Select(r => r.Id).ToList(), null, to);
        }

        // 2. Mover jobs completos (todo pendiente) del más cargado al menos cargado.
        for (var i = 0; i < 60; i++)
        {
            var so = team.OrderByDescending(a => Load(a.Id)).ToList();
            var mx = so[0];
            var mn = so[^1];
            var gap = Load(mx.Id) - Load(mn.Id);
            if (gap <= 1 && Load(mx.Id) <= capacity) break;
            var cand = items.Where(r => asg[r.Id] == mx.Id)
                .GroupBy(r => r.Job)
                .Select(g => g.ToList())
                .Where(g => g.All(r => r.Estado == Estado.Pendiente) && g.Count < gap)
                .OrderByDescending(g => g.Count)
                .ThenByDescending(g => g[0].Fecha)
                .FirstOrDefault();
            if (cand is null) break;
            foreach (var r in cand) asg[r.Id] = mn.Id;
            Rec(cand[0].Job, cand.Select(r => r.Id).ToList(), mx.Id, mn.Id);
        }

        return moves.Select(m => new BalanceMove(m.Job, m.Ids, m.From, m.To)).ToList();
    }
}
