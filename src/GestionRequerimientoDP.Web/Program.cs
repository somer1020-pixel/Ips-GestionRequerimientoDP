using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Infrastructure;
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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapAccountEndpoints();
app.MapExportEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.Services.InitializeDatabaseAsync(
    migrate: app.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()),
    seed: app.Configuration.GetValue("Database:Seed", app.Environment.IsDevelopment()),
    app.Logger);

app.Run();

public partial class Program;
