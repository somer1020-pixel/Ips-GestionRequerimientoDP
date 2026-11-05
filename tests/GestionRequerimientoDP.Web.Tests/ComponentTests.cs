using GestionRequerimientoDP.Infrastructure.Data;
using System.Security.Claims;
using Bunit;
using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Application.Tests;
using GestionRequerimientoDP.Domain;
using GestionRequerimientoDP.Infrastructure.Security;
using GestionRequerimientoDP.Web.Auth;
using GestionRequerimientoDP.Web.Components.Dialogs;
using GestionRequerimientoDP.Web.Components.Layout;
using GestionRequerimientoDP.Web.Components.Pages;
using GestionRequerimientoDP.Web.Components.Shared;
using GestionRequerimientoDP.Web.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GestionRequerimientoDP.Web.Tests;

public class DueMarkTests : BunitContext
{
    static readonly DateOnly Viernes = new(2026, 10, 23);

    [Fact]
    public void Rojo_si_vence_hoy_o_vencido()
    {
        var cut = Render<DueMark>(p => p.Add(x => x.R, Make.Req(Equipo.Procesamiento, Viernes, 1)).Add(x => x.Today, Viernes));
        Assert.Contains("var(--danger)", cut.Markup);
        Assert.Contains("Vence hoy", cut.Markup);
    }

    [Fact]
    public void Amarillo_si_vence_el_proximo_dia_habil()
    {
        var cut = Render<DueMark>(p => p.Add(x => x.R, Make.Req(Equipo.Procesamiento, Viernes.AddDays(3), 1)).Add(x => x.Today, Viernes));
        Assert.Contains("oklch(0.86 0.17 92)", cut.Markup);
        Assert.Contains("Vence el próximo día hábil", cut.Markup);
    }

    [Fact]
    public void Sin_icono_con_OK()
    {
        var cut = Render<DueMark>(p => p.Add(x => x.R, Make.Req(Equipo.Procesamiento, Viernes, 1, estado: Estado.Ok)).Add(x => x.Today, Viernes));
        Assert.DoesNotContain("<svg", cut.Markup);
    }
}

public class ReqCardTests : BunitContext
{
    [Fact]
    public void Atrasado_sin_gestion_y_boton_Dar_OK_solo_en_curso()
    {
        var today = new DateOnly(2026, 10, 22);
        var r = Make.Req(Equipo.Procesamiento, new DateOnly(2026, 10, 19), 1);
        var cut = Render<ReqCard>(p => p.Add(x => x.R, r).Add(x => x.Today, today).Add(x => x.AnalistaNombre, "Andrés Salinas"));
        Assert.Contains("Atrasado sin gestión · Lun 19 oct", cut.Markup);
        Assert.Empty(cut.FindAll("button"));
        r.Estado = Estado.EnCurso;
        r.Actualizado = Fechas.FromLocal(today, 9, 0);
        cut = Render<ReqCard>(p => p.Add(x => x.R, r).Add(x => x.Today, today));
        Assert.Contains("Atrasado · Lun 19 oct", cut.Markup);
        Assert.Contains("Dar OK", cut.Find("button").TextContent);
    }
}

/// <summary>Componentes con servicios reales sobre BD en memoria con los datos semilla.</summary>
public abstract class AppComponentTest : BunitContext
{
    protected static readonly DateOnly Martes = new(2026, 10, 20);
    protected readonly TestApp App = new(Martes);

    protected AppComponentTest()
    {
        App.SeedAsync().GetAwaiter().GetResult();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var s = Services;
        s.AddSingleton(App.Clock);
        s.AddSingleton<IChangeNotifier>(App.Changes);
        s.AddSingleton<IOptionsMonitor<ReglasOptions>>(new OptionsMonitorStub(App.Reglas));
        s.AddSingleton<IOptions<AuthOptions>>(Options.Create(new AuthOptions { DemoMode = true }));
        s.AddSingleton<IAppDbContextFactory>(App.Factory);
        s.AddSingleton<IPasswordService, PasswordService>();
        s.AddScoped<UiState>();
        s.AddSingleton<INotificationService>(App.Notif);
        s.AddSingleton<IRequerimientoService>(App.Reqs);
        s.AddSingleton<IOkService>(App.Ok);
        s.AddSingleton<ISolicitudService>(App.Sols);
        s.AddSingleton<IScriptAssignmentService>(App.Scripts);
        s.AddScoped<IUsuarioService, UsuarioService>();
        s.AddScoped<IConsultaService, ConsultaService>();
        s.AddScoped<IAccesoService, AccesoService>();
        s.AddScoped<IAuditoriaService, AuditoriaService>();
        s.AddScoped<IComentarioService, ComentarioService>();
    }

