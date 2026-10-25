using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Domain;

namespace GestionRequerimientoDP.Application.Tests;

public class ScriptRankingTests
{
    static readonly DateOnly Today = new(2026, 10, 21);

    [Fact]
    public void Menos_activos_primero_y_desempate_por_DP_y_ultimo_OK()
    {
        var a = Make.Analista(1, Equipo.Script);
        var b = Make.Analista(2, Equipo.Script, Equipo.Procesamiento);
        var c = Make.Analista(3, Equipo.Script);
        var reqs = new List<Requerimiento>
        {
            Make.Req(Equipo.Script, Today, 1), // A: 1 activo
            Make.Req(Equipo.Procesamiento, Today, 2), // B: 0 activos, 1 DP hoy
            Make.Req(Equipo.Script, Today.AddDays(-5), 3, estado: Estado.Ok), // C: 0 activos, OK reciente
        };
        reqs[2].OkAt = Fechas.FromLocal(Today.AddDays(-1), 10, 0);
        var rank = ScriptRanking.Rank([a, b, c], reqs, Today);
        Assert.Equal([3, 2, 1], rank.Select(x => x.Analista.Id)); // C (0 activos, 0 DP) · B (0 activos, 1 DP) · A
        Assert.Equal(1, rank[1].DpHoy);
    }

    [Fact]
    public void Ultimo_OK_mas_antiguo_gana_el_empate()
    {
        var a = Make.Analista(1, Equipo.Script);
        var b = Make.Analista(2, Equipo.Script);
        var ra = Make.Req(Equipo.Script, Today.AddDays(-3), 1, estado: Estado.Ok); ra.OkAt = Fechas.FromLocal(Today.AddDays(-1), 9, 0);
        var rb = Make.Req(Equipo.Script, Today.AddDays(-3), 2, estado: Estado.Ok); rb.OkAt = Fechas.FromLocal(Today.AddDays(-4), 9, 0);
        Assert.Equal(2, ScriptRanking.Rank([a, b], [ra, rb], Today)[0].Analista.Id);
    }

    [Fact]
    public void WIP_lleno_recomienda_igual_al_menos_cargado_con_aviso()
    {
        var a = Make.Analista(1, Equipo.Script);
        var b = Make.Analista(2, Equipo.Script);
        var reqs = Enumerable.Range(0, 4).Select(_ => Make.Req(Equipo.Script, Today, 1))
            .Concat(Enumerable.Range(0, 3).Select(_ => Make.Req(Equipo.Script, Today, 2))).ToList();
        var top = ScriptRanking.Rank([a, b], reqs, Today)[0];
        Assert.Equal(2, top.Analista.Id);
        Assert.Contains("al límite", ScriptRanking.Motivo(top, 3));
    }

    [Fact]
    public void Inactivos_no_participan()
    {
        var a = Make.Analista(1, Equipo.Script); a.Activo = false;
        Assert.Empty(ScriptRanking.Rank([a], [], Today));
    }
}

public class BalanceEngineTests
{
    static readonly DateOnly W = new(2026, 10, 26);

    static List<Requerimiento> Job(string job, int? an, int n, Estado e = Estado.Pendiente) =>
        Enumerable.Range(0, n).Select(i => Make.Req(Equipo.Procesamiento, W.AddDays(i % 5), an, job, e)).ToList();

    static Dictionary<int, int> Loads(IEnumerable<Requerimiento> reqs, List<BalanceMove> moves)
    {
        var asg = reqs.ToDictionary(r => r.Id, r => r.AnalistaId);
        foreach (var m in moves) foreach (var id in m.Ids) asg[id] = m.To;
        return asg.Values.Where(v => v != null).GroupBy(v => v!.Value).ToDictionary(g => g.Key, g => g.Count());
    }

