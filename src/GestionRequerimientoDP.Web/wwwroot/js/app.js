// Utilidades de GestionRequerimientoDP para Blazor (descargas, portapapeles, confirmaciones y drag & drop).
window.grdp = {
  // Descarga un archivo con la sesión actual. Devuelve el código HTTP (200 = descargado, 204 = sin datos).
  async download(url) {
    try {
      const r = await fetch(url, { credentials: 'same-origin' });
      if (r.status !== 200) return r.status;
      const blob = await r.blob();
      const cd = r.headers.get('content-disposition') || '';
      const m = /filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i.exec(cd);
      const name = m ? decodeURIComponent(m[1] || m[2]) : 'descarga.xlsx';
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = name;
      document.body.appendChild(a);
      a.click();
      setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
      return 200;
    } catch (e) { return 0; }
  },
  async copy(text) {
    try { await navigator.clipboard.writeText(text); return true; } catch (e) { return false; }
  },
  confirm(msg) { return window.confirm(msg); },
  origin() { return location.origin; }
};

// Firefox exige datos en el dragstart para iniciar el arrastre; Blazor maneja el resto (@ondragstart / @ondrop).
document.addEventListener('dragstart', e => {
  const el = e.target && e.target.closest ? e.target.closest('[data-req-id]') : null;
  if (el && e.dataTransfer) { e.dataTransfer.setData('text/plain', el.dataset.reqId); e.dataTransfer.effectAllowed = 'move'; }
});