    protected UsuarioActual LoginAs(string email)
    {
        var me = App.UserAsync(email).GetAwaiter().GetResult();
        var auth = AddAuthorization();
        auth.SetAuthorized(me.Nombre);
        auth.SetRoles(me.Rol.ToString());
        auth.SetClaims(new Claim(Claims.UserId, me.Id.ToString()), new Claim(Claims.Equipos, string.Join(',', me.Equipos)));
        return me;
    }

    protected Requerimiento FirstReq(Func<Requerimiento, bool> f)
    {
        using var db = (AppDbContext)App.Factory.Create();
        return db.Requerimientos.AsNoTracking().AsEnumerable().First(f);
    }

    sealed class OptionsMonitorStub(ReglasOptions v) : IOptionsMonitor<ReglasOptions>
    {
        public ReglasOptions CurrentValue => v;
        public ReglasOptions Get(string? name) => v;
        public IDisposable? OnChange(Action<ReglasOptions, string?> listener) => null;
    }
}

public class OkModalTests : AppComponentTest
{
    [Fact]
    public void Codificacion_pide_registros_y_metodo()
    {
        LoginAs("rfuentes@empresa.cl");
        var r = FirstReq(x => x.Tipo == Equipo.Codificacion && x.Estado != Estado.Ok && x.Fecha >= Martes);
        var cut = Render<OkModal>(p => p.Add(x => x.Id, r.Id));
        cut.WaitForAssertion(() => Assert.Contains("Registros codificados", cut.Markup));
        Assert.Contains("Método", cut.Markup);
        Assert.DoesNotContain("Horas dedicadas", cut.Markup);
        Assert.DoesNotContain("Entrega con atraso", cut.Markup);
    }

    [Fact]
    public void Procesamiento_atrasado_pide_horas_y_motivo()
    {
        LoginAs("cmendez@empresa.cl");
        var r = FirstReq(x => x.Tipo == Equipo.Procesamiento && x.Estado != Estado.Ok && x.Fecha < Martes);
        var cut = Render<OkModal>(p => p.Add(x => x.Id, r.Id));
        cut.WaitForAssertion(() => Assert.Contains("Horas dedicadas", cut.Markup));
        Assert.Contains("Entrega con atraso", cut.Markup);
        Assert.Contains("Responsable del atraso", cut.Markup);
        Assert.Contains("Procesamiento", cut.Find(".dialog .seg").TextContent); // nunca "Operaciones"
        Assert.DoesNotContain("Operaciones", cut.Find(".dialog .seg").TextContent);
        cut.Find(".dialog-actions .btn-primary").Click();
        cut.WaitForAssertion(() => Assert.Contains("horas", cut.Find(".err").TextContent));
    }
}

public class MenuTests : AppComponentTest
{
    [Theory]
    [InlineData("cmendez@empresa.cl", 9, "Usuarios")]
    [InlineData("asalinas@empresa.cl", 2, "Mi semana")]
    [InlineData("mayala@empresa.cl", 2, "Nueva solicitud")]
    [InlineData("porellana@empresa.cl", 2, "Mis solicitudes")]
    public void Items_segun_rol(string email, int items, string incluye)
    {
        LoginAs(email);
        var cut = Render<MainLayout>(p => p.Add(x => x.Body, (RenderFragment)(b => b.AddContent(0, "contenido"))));
        cut.WaitForAssertion(() => Assert.Equal(items, cut.FindAll(".side .nav-i").Count));
        Assert.Contains(incluye, cut.Find(".side").TextContent);
        if (email.StartsWith("asalinas")) Assert.DoesNotContain("Usuarios", cut.Find(".side").TextContent);
        Assert.Equal(email.StartsWith("cmendez"), cut.Markup.Contains("Nuevo requerimiento"));
    }

    [Theory]
    [InlineData("cmendez@empresa.cl", false)]
    [InlineData("admin.ops@empresa.cl", true)]
    public async Task Con_administrador_solo_el_ve_Usuarios(string email, bool ve)
    {
        await using (var db = App.Factory.Create())
        {
            db.Usuarios.Add(new Usuario { Nombre = "Admin Ops", Email = "admin.ops@empresa.cl", Rol = Rol.Admin, Activo = true });
            await db.SaveChangesAsync();
        }
        LoginAs(email);
        var cut = Render<MainLayout>(p => p.Add(x => x.Body, (RenderFragment)(b => b.AddContent(0, "contenido"))));
        cut.WaitForAssertion(() => Assert.Equal(ve ? 9 : 8, cut.FindAll(".side .nav-i").Count));
        Assert.Equal(ve, cut.Find(".side").TextContent.Contains("Usuarios"));
        Assert.Contains("Nuevo requerimiento", cut.Markup);
        if (ve) Assert.Contains("Administrador", cut.Markup);
    }
}

