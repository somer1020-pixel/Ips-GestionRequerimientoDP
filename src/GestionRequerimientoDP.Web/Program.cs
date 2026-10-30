using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Infrastructure;
using GestionRequerimientoDP.Web;
using GestionRequerimientoDP.Web.Auth;
using GestionRequerimientoDP.Web.Components;
using GestionRequerimientoDP.Web.Endpoints;
using GestionRequerimientoDP.Web.Ui;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.Configure<ReglasOptions>(builder.Configuration.GetSection(ReglasOptions.Section));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<UiState>();
builder.AddAppAuth();
builder.AddAppHosting();

var app = builder.Build();
app.UseAppHosting(); // proxy inverso / sub-ruta de IIS: primero de todo

// Si la base no responde al iniciar, la app no se cae: muestra la causa en el navegador
// (Visual Studio solo diría "No se puede conectar al servidor web").
var dbError = await StartupDatabase.InitializeAsync(app);
if (dbError != null)
    app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path.StartsWithSegments("/_framework") || ctx.Request.Path.StartsWithSegments("/css")) { await next(); return; }
        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(dbError);
    });

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseSsoErrorRedirect(); // antes de UseAuthentication: convierte el 400 de Negotiate en vuelta al ingreso
app.UseSesionSoloCookie(); // IIS: la identidad de Windows de la conexión no es la sesión de la app
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapAccountEndpoints();
app.MapSsoEndpoints();
app.MapExportEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
