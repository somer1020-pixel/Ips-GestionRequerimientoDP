using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Domain;

namespace GestionRequerimientoDP.Web.Ui;

/// <summary>Estado de interfaz de un circuito (una pestaña del navegador): diálogos, toast y filtros compartidos entre vistas.</summary>
public sealed class UiState
{
    public event Action? Changed;
    void Notify() => Changed?.Invoke();

    // Diálogos globales
    public int? DetailId { get; private set; }
    public int? OkId { get; private set; }
    public Equipo? NewTipo { get; private set; }
    public void OpenDetail(int id) { DetailId = id; Notify(); }
    public void CloseDetail() { DetailId = null; Notify(); }
    public void AskOk(int id) { OkId = id; Notify(); }
    public void CloseOk() { OkId = null; Notify(); }
    public void OpenNew(Equipo tipo) { NewTipo = tipo; Notify(); }
    public void CloseNew() { NewTipo = null; Notify(); }

    // Toast
    public string Toast { get; private set; } = "";
    CancellationTokenSource? _toastCts;
    public void Flash(string msg)
    {
        Toast = msg;
        Notify();
        _toastCts?.Cancel();
        var cts = _toastCts = new CancellationTokenSource();
        _ = Task.Delay(2800, cts.Token).ContinueWith(t => { if (!t.IsCanceled) { Toast = ""; Notify(); } }, TaskScheduler.Default);
    }

    // Filtro de analista compartido (Tablero, Planificación, Histórico, Motivos de atraso)
    public int? AnFilter { get; set; }

    // Periodo compartido entre Histórico y Motivos de atraso
    public Periodo? Periodo { get; set; }

    // Planificación (semana y unidad visibles; el botón "Nuevo requerimiento" las usa)
    public Equipo PlanUnit { get; set; } = Equipo.Procesamiento;
    public DateOnly? PlanWeek { get; set; }

    /// <summary>Vista actual, para el botón "Nuevo requerimiento" del encabezado.</summary>
    public string CurrentTab { get; set; } = "";
}