    [Fact]
    public void Converge_sin_separar_jobs()
    {
        var team = new[] { Make.Analista(1, Equipo.Procesamiento), Make.Analista(2, Equipo.Procesamiento) };
        var reqs = Job("J1", 1, 3).Concat(Job("J2", 1, 2)).Concat(Job("J3", 1, 2)).Concat(Job("J4", 1, 1)).ToList();
        var moves = BalanceEngine.Suggest(Equipo.Procesamiento, W, team, reqs, 10);
        Assert.NotEmpty(moves);
        foreach (var m in moves) Assert.Equal(reqs.Count(r => r.Job == m.Job), m.Ids.Count); // job completo
        var loads = Loads(reqs, moves);
        Assert.True(Math.Abs(loads.GetValueOrDefault(1) - loads.GetValueOrDefault(2)) <= 1);
    }

    [Fact]
    public void Nunca_mueve_jobs_con_entregables_en_curso_u_OK()
    {
        var team = new[] { Make.Analista(1, Equipo.Procesamiento), Make.Analista(2, Equipo.Procesamiento) };
        var reqs = Job("J1", 1, 3, Estado.EnCurso).Concat(Job("J2", 1, 3, Estado.Ok)).ToList();
        reqs.Add(Make.Req(Equipo.Procesamiento, W, 1, "J3", Estado.EnCurso));
        reqs.Add(Make.Req(Equipo.Procesamiento, W, 1, "J3", Estado.Pendiente));
        Assert.Empty(BalanceEngine.Suggest(Equipo.Procesamiento, W, team, reqs, 10));
    }

    [Fact]
    public void Sin_asignar_va_por_job_al_menos_cargado_y_scripts_suman_carga()
    {
        var team = new[] { Make.Analista(1, Equipo.Procesamiento, Equipo.Script), Make.Analista(2, Equipo.Procesamiento) };
        var reqs = Job("J1", null, 2).ToList();
        reqs.Add(Make.Req(Equipo.Script, W, 1, "S1"));
        var moves = BalanceEngine.Suggest(Equipo.Procesamiento, W, team, reqs, 10);
        var first = Assert.Single(moves);
        Assert.Null(first.From);
        Assert.Equal(2, first.To); // A1 ya tiene un script
        Assert.DoesNotContain(moves, m => m.Job == "S1"); // los scripts nunca se mueven
    }
}

public class ImportParserTests
{
    [Fact]
    public void ToIsoDate_formatos()
    {
        Assert.Equal(new DateOnly(2026, 10, 19), ImportParser.ToIsoDate(46314d)); // serial de Excel
        Assert.Equal(new DateOnly(2026, 10, 19), ImportParser.ToIsoDate("19/10/2026"));
        Assert.Equal(new DateOnly(2026, 10, 19), ImportParser.ToIsoDate("10/19/2026")); // m/d si el segundo > 12
        Assert.Equal(new DateOnly(2026, 3, 4), ImportParser.ToIsoDate("4/3/26"));
        Assert.Equal(new DateOnly(2026, 10, 19), ImportParser.ToIsoDate("2026-10-19"));
        Assert.Equal(new DateOnly(2026, 10, 19), ImportParser.ToIsoDate(new DateTime(2026, 10, 19)));
        Assert.Null(ImportParser.ToIsoDate("hola"));
        Assert.Null(ImportParser.ToIsoDate("31/02/2026"));
    }