public class ConsultaTests : AppComponentTest
{
    [Fact]
    public void Muestra_estado_sin_notas()
    {
        var auth = AddAuthorization();
        auth.SetNotAuthorized();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/consulta?job=26-000802-02");
        var cut = Render<Consulta>();
        cut.WaitForAssertion(() => Assert.Contains("Tracking Banco de Chile Ola octubre", cut.Markup));
        Assert.Contains("Por asignar", cut.Markup);
        Assert.DoesNotContain("Materiales llegan el lunes", cut.Markup); // nota de la solicitud
    }
}

public class DialogosTests : AppComponentTest
{
    [Fact]
    public async Task Detalle_muestra_el_hilo_y_publicar_menciona_y_avisa()
    {
        LoginAs("cmendez@empresa.cl");
        int reqId; string nombre;
        await using (var db = App.Factory.Create())
        {
            var r = await db.Requerimientos.FirstAsync(x => x.AnalistaId != null);
            reqId = r.Id;
            nombre = (await db.Usuarios.SingleAsync(u => u.Id == r.AnalistaId)).Nombre;
        }
        JSInterop.Setup<string>("grdp.mencion.valor", _ => true).SetResult($"@{nombre} ¿cómo vas?");
        var cut = Render<ReqDetailModal>(p => p.Add(x => x.Id, reqId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("textarea[data-mencion]")));
        Assert.Contains(nombre, cut.Find("textarea[data-mencion]").GetAttribute("data-mencion"));
        cut.FindAll("button").First(b => b.TextContent.Contains("Comentar")).Click();
        cut.WaitForAssertion(() => Assert.Equal("@" + nombre, cut.Find(".com .mencion").TextContent));
        Assert.Contains("¿cómo vas?", cut.Find(".com-texto").TextContent);
        await using var db2 = App.Factory.Create();
        Assert.Single(await db2.Notificaciones.Where(n => n.Kind == NotificacionKind.Mencion).ToListAsync());
    }

    [Fact]
    public void Nuevo_requerimiento_no_sugiere_una_fecha_pasada_y_al_pasar_a_Script_la_recalcula()
    {
        LoginAs("cmendez@empresa.cl");
        var ui = Services.GetRequiredService<UiState>();
        var hoy = App.Clock.Today; // martes
        ui.PlanWeek = Fechas.MondayOf(hoy); // semana actual: su lunes ya pasó
        var cut = Render<NewReqModal>(p => p.Add(x => x.Tipo, Equipo.Procesamiento));
        cut.WaitForAssertion(() => Assert.Equal(Fechas.Iso(hoy), cut.Find("#nr-fecha").GetAttribute("value")));
        cut.FindAll(".seg-opt input").Last().Change(true); // Script
        cut.WaitForAssertion(() => Assert.Equal(Fechas.Iso(hoy.AddDays(2)), cut.Find("#nr-fecha").GetAttribute("value")));
        cut.Find("#nr-fecha").Change(Fechas.Iso(hoy.AddDays(9))); // fecha elegida a mano
        cut.FindAll(".seg-opt input").First().Change(true); // vuelve a Procesamiento: no pisa la elegida
        cut.WaitForAssertion(() => Assert.Equal(Fechas.Iso(hoy.AddDays(9)), cut.Find("#nr-fecha").GetAttribute("value")));
    }

    [Fact]
    public void Dar_OK_desde_el_detalle_abre_ambos_dialogos_del_mismo_requerimiento_sin_error()
    {
        LoginAs("cmendez@empresa.cl");
        var r = FirstReq(x => x.Estado != Estado.Ok);
        var ui = Services.GetRequiredService<UiState>();
        var cut = Render<MainLayout>(p => p.Add(x => x.Body, (RenderFragment)(b => b.AddContent(0, "contenido"))));
        cut.InvokeAsync(() => { ui.OpenDetail(r.Id); ui.AskOk(r.Id); }); // antes: "More than one sibling ... has the same key value"
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[aria-label='Detalle del requerimiento']"));
            Assert.Contains("Confirmar OK", cut.Markup);
        });
    }
}
