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

// Menciones en comentarios: en un textarea con data-mencion (JSON [{n: nombre, d: detalle}]), escribir "@" y parte del
// nombre abre la lista de personas; flechas + Enter/Tab o clic la eligen e insertan "@Nombre Apellido ". Escape la cierra.
(function () {
  const sinTildes = s => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
  let menu = null, estado = null; // estado: { ta, ini, opciones, sel }
  function cerrar() { if (menu) menu.remove(); menu = null; estado = null; }
  function consulta(ta) {
    const pos = ta.selectionStart, antes = ta.value.slice(0, pos);
    const m = /(^|[\s(])@([^\s@]{0,30})$/.exec(antes);
    return m ? { ini: pos - m[2].length - 1, q: sinTildes(m[2]) } : null;
  }
  function personas(ta) { try { return JSON.parse(ta.dataset.mencion || '[]'); } catch (e) { return []; } }
  function pintar() {
    if (!estado) return;
    if (!menu) { menu = document.createElement('div'); menu.className = 'mencion-menu'; menu.setAttribute('role', 'listbox'); document.body.appendChild(menu); }
    menu.innerHTML = '';
    estado.opciones.forEach((p, i) => {
      const o = document.createElement('div');
      o.className = 'mencion-opt' + (i === estado.sel ? ' activa' : ''); o.setAttribute('role', 'option');
      const b = document.createElement('strong'); b.textContent = p.n; const s = document.createElement('span'); s.textContent = p.d || '';
      o.append(b, s);
      o.addEventListener('mousedown', ev => { ev.preventDefault(); elegir(i); });
      menu.appendChild(o);
    });
    const r = estado.ta.getBoundingClientRect();
    const abajo = window.innerHeight - r.bottom > 220;
    menu.style.left = Math.max(8, r.left) + 'px';
    menu.style.width = Math.min(360, r.width) + 'px';
    if (abajo) { menu.style.top = (r.bottom + 4) + 'px'; menu.style.bottom = ''; }
    else { menu.style.bottom = (window.innerHeight - r.top + 4) + 'px'; menu.style.top = ''; }
  }
  function elegir(i) {
    const { ta, ini, opciones } = estado || {};
    if (!ta || !opciones[i]) return cerrar();
    const fin = ta.selectionStart, txt = '@' + opciones[i].n + ' ';
    ta.value = ta.value.slice(0, ini) + txt + ta.value.slice(fin);
    const c = ini + txt.length; ta.setSelectionRange(c, c); ta.focus();
    cerrar();
  }
  function actualizar(ta) {
    const c = consulta(ta);
    if (!c) return cerrar();
    const op = personas(ta).filter(p => { const n = sinTildes(p.n); return !c.q || n.startsWith(c.q) || n.split(/\s+/).some(w => w.startsWith(c.q)); }).slice(0, 8);
    if (!op.length) return cerrar();
    estado = { ta, ini: c.ini, opciones: op, sel: 0 };
    pintar();
  }
  document.addEventListener('input', e => { const ta = e.target; if (ta && ta.matches && ta.matches('textarea[data-mencion]')) actualizar(ta); }, true);
  document.addEventListener('keydown', e => {
    if (!estado || e.target !== estado.ta) return;
    const n = estado.opciones.length;
    if (e.key === 'ArrowDown') { estado.sel = (estado.sel + 1) % n; pintar(); e.preventDefault(); }
    else if (e.key === 'ArrowUp') { estado.sel = (estado.sel + n - 1) % n; pintar(); e.preventDefault(); }
    else if (e.key === 'Enter' || e.key === 'Tab') { elegir(estado.sel); e.preventDefault(); e.stopPropagation(); }
    else if (e.key === 'Escape') { cerrar(); e.preventDefault(); e.stopPropagation(); }
  }, true);
  document.addEventListener('focusout', e => { if (estado && e.target === estado.ta) setTimeout(cerrar, 120); }, true);
  window.addEventListener('scroll', () => { if (estado) pintar(); }, true);
  window.addEventListener('resize', cerrar);
  window.grdp.mencion = {
    valor(id) { const el = document.getElementById(id); return el ? el.value : ''; },
    limpiar(id) { const el = document.getElementById(id); if (el) el.value = ''; cerrar(); },
  };
})();