    [Fact]
    public void Encabezados_duplicados_y_analistas()
    {
        var team = new[] { new Usuario { Id = 7, Nombre = "Andrés Salinas", Rol = Rol.Analista } };
        var existing = new[] { new Requerimiento { Fecha = new DateOnly(2026, 10, 19), Job = "J0", Entregable = "BD", Descripcion = "x", AnalistaId = 7 } };
        var matrix = ImportParser.FromText(
            "DÍA\tCliente\tEstudio\tJOB\tEntregable\tDescripción\tAnalista\n" +
            "19-10-2026\tC1\tP1\tJ0\tBD\tx\tandres salinas\n" +   // duplicado contra la BD
            "20/10/2026\tC1\tP1\tJ1\tBD\ty\tAndres Salinas\n" +
            "20/10/2026\tC1\tP1\tJ1\tBD\ty\tAndrés Salinas\n" +   // duplicado en el lote
            "21/10/2026\tC2\tP2\tJ2\tSPSS\tz\tNadie Conocido\n" +
            "\tC3\tP3\tJ3\tBD\tz\t\n");                          // sin DIA
        var res = ImportParser.Parse(matrix, team, existing);
        Assert.Equal(2, res.Rows.Count);
        Assert.Equal(2, res.Duplicados);
        Assert.Equal(1, res.Invalidas);
        Assert.Equal(["Nadie Conocido"], res.NoReconocidos);
        Assert.Equal(7, res.Rows[0].AnalistaId);
        Assert.Null(res.Rows[1].AnalistaId);
        Assert.Equal("P1", res.Rows[0].Proyecto); // ESTUDIO → PROYECTO
        Assert.Equal([new DateOnly(2026, 10, 19)], res.Semanas);
    }

    [Fact]
    public void Sin_encabezado_usa_el_orden_de_la_planilla()
    {
        var res = ImportParser.Parse(ImportParser.FromText("2026-10-20;CL;BHT;IDP;Cliente;Proyecto;J9;BD;Desc;Com;"), [], []);
        var r = Assert.Single(res.Rows);
        Assert.Equal("J9", r.Job);
        Assert.Equal("BHT", r.SL);
    }
}

public class OkValidatorTests
{
    static readonly DateOnly Today = new(2026, 10, 21);

    [Theory]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("201", false)]
    [InlineData("3,5", true)]
    [InlineData("3.25", true)]
    public void Horas_obligatorias(string horas, bool valido)
    {
        var r = Make.Req(Equipo.Procesamiento, Today, 1);
        var (err, p) = OkValidator.Validate(r, new OkInput { Horas = horas }, Today);
        Assert.Equal(valido, err is null);
        if (valido) Assert.True(p!.Horas > 0);
    }

    [Fact]
    public void Codificacion_pide_registros_y_metodo_sin_horas()
    {
        var r = Make.Req(Equipo.Codificacion, Today, 1);
        Assert.NotNull(OkValidator.Validate(r, new OkInput { Registros = "1200" }, Today).Error);
        var (err, p) = OkValidator.Validate(r, new OkInput { Registros = "1.200", Metodo = MetodoCodificacion.IA }, Today);
        Assert.Null(err);
        Assert.Equal(1200, p!.Registros);
        Assert.Null(p.Horas);
    }

    [Fact]
    public void Materiales_no_puede_ser_futuro()
    {
        var r = Make.Req(Equipo.Script, Today, 1);
        Assert.Contains("materiales", OkValidator.Validate(r, new OkInput { Horas = "1", Materiales = Today.AddDays(1) }, Today).Error);
    }

    [Fact]
    public void Atraso_exige_responsable_motivo_y_area_de_campo()
    {
        var r = Make.Req(Equipo.Procesamiento, Today.AddDays(-2), 1);
        Assert.Contains("atrasada", OkValidator.Validate(r, new OkInput { Horas = "2" }, Today).Error);
        Assert.Contains("campo", OkValidator.Validate(r, new OkInput { Horas = "2", Resp = AtrasoResponsable.Campo, Motivo = "Cuotas no cumplidas" }, Today).Error);
        var (err, p) = OkValidator.Validate(r, new OkInput { Horas = "2", Resp = AtrasoResponsable.Campo, Motivo = "Cuotas no cumplidas", Campo = "CATI" }, Today);
        Assert.Null(err);
        Assert.Equal("CATI", p!.Campo);
    }

    [Fact]
    public void Sugiere_comercial_si_los_materiales_llegaron_tarde()
    {
        var r = Make.Req(Equipo.Procesamiento, Today.AddDays(-2), 1);
        r.MaterialesAt = r.Fecha;
        var d = OkInput.Defaults(r, Today);
        Assert.Equal(AtrasoResponsable.Comercial, d.Resp);
        Assert.Equal("Materiales entregados tarde", d.Motivo);
    }
}
