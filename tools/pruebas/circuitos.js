const { chromium } = require('playwright');
const fs = require('fs');
const B = (process.env.GRDP_URL || 'http://localhost:5288').replace(/\/$/, '');
const PID = process.env.GRDP_PID || ''; // opcional (Linux): PID del proceso de la app para medir memoria
const rss = () => { try { return Math.round(+fs.readFileSync(`/proc/${PID}/status`, 'utf8').match(/VmRSS:\s+(\d+)/)[1] / 1024); } catch { return 0; } };
const N = +process.argv[2] || 50;
(async () => {
  // sesión del líder
  const r0 = await fetch(B + '/cuenta/ingresar'); const ck0 = r0.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
  const t = (await r0.text()).match(/name="__RequestVerificationToken" value="([^"]+)"/)[1];
  const r1 = await fetch(B + '/cuenta/login', { method: 'POST', redirect: 'manual', headers: { cookie: ck0, 'content-type': 'application/x-www-form-urlencoded' }, body: new URLSearchParams({ email: 'cmendez@empresa.cl', password: 'demo123', __RequestVerificationToken: t }) });
  const sess = r1.headers.getSetCookie().find(c => c.startsWith('grdp.session')).split(';')[0].split('=');
  const b = await chromium.launch(); const errs = [];
  const base = rss(); console.log(`RSS inicial ${base} MB · abriendo ${N} circuitos…`);
  const t0 = performance.now(); const pages = [];
  for (let k = 0; k < N; k += 10) {
    pages.push(...await Promise.all(Array.from({ length: Math.min(10, N - k) }, async () => {
      const ctx = await b.newContext({ viewport: { width: 1280, height: 800 } });
      await ctx.addCookies([{ name: sess[0], value: sess[1], domain: 'localhost', path: '/' }]);
      const p = await ctx.newPage(); p.on('pageerror', e => errs.push(e.message));
      await p.goto(B + '/planificacion'); await p.locator('table select[aria-label=Analista]').first().waitFor({ timeout: 60000 });
      await p.waitForFunction(() => window.Blazor && document.querySelector('.components-reconnect-show') == null, null, { timeout: 60000 });
      return p;
    })));
  }
  console.log(`${pages.length} circuitos activos en ${((performance.now() - t0) / 1000).toFixed(1)} s · RSS ${rss()} MB (+${rss() - base})`);
  // Interacción simultánea: todas cambian a la semana siguiente y vuelven
  const t1 = performance.now();
  await Promise.all(pages.map(async p => { await p.locator('.seg-opt', { hasText: /siguiente/i }).first().click(); await p.waitForTimeout(50); await p.locator('.seg-opt', { hasText: /actual/i }).first().click(); }));
  // esperar a que cada pestaña muestre de verdad la semana actual (ninguna fila de noviembre)
  await Promise.all(pages.map(p => p.waitForFunction(() => { const rows = [...document.querySelectorAll('table tbody tr')]; return rows.length > 0 && !rows.some(tr => /nov/.test(tr.querySelector('td')?.textContent || '')); }, null, { timeout: 60000 })));
  console.log(`${N} cambios de semana simultáneos (ida y vuelta) en ${((performance.now() - t1) / 1000).toFixed(2)} s · RSS ${rss()} MB`);
  // Tiempo real: una pestaña asigna y todas las demás deben reflejarlo
  // identifica la fila por su texto (la tabla se reordena al asignar)
  const row0 = pages[0].locator('table tbody tr').first(); const firma = (await row0.locator('td').evaluateAll(td => td.slice(0, 7).map(x => x.textContent.trim()).join('|')));
  const sel = row0.locator('select[aria-label=Analista]');
  const antes = await sel.inputValue(); const opts = await sel.locator('option').evaluateAll(o => o.map(x => x.value).filter(v => v));
  const nuevo = opts.find(v => v !== antes);
  const valor = (p, v) => p.waitForFunction(([f, v]) => [...document.querySelectorAll('table tbody tr')].some(tr =>
      [...tr.querySelectorAll('td')].slice(0, 7).map(x => x.textContent.trim()).join('|') === f && tr.querySelector('select[aria-label=Analista]')?.value === v), [firma, v], { timeout: 30000 });
  const t2 = performance.now(); await sel.selectOption(nuevo);
  const rs = await Promise.allSettled(pages.slice(1).map(p => valor(p, nuevo)));
  const malas = rs.map((r, i) => r.status === 'rejected' ? i + 1 : -1).filter(i => i >= 0);
  if (malas.length) {
    console.log(`  ${malas.length} pestañas sin actualizar. Ejemplo:`);
    const p = pages[malas[0]];
    const d = await p.evaluate(f => ({ semana: document.querySelector('.seg-opt[aria-checked=true], .seg-opt.on, .seg-opt[aria-pressed=true]')?.textContent?.trim(),
      reconectando: !!document.querySelector('.components-reconnect-show, #components-reconnect-modal[open]'),
      fila: [...document.querySelectorAll('table tbody tr')].map(tr => [...tr.querySelectorAll('td')].slice(0, 7).map(x => x.textContent.trim()).join('|')).filter(x => x === f).length,
      valor: [...document.querySelectorAll('table tbody tr')].find(tr => [...tr.querySelectorAll('td')].slice(0, 7).map(x => x.textContent.trim()).join('|') === f)?.querySelector('select')?.value,
      filas: document.querySelectorAll('table tbody tr').length, h: document.querySelector('.page-head h2')?.textContent }), firma);
    console.log('  ', JSON.stringify(d), 'esperado', nuevo, 'firma', firma.slice(0, 80));
    const d0 = await pages[0].evaluate(() => ({ filas: document.querySelectorAll('table tbody tr').length }));
    console.log('   pestaña 0:', JSON.stringify(d0));
  }
  console.log(`Asignación propagada en tiempo real a ${N - 1} pestañas en ${((performance.now() - t2) / 1000).toFixed(2)} s`);
  const fila = pages[0].locator('table tbody tr').filter({ has: pages[0].locator(`select[aria-label=Analista] >> nth=0`) });
  await pages[0].locator('table tbody tr').evaluateAll(() => 0);
  const p0 = pages[0]; const idx = await p0.locator('table tbody tr').evaluateAll((trs, f) => trs.findIndex(tr => [...tr.querySelectorAll('td')].slice(0, 7).map(x => x.textContent.trim()).join('|') === f), firma);
  await p0.locator('table tbody tr').nth(idx).locator('select[aria-label=Analista]').selectOption(antes); // deshacer
  try { await valor(pages[1], antes); } catch { console.log("  (deshacer: la pestaña 1 no vio la vuelta atrás en 30 s)"); }
  console.log(`errores de página: ${errs.length ? [...new Set(errs)].slice(0, 3) : 'ninguno'}`);
  await b.close();
  for (const s of [3, 15]) { await new Promise(r => setTimeout(r, s * 1000)); console.log(`tras cerrar las pestañas (+${s}s): RSS ${rss()} MB`); }
})().catch(e => { console.error('ERROR', e.message); process.exit(1); });
