using GestionRequerimientoDP.Domain;

namespace GestionRequerimientoDP.Application.Tests;

public class ReglasTests
{
    [Theory]
    [InlineData("CATI (Enc. Telefónicas)", "CATI")]
    [InlineData("Intercepción", "F2F")]
    [InlineData("Qualtrics", "Online")]
    [InlineData("IDP", "IDP")]
    [InlineData("IIS", "IIS")]
    [InlineData("Mystery shopper", "MS")]
    [InlineData("WhatsApp", "WhatsApp")]
    [InlineData("Data processing", "")]
    [InlineData("", "")]
    public void CampoFrom_reconoce_la_tecnica(string tecnica, string esperado) => Assert.Equal(esperado, Reglas.CampoFrom(tecnica));

    [Fact]
    public void NextBiz_de_viernes_es_lunes()
    {
        Assert.Equal(new DateOnly(2026, 10, 26), Fechas.NextBiz(new DateOnly(2026, 10, 23)));
        Assert.Equal(new DateOnly(2026, 10, 21), Fechas.NextBiz(new DateOnly(2026, 10, 20)));
    }

    [Fact]
    public void Iconos_rojo_y_amarillo()
    {
        var friday = new DateOnly(2026, 10, 23);
        Assert.True(Reglas.IsWarn(Make.Req(Equipo.Procesamiento, new DateOnly(2026, 10, 26), 1), friday)); // un viernes, el lunes ya es amarillo
        Assert.True(Reglas.IsDue(Make.Req(Equipo.Procesamiento, friday, 1), friday));
        Assert.False(Reglas.IsDue(Make.Req(Equipo.Procesamiento, friday, 1, estado: Estado.Ok), friday));
        Assert.False(Reglas.IsWarn(Make.Req(Equipo.Procesamiento, new DateOnly(2026, 10, 27), 1), friday));
    }

    [Fact]
    public void Atrasado_y_sin_gestion()
    {
        var today = new DateOnly(2026, 10, 22);
        var r = Make.Req(Equipo.Procesamiento, new DateOnly(2026, 10, 20), 1, estado: Estado.EnCurso);
        r.Actualizado = Fechas.FromLocal(today.AddDays(-1), 9, 0);
        Assert.True(Reglas.Atrasado(r, today));
        Assert.False(Reglas.SinGestion(r, today, 2));
        r.Actualizado = Fechas.FromLocal(today.AddDays(-2), 9, 0);
        Assert.True(Reglas.SinGestion(r, today, 2));
        r.Estado = Estado.Pendiente;
        r.Actualizado = Fechas.FromLocal(today, 9, 0);
        Assert.True(Reglas.SinGestion(r, today, 2)); // pendiente = sin gestión
    }

    [Fact]
    public void EffResp_usa_la_unidad_y_nunca_dice_Operaciones()
    {
        var r = Make.Req(Equipo.Script, new DateOnly(2026, 10, 20), 1, estado: Estado.Ok);
        r.OkAt = Fechas.FromLocal(new DateOnly(2026, 10, 22), 10, 0);
        r.AtrasoResp = AtrasoResponsable.Operaciones;
        Assert.Equal("op_script", Reglas.EffResp(r));
        Assert.Equal("Script", Reglas.RespLabel(r));
        r.AtrasoResp = AtrasoResponsable.Campo; r.AtrasoCampo = "CATI";
        Assert.Equal("Campo · CATI", Reglas.RespLabel(r));
        Assert.True(Reglas.IsLateOk(r));
        Assert.Equal(2, Reglas.DiasAtraso(r));
    }

    [Fact]
    public void TeamLeadersOf_por_equipo_o_todos()
    {
        Usuario L(int id, params Equipo[] e) => new() { Id = id, Rol = Rol.Lider, Activo = true, Equipos = e.Select(x => new UsuarioEquipo { Equipo = x }).ToList() };
        var users = new[] { L(1, Equipo.Procesamiento), L(2, Equipo.Codificacion), L(3) };
        Assert.Equal([1, 3], Reglas.TeamLeadersOf(users, Equipo.Procesamiento).Select(u => u.Id));
        Assert.Equal([3], Reglas.TeamLeadersOf(users, Equipo.Script).Select(u => u.Id));
        Assert.Equal([1, 2], Reglas.TeamLeadersOf(users.Take(2), Equipo.Script).Select(u => u.Id)); // nadie a cargo → todos
    }

    [Fact]
    public void Formatos_es_CL()
    {
        Assert.Equal("Lun 19 oct", Fechas.FmtD(new DateOnly(2026, 10, 19)));
        Assert.Equal("19 – 23 oct", Fechas.WeekLabel(new DateOnly(2026, 10, 19)));
        Assert.Equal("28 sep – 2 oct", Fechas.WeekLabel(new DateOnly(2026, 9, 28)));
        Assert.Equal("3,5 h", Reglas.FmtH(3.5m));
        Assert.Equal("1.240", Reglas.FmtN(1240L));
    }
}
