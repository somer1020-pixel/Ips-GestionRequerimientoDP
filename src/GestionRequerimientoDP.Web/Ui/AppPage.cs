using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Domain;
using GestionRequerimientoDP.Web.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace GestionRequerimientoDP.Web.Ui;

/// <summary>
/// Base de las pantallas: usuario actual, recarga en tiempo real cuando otro circuito cambia datos
/// (ChangeNotifier) y manejo uniforme de errores de reglas y permisos.
/// </summary>
public abstract class AppPage : ComponentBase, IDisposable
{
    [CascadingParameter] public Task<AuthenticationState> AuthState { get; set; } = default!;
    [Inject] protected UiState Ui { get; set; } = default!;
    [Inject] protected IChangeNotifier Changes { get; set; } = default!;
    [Inject] protected IClock Clock { get; set; } = default!;
    [Inject] protected IOptionsMonitor<ReglasOptions> Opts { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;

    protected UsuarioActual Me { get; private set; } = default!;
    protected DateOnly Today => Clock.Today;
    protected DateOnly Cur => Fechas.MondayOf(Today);
    protected DateOnly Next => Cur.AddDays(7);
    protected bool Loaded { get; private set; }

    /// <summary>Tipos de cambio que obligan a recargar la vista.</summary>
    protected virtual ChangeKind[] ReloadOn => [ChangeKind.Requerimientos, ChangeKind.Usuarios];

    protected abstract Task LoadAsync();

    protected override async Task OnInitializedAsync()
    {
        Me = Claims.ToUsuario((await AuthState).User) ?? throw new InvalidOperationException("Sin sesión");
        Changes.Changed += OnChanged;
        await LoadAsync();
        Loaded = true;
    }

    int _pending;
    void OnChanged(ChangeKind kind)
    {
        if (!ReloadOn.Contains(kind) || Interlocked.Exchange(ref _pending, 1) == 1) return;
        _ = InvokeAsync(async () =>
        {
            await Task.Delay(150); // agrupa ráfagas de cambios
            Interlocked.Exchange(ref _pending, 0);
            try { await LoadAsync(); StateHasChanged(); }
            catch (Exception) { /* el circuito pudo cerrarse */ }
        });
    }

    /// <summary>Ejecuta una acción mostrando como toast los errores de reglas o permisos.</summary>
    protected async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (ReglaException ex) { Ui.Flash(ex.Message); await LoadAsync(); }
        catch (PermisoException ex) { Ui.Flash(ex.Message); }
    }

    protected async Task Download(string url)
    {
        var status = await JS.InvokeAsync<int>("grdp.download", url);
        Ui.Flash(status == 200 ? "Planilla descargada" : status == 204 ? "No hay datos para descargar con este filtro" : "No se pudo descargar la planilla");
    }

    public virtual void Dispose()
    {
        Changes.Changed -= OnChanged;
        GC.SuppressFinalize(this);
    }
}
