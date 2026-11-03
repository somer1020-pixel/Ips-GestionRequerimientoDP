// Texto tipeado en los buscadores (data-preservar) mientras la página aún no es interactiva: Blazor reemplaza el cuadro al
// conectar y se perdería. Se escucha en el documento, así que también cubre el cuadro nuevo hasta que el componente lo recupera.
const textoPrevio = {};
document.addEventListener('input', e => { const id = e.target && e.target.dataset ? e.target.dataset.preservar : null; if (id) textoPrevio[id] = e.target.value; }, true);

// Portapapeles: navigator.clipboard solo existe en sitios seguros (https o localhost); en http://servidor no está.
// Los botones con data-copiar copian en el mismo clic (el navegador exige un gesto del usuario) con la API moderna si
// existe o, si no, seleccionando un texto oculto (execCommand). Si ambos fallan, se muestra el texto para copiarlo a mano.
function copiarConSeleccion(text) {
  const ta = document.createElement('textarea');
  ta.value = text; ta.setAttribute('readonly', ''); ta.style.cssText = 'position:fixed;top:0;left:0;opacity:0;pointer-events:none';
  const activo = document.activeElement;
  document.body.appendChild(ta); ta.focus(); ta.select(); ta.setSelectionRange(0, text.length);
  let ok = false;
  try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
  ta.remove(); if (activo && activo.focus) activo.focus();
  return ok;
}
let ultimaCopia = null;
document.addEventListener('click', e => {
  const el = e.target && e.target.closest ? e.target.closest('[data-copiar]') : null;
  if (!el) return;
  const text = el.getAttribute('data-copiar') || '';
  let ok = false;
  if (window.isSecureContext && navigator.clipboard) { navigator.clipboard.writeText(text).catch(() => { }); ok = true; }
  else ok = copiarConSeleccion(text);
  if (!ok) window.prompt('Copia con Ctrl+C y presiona Enter:', text);
  ultimaCopia = ok;
}, true);

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
    if (window.isSecureContext && navigator.clipboard) { try { await navigator.clipboard.writeText(text); return true; } catch (e) { } }
    return copiarConSeleccion(text);
  },
  // Borradores de formularios en este navegador (sobreviven a cortes de conexión y recargas).
  borrador: {
    guardar(k, v) { try { localStorage.setItem(k, v); } catch (e) { } },
    leer(k) { try { return localStorage.getItem(k); } catch (e) { return null; } },
    borrar(k) { try { localStorage.removeItem(k); } catch (e) { } },
  },
  // Resultado del último botón data-copiar (la copia ya se hizo en el clic); null si no hubo.
  copiado() { const r = ultimaCopia; ultimaCopia = null; return r; },
  confirm(msg) { return window.confirm(msg); },
  // Devuelve (y vuelve a poner en el cuadro, con foco) lo escrito en un buscador antes de que la página fuera interactiva.
  recuperarTexto(id) {
    const v = textoPrevio[id];
    delete textoPrevio[id];
    if (v == null) return null;
    const el = document.querySelector(`[data-preservar="${CSS.escape(id)}"]`);
    if (el) {
      if (el.value !== v) el.value = v;
      if (document.activeElement === document.body || !document.activeElement) { el.focus(); el.setSelectionRange(v.length, v.length); }
    }
    return v;
  },
  origin() { return location.origin; }
};

// Firefox exige datos en el dragstart para iniciar el arrastre; Blazor maneja el resto (@ondragstart / @ondrop).
document.addEventListener('dragstart', e => {
  const el = e.target && e.target.closest ? e.target.closest('[data-req-id]') : null;
  if (el && e.dataTransfer) { e.dataTransfer.setData('text/plain', el.dataset.reqId); e.dataTransfer.effectAllowed = 'move'; }
});

// SSO: intento automático con la cuenta corporativa desde la red interna (pantalla de ingreso).
// En ventanas InPrivate / incógnito el navegador no envía la cuenta Windows: se muestra el formulario directamente.
document.addEventListener('DOMContentLoaded', async () => {
  const el = document.getElementById('sso-auto');
  if (!el) return;
  const form = document.getElementById('login-form');
  const show = () => { el.style.display = 'none'; if (form) form.classList.remove('d-none'); };
  try {
    if (navigator.storage && navigator.storage.estimate) {
      const est = await navigator.storage.estimate();
      if (est && est.quota && est.quota < 4000000000) { show(); return; }
    }
  } catch (e) { }
  setTimeout(show, 3500);
  window.location.replace(el.dataset.ssoUrl);
});
