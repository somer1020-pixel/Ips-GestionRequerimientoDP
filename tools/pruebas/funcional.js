const { chromium } = require('playwright');
const B = (process.env.GRDP_URL || 'http://localhost:5288').replace(/\/$/, '');
const res = []; const ok = (c, m) => { res.push(`${c ? 'OK   ' : 'FALLA'} ${m}`); };
const stamp = Date.now() % 100000;
async function sesion(b, email) {
  const ctx = await b.newContext({ viewport: { width: 1440, height: 900 }, acceptDownloads: true }); const p = await ctx.newPage();
  const errs = []; p.on('pageerror', e => errs.push(e.message)); p.on('console', m => m.type() === 'error' && errs.push(m.text()));
  p.on('response', r => { if (r.status() >= 500) errs.push(`HTTP ${r.status()} ${r.url()}`); });
  p.on('dialog', d => d.accept());
  await p.goto(B + '/cuenta/ingresar?manual=1'); await p.fill('input[name=email]', email); await p.fill('input[name=password]', 'demo123');
  await p.getByRole('button', { name: 'Ingresar', exact: true }).click(); await p.waitForURL(u => !/cuenta\/ingresar/.test(u)); await p.waitForLoadState('networkidle');
  return { p, errs };
}
async function visitar(s, rol, rutas) {
  for (const r of rutas) {
    await s.p.goto(B + r); await s.p.waitForLoadState('networkidle'); await s.p.waitForTimeout(300);
    const errUi = await s.p.locator('#blazor-error-ui').isVisible(); const url = new URL(s.p.url()).pathname;
    ok(!errUi && url === r && !(await s.p.locator('h1:has-text("Ocurrió un error")').count()), `${rol} abre ${r}${url !== r ? ` (redirigió a ${url})` : ''}`);
  }
}
async function prohibido(s, rol, rutas) {
  for (const r of rutas) { await s.p.goto(B + r); await s.p.waitForLoadState('networkidle'); const url = new URL(s.p.url()).pathname; ok(url !== r, `${rol} no accede a ${r} (→ ${url})`); }
}
(async () => {
  const b = await chromium.launch();
  // ---------- Líder ----------
  const L = await sesion(b, 'cmendez@empresa.cl');
  await visitar(L, 'líder', ['/', '/solicitudes', '/planificacion', '/scripts', '/resumen', '/historico', '/atrasos', '/notificaciones', '/usuarios', '/usuarios/auditoria', '/consulta']);
  // nuevo requerimiento asignado a sí misma
  await L.p.goto(B + '/planificacion'); await L.p.waitForLoadState('networkidle');
  await L.p.getByRole('button', { name: 'Nuevo requerimiento' }).click();
  await L.p.fill('#nr-cli', 'Cliente E2E'); await L.p.fill('#nr-job', `26-E2E-${stamp}`); await L.p.fill('#nr-pro', 'Proyecto funcional');
  await L.p.selectOption('#nr-tec', { index: 1 }); await L.p.selectOption('#nr-an', { label: 'Carolina Méndez (líder)' });
  await L.p.getByRole('button', { name: 'Agregar a la planificación' }).click();
  await L.p.locator('.toast-box').waitFor(); ok(/agregado/i.test(await L.p.locator('.toast-box').textContent()), `líder crea requerimiento asignado a sí misma: "${await L.p.locator('.toast-box').textContent()}"`);
  await L.p.waitForTimeout(800);
  const mias = L.p.locator('aside a.nav-i', { hasText: 'Mis asignaciones' }); ok(await mias.count() === 1, 'aparece la pestaña "Mis asignaciones"');
  await mias.click(); await L.p.waitForURL(/mi-semana/); await L.p.locator(`tr:has-text("26-E2E-${stamp}")`).waitFor();
  await L.p.locator(`tr:has-text("26-E2E-${stamp}")`).getByRole('button', { name: 'Iniciar' }).click(); await L.p.waitForTimeout(800);
  ok(await L.p.locator(`tr:has-text("26-E2E-${stamp}")`).getByText('En curso').count() > 0, 'líder inicia su propio requerimiento');
  await L.p.locator(`tr:has-text("26-E2E-${stamp}")`).getByRole('button', { name: 'Dar OK' }).click();
  await L.p.fill('#ok-horas', '2'); await L.p.getByRole('button', { name: 'Confirmar OK' }).click(); await L.p.waitForTimeout(1000);
  ok(await L.p.locator(`tr:has-text("26-E2E-${stamp}")`).locator('text=OK').count() > 0, 'líder da OK con horas');
  // descargas
  for (const [ruta, boton] of [['/planificacion', 'Descargar planificación'], ['/historico', 'Descargar periodo']]) {
    await L.p.goto(B + ruta); await L.p.waitForLoadState('networkidle');
    const dl = L.p.waitForEvent('download', { timeout: 15000 }); await L.p.getByRole('button', { name: boton }).click();
    ok(/\.xlsx$/.test((await dl).suggestedFilename()), `descarga ${boton}`);
  }
  // scripts: autoasignación
  await L.p.goto(B + '/scripts'); await L.p.waitForLoadState('networkidle');
  const auto = L.p.getByRole('button', { name: 'Asignar a primer disponible' });
  if (await auto.count()) { await auto.click(); await L.p.locator('.toast-box').waitFor(); ok(true, `scripts: ${await L.p.locator('.toast-box').textContent()}`); } else ok(true, 'scripts: sin cola por asignar');
  // usuarios: crear y eliminar
  await L.p.goto(B + '/usuarios'); await L.p.waitForLoadState('networkidle');
  await L.p.getByRole('button', { name: 'Nuevo usuario' }).click(); await L.p.fill('#u-nom', `Funcional ${stamp}`); await L.p.fill('#u-mail', `func${stamp}@empresa.cl`);
  await L.p.getByRole('button', { name: 'Guardar' }).click(); const fila = L.p.locator('tr', { hasText: `func${stamp}@empresa.cl` }); await fila.waitFor();
  ok(true, 'líder crea usuario'); await fila.getByRole('button', { name: 'Eliminar' }).click(); await L.p.waitForTimeout(800);
  ok(await L.p.locator('tr', { hasText: `func${stamp}@empresa.cl` }).count() === 0, 'líder elimina usuario sin historial');
  // ---------- Analista DP ----------
  const A = await sesion(b, 'asalinas@empresa.cl');
  await visitar(A, 'analista', ['/', '/mi-semana', '/consulta']);
  await prohibido(A, 'analista', ['/planificacion', '/usuarios', '/historico', '/solicitudes']);
  // ---------- Analista scripting / mixto / codificación ----------
  for (const [email, rol] of [['jperez@empresa.cl', 'analista scripting'], ['cgomez@empresa.cl', 'analista DP+script'], ['molivares@empresa.cl', 'analista codificación']]) {
    const s = await sesion(b, email); await visitar(s, rol, ['/', '/mi-semana']); ok(s.errs.length === 0, `${rol} sin errores (${s.errs.slice(0, 2)})`);
  }
  // ---------- Comercial ----------
  const C = await sesion(b, 'mayala@empresa.cl');
  await visitar(C, 'comercial', ['/solicitar', '/mis-solicitudes', '/consulta']);
  await prohibido(C, 'comercial', ['/', '/planificacion', '/mi-semana', '/usuarios']);
  await C.p.goto(B + '/solicitar'); await C.p.waitForLoadState('networkidle');
  await C.p.fill('#s-cli', 'Cliente Comercial E2E'); await C.p.fill('#s-job', `26-SOL-${stamp}`); await C.p.fill('#s-pro', 'Estudio comercial E2E');
  await C.p.selectOption('#s-sl', { index: 1 }); await C.p.selectOption('#s-tec', { index: 1 });
  const ent = C.p.locator('.seg-opt, label', { hasText: /Base de datos|Tablas|Cuestionario/ }).first(); if (await ent.count()) await ent.click();
  await C.p.getByRole('button', { name: 'Enviar solicitud' }).click(); await C.p.waitForTimeout(1500);
  const enviado = /mis-solicitudes/.test(C.p.url()) && await C.p.locator(`text=26-SOL-${stamp}`).count() > 0;
  ok(enviado, `comercial envía solicitud ${enviado ? '' : `(url ${C.p.url()}, toast ${await C.p.locator('.toast-box').textContent().catch(() => '-')}, error ${await C.p.locator('[role=alert]').allTextContents()})`}`);
  // el líder la ve en Solicitudes nuevas
  await L.p.goto(B + '/solicitudes'); await L.p.waitForLoadState('networkidle');
  ok(!enviado || await L.p.locator(`text=26-SOL-${stamp}`).count() > 0, 'la solicitud llega a "Solicitudes nuevas" del líder');
  // ---------- Consulta pública ----------
  const P = await (await b.newContext()).newPage(); await P.goto(B + '/consulta?job=26-E2E-' + stamp); await P.waitForLoadState('networkidle');
  ok(await P.locator(`text=26-E2E-${stamp}`).count() > 0, 'consulta pública encuentra el requerimiento sin sesión');
  ok(L.errs.length === 0 && A.errs.length === 0 && C.errs.length === 0, `sin errores de página/consola/5xx (${[...L.errs, ...A.errs, ...C.errs].slice(0, 3)})`);
  await b.close();
  console.log(res.join('\n')); console.log(`\n${res.filter(r => r.startsWith('OK')).length}/${res.length} verificaciones OK`);
})().catch(e => { console.error('ERROR', e.message); console.log(res.join('\n')); process.exit(1); });
