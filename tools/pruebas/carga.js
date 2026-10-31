// Generador de carga: N clientes concurrentes durante D segundos por escenario.
const B = (process.env.GRDP_URL || 'http://localhost:5288').replace(/\/$/, '');
const fs = require('fs');
const PID = process.env.GRDP_PID || ''; // opcional (Linux): PID del proceso de la app para medir memoria
const rss = () => { try { return Math.round(+fs.readFileSync(`/proc/${PID}/status`, 'utf8').match(/VmRSS:\s+(\d+)/)[1] / 1024); } catch { return 0; } };
function setc(jar, r) { for (const c of r.headers.getSetCookie?.() ?? []) { const kv = c.split(';')[0]; jar.m[kv.split('=')[0]] = kv; } jar.c = Object.values(jar.m).join('; '); }
async function token(jar, path) { const r = await fetch(B + path, { headers: { cookie: jar.c } }); setc(jar, r); return (await r.text()).match(/name="__RequestVerificationToken" value="([^"]+)"/)?.[1]; }
async function login(email, pw = 'demo123') {
  const jar = { m: {}, c: '' }; const t = await token(jar, '/cuenta/ingresar');
  const r = await fetch(B + '/cuenta/login', { method: 'POST', redirect: 'manual', headers: { cookie: jar.c, 'content-type': 'application/x-www-form-urlencoded' }, body: new URLSearchParams({ email, password: pw, __RequestVerificationToken: t }) });
  setc(jar, r); return jar;
}
async function run(name, conc, secs, fn) {
  const lat = []; let err = 0, n = 0; const codes = {}; const end = Date.now() + secs * 1000; const r0 = rss(); let rmax = r0;
  const mon = setInterval(() => { rmax = Math.max(rmax, rss()); }, 250);
  await Promise.all(Array.from({ length: conc }, async (_, w) => {
    while (Date.now() < end) {
      const s = performance.now();
      try { const st = await fn(w); codes[st] = (codes[st] || 0) + 1; if (st >= 500 || st === 0) err++; } catch { err++; }
      lat.push(performance.now() - s); n++;
    }
  }));
  clearInterval(mon); lat.sort((a, b) => a - b); const p = q => lat[Math.min(lat.length - 1, Math.floor(q * lat.length))].toFixed(0);
  console.log(`${name.padEnd(34)} c=${String(conc).padStart(3)} ${String(n).padStart(6)} req  ${(n / secs).toFixed(0).padStart(5)} req/s  p50 ${p(.5).padStart(5)} ms  p95 ${p(.95).padStart(5)}  p99 ${p(.99).padStart(5)}  errores ${err}  códigos ${JSON.stringify(codes)}  RSS ${r0}→${rmax} MB`);
}
(async () => {
  const lider = await login('cmendez@empresa.cl'); const an = await login('asalinas@empresa.cl');
  const get = (path, jar) => async () => { const r = await fetch(B + path, { redirect: 'manual', headers: jar ? { cookie: jar.c } : {} }); await r.arrayBuffer(); return r.status; };
  const S = +process.argv[2] || 20;
  for (const c of [10, 50, 100]) await run('GET pantalla de ingreso', c, S, get('/cuenta/ingresar'));
  for (const c of [10, 50, 100]) await run('GET consulta pública (búsqueda BD)', c, S, get('/consulta?job=26-0008'));
  for (const c of [10, 50]) await run('GET tablero líder (SSR + BD)', c, S, get('/', lider));
  for (const c of [10, 30]) await run('GET export planificación .xlsx', c, S, get('/export/planificacion?unidad=proc', lider));
  for (const c of [10, 30]) await run('GET export mi semana (analista)', c, S, get('/export/mi-semana', an));
  // Ingreso completo (GET + POST con hash PBKDF2): el más costoso en CPU.
  for (const c of [10, 30]) await run('Ingreso completo (PBKDF2)', c, S, async (w) => { const j = await login(['rsalgado', 'cgomez', 'aperez', 'lruiz', 'aduran'][w % 5] + '@empresa.cl'); return j.m['grdp.session'] ? 302 : 401; });
})().catch(e => { console.error('ERROR', e); process.exit(1); });
