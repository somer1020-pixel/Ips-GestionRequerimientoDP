const { chromium } = require('playwright');
const B = (process.env.GRDP_URL || 'http://localhost:5288').replace(/\/$/, '');
const out = []; const ok = (c, m) => out.push(`${c ? 'OK  ' : 'FALLA'} ${m}`);
async function token(jar, path) {
  const r = await fetch(B + path, { headers: { cookie: jar.c } }); setc(jar, r);
  return (await r.text()).match(/name="__RequestVerificationToken" value="([^"]+)"/)?.[1];
}
function setc(jar, r) { for (const c of r.headers.getSetCookie?.() ?? []) { const kv = c.split(';')[0]; const k = kv.split('=')[0]; jar.m[k] = kv; } jar.c = Object.values(jar.m).join('; '); }
async function login(email, pw = 'demo123', ret = '/') {
  const jar = { m: {}, c: '' }; const t = await token(jar, '/cuenta/ingresar');
  const r = await fetch(B + '/cuenta/login', { method: 'POST', redirect: 'manual', headers: { cookie: jar.c, 'content-type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({ email, password: pw, returnUrl: ret, __RequestVerificationToken: t }) });
  setc(jar, r); return { jar, loc: r.headers.get('location'), status: r.status, setCookie: r.headers.getSetCookie?.() ?? [] };
}
(async () => {
  // 1. Autorización por rol en descargas
  const anon = await fetch(B + '/export/historico', { redirect: 'manual' });
  ok(anon.status === 401, `anónimo /export/historico → ${anon.status}`);
  const an = await login('asalinas@empresa.cl'), com = await login('mayala@empresa.cl'), lid = await login('cmendez@empresa.cl');
  for (const [u, who, path, exp] of [[an, 'analista', '/export/historico', 403], [an, 'analista', '/export/solicitudes', 403], [com, 'comercial', '/export/mi-semana', 403],
    [com, 'comercial', '/export/planificacion', 403], [an, 'analista', '/export/mi-semana', 200], [lid, 'líder', '/export/historico', 200]]) {
    const r = await fetch(B + path, { redirect: 'manual', headers: { cookie: u.jar.c } }); ok(r.status === exp, `${who} ${path} → ${r.status} (esperado ${exp})`);
  }
  // 2. Cookie de sesión
  const sc = lid.setCookie.find(c => c.startsWith('grdp.session')) || '';
  ok(/httponly/i.test(sc), `cookie de sesión HttpOnly`); ok(/samesite=lax|samesite=strict/i.test(sc), `cookie de sesión SameSite (${(sc.match(/samesite=\w+/i) || ['-'])[0]})`);
  // 3. Redirección abierta
  for (const ret of ['//evil.example', 'https://evil.example/x', '/\\evil.example', 'javascript:alert(1)']) {
    const r = await login('cmendez@empresa.cl', 'demo123', ret); ok(r.loc && r.loc.startsWith('/') && !r.loc.startsWith('//') && !r.loc.startsWith('/\\'), `returnUrl ${ret} → ${r.loc}`);
  }
  // 4. CSRF: POST sin token
  const nocsrf = await fetch(B + '/cuenta/login', { method: 'POST', redirect: 'manual', headers: { 'content-type': 'application/x-www-form-urlencoded' }, body: 'email=a&password=b' });
  ok(nocsrf.status === 400, `login sin token antiforgery → ${nocsrf.status}`);
  // 5. Fuga de detalles de error
  const bad = await fetch(B + '/export/historico?analista=abc', { headers: { cookie: lid.jar.c } }); const bt = await bad.text();
  ok(!/Exception|StackTrace|at Gestion/.test(bt), `error de parámetro sin traza (${bad.status})`);
  // 6. Cabeceras
  const h = (await fetch(B + '/cuenta/ingresar')).headers;
  for (const k of ['x-content-type-options', 'x-frame-options', 'referrer-policy', 'content-security-policy']) ok(!!h.get(k), `cabecera ${k}: ${h.get(k) ?? 'ausente'}`);
  ok(!h.get('server'), `cabecera Server: ${h.get('server') ?? 'ausente'}`);
  // 7. XSS: datos maliciosos en registro y consulta
  const xjar = { m: {}, c: '' }; const xt = await token(xjar, '/cuenta/registro'); const stamp = Date.now() % 100000;
  const xr = await fetch(B + '/cuenta/solicitar-acceso', { method: 'POST', redirect: 'manual', headers: { cookie: xjar.c, 'content-type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({ nombre: `Xss <img src=x onerror=window.__xss=1> ${stamp}`, email: `xss${stamp}@empresa.cl`, rut: '', motivo: '"><script>window.__xss=2</script>', password: 'Planilla#2026', confirmacion: 'Planilla#2026', __RequestVerificationToken: xt }) });
  ok(xr.status === 302, `registro con HTML malicioso aceptado como texto (${xr.status})`);
  const cq = await (await fetch(B + '/consulta?job=' + encodeURIComponent('<script>alert(1)</script>'))).text();
  ok(!cq.includes('<script>alert(1)</script>'), `consulta refleja el término codificado`);
  const b = await chromium.launch(); const ctx = await b.newContext(); const [k, v] = sc.split(';')[0].split('=');
  await ctx.addCookies([{ name: k, value: v.replace(/^"|"$/g, ''), domain: 'localhost', path: '/' }]);
  const p = await ctx.newPage(); let dialog = false; p.on('dialog', d => { dialog = true; d.dismiss(); });
  await p.goto(B + '/usuarios'); await p.waitForLoadState('networkidle'); await p.locator('text=Solicitudes de acceso').waitFor();
  const xss = await p.evaluate(() => window.__xss); ok(!xss && !dialog, `Usuarios muestra el nombre/motivo malicioso sin ejecutarlo (window.__xss=${xss})`);
  await p.goto(B + '/notificaciones'); await p.waitForLoadState('networkidle');
  ok(!(await p.evaluate(() => window.__xss)), `Notificaciones sin ejecución de script`);
  await p.goto(B + '/usuarios/auditoria'); await p.waitForLoadState('networkidle');
  ok(!(await p.evaluate(() => window.__xss)), `Auditoría sin ejecución de script`);
  // limpieza: rechazar la solicitud XSS
  await p.goto(B + '/usuarios'); p.on('dialog', d => d.accept());
  const fila = p.locator('tr', { hasText: `xss${stamp}@empresa.cl` }); await fila.getByRole('button', { name: 'Rechazar' }).click(); await p.waitForTimeout(800);
  await b.close();
  // 8. Enumeración de cuentas por tiempo de respuesta
  async function t(email) { const s = performance.now(); await login(email, 'Incorrecta#1'); return performance.now() - s; }
  let ex = 0, nx = 0; for (let i = 0; i < 6; i++) { ex += await t('rsalgado@empresa.cl'); nx += await t(`noexiste${i}@empresa.cl`); }
  ok(Math.abs(ex - nx) / 6 < 40, `tiempo login cuenta existente ${(ex / 6).toFixed(0)} ms vs inexistente ${(nx / 6).toFixed(0)} ms`);
  console.log(out.join('\n'));
})().catch(e => { console.error('ERROR', e); process.exit(1); });
