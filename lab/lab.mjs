// Orquestador del experimento: publicacion tradicional vs. blue-green + autoescalado.
// Uso: node lab.mjs <escenario>   (deploy | rollback | escala | todo)
import { spawn, execSync } from 'node:child_process';
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';

const ROOT = path.resolve(import.meta.dirname, '..');
const PUB = path.join(ROOT, 'BlazorPub', 'publicaciones');
const SITES = path.join(ROOT, 'sitios');
const OUT = path.join(ROOT, 'resultados');
fs.mkdirSync(OUT, { recursive: true });

const sleep = ms => new Promise(r => setTimeout(r, ms));
const now = () => performance.now();
const agent = new http.Agent({ keepAlive: true, maxSockets: 1024 });
const MASKS = [0x3, 0xC, 0x30, 0xC0];   // 2 procesadores logicos por instancia

function setAffinity(pid, mask) {
  execSync(`powershell -NoProfile -c "(Get-Process -Id ${pid}).ProcessorAffinity=${mask}"`);
}
setAffinity(process.pid, 0xFF000);

function get(port, p, timeout = 3000) {
  return new Promise(res => {
    const t0 = now();
    const req = http.get({ host: '127.0.0.1', port, path: p, agent, timeout }, r => {
      let b = ''; r.on('data', d => b += d);
      r.on('end', () => res({ ok: r.statusCode === 200, status: r.statusCode, lat: now() - t0, body: b }));
    });
    req.on('timeout', () => req.destroy(new Error('timeout')));
    req.on('error', e => res({ ok: false, status: 0, lat: now() - t0, err: e.code || e.message }));
  });
}
function post(port, p, data) {
  return new Promise(res => {
    const req = http.request({ host: '127.0.0.1', port, path: p, method: 'POST' }, r => { r.resume(); r.on('end', res); });
    req.end(JSON.stringify(data));
  });
}

// ---------- gestion de instancias ----------
function startInstance(name, dir, port, mask) {
  const child = spawn(path.join(dir, 'BlazorPub.exe'), [], {
    cwd: dir, stdio: 'ignore',
    env: { ...process.env, ASPNETCORE_URLS: `http://127.0.0.1:${port}`, ASPNETCORE_ENVIRONMENT: 'Production', INSTANCIA: name }
  });
  try { setAffinity(child.pid, mask); } catch { }
  return { name, port, child, host: '127.0.0.1' };
}
async function stopInstance(inst) {
  if (!inst || inst.child.exitCode !== null) return;
  const done = new Promise(r => inst.child.once('exit', r));
  inst.child.kill();
  await done;
}
function copyRelease(version, dir) {
  fs.rmSync(dir, { recursive: true, force: true, maxRetries: 20, retryDelay: 50 });
  fs.cpSync(path.join(PUB, version), dir, { recursive: true });
}
async function waitHealthy(port, timeoutMs = 30000) {
  const t0 = now();
  while (now() - t0 < timeoutMs) {
    const r = await get(port, '/healthz', 1000);
    if (r.ok) return now() - t0;
    await sleep(25);
  }
  throw new Error('instancia no saludable ' + port);
}

// ---------- generador de carga ----------
function startLoad(port, concurrency, iter = 2000) {
  const log = []; let stop = false; let conc = concurrency; const workers = [];
  const loop = async (w) => {
    let k = 1;
    while (!stop) {
      if (w >= conc) { await sleep(20); continue; }
      const t = now();
      const r = await get(port, `/api/pedidos/${(k++ % 50) + 1}?iter=${iter}`);
      let version = null; try { version = JSON.parse(r.body).version; } catch { }
      log.push({ t, end: now(), ok: r.ok, status: r.status, lat: r.lat, version });
    }
  };
  const ctl = {
    log,
    setConcurrency(c) { while (workers.length < c) workers.push(loop(workers.length)); conc = c; },
    async stop() { stop = true; await Promise.all(workers); return log; }
  };
  ctl.setConcurrency(concurrency);
  return ctl;
}

const pct = (arr, p) => { const a = [...arr].sort((x, y) => x - y); return a.length ? a[Math.min(a.length - 1, Math.floor(a.length * p))] : 0; };
const mean = a => a.reduce((s, x) => s + x, 0) / (a.length || 1);
const sd = a => { const m = mean(a); return Math.sqrt(a.reduce((s, x) => s + (x - m) ** 2, 0) / Math.max(1, a.length - 1)); };

// Ventana de indisponibilidad: desde el inicio de la primera solicitud fallida hasta el fin de la ultima fallida
function outage(log, from, to) {
  const w = log.filter(e => e.t >= from && e.t <= to);
  const fails = w.filter(e => !e.ok);
  const downtime = fails.length ? Math.max(...fails.map(e => e.end)) - Math.min(...fails.map(e => e.t)) : 0;
  return { total: w.length, fallidas: fails.length, downtimeMs: downtime, p95: pct(w.filter(e => e.ok).map(e => e.lat), 0.95) };
}

// ======================================================================
// ESCENARIO 1: actualizaciones de version (10 repeticiones por estrategia)
// ======================================================================
async function escenarioDeploy(reps = 10, conc = 16) {
  const res = { tradicional: [], blueGreen: [] };

  // --- A: tradicional (detener -> sobrescribir archivos -> iniciar), sin proxy ---
  const dirA = path.join(SITES, 'A');
  copyRelease('v1', dirA);
  let inst = startInstance('A', dirA, 5100, MASKS[0]);
  await waitHealthy(5100);
  let load = startLoad(5100, conc);
  await sleep(3000);
  for (let i = 0; i < reps; i++) {
    const nueva = i % 2 === 0 ? 'v2' : 'v1';
    const t0 = now();
    await stopInstance(inst);                 // equivale a reciclar el App Pool / app_offline
    copyRelease(nueva, dirA);                 // equivale a subir los binarios por FTP/WebDeploy
    inst = startInstance('A', dirA, 5100, MASKS[0]);
    await waitHealthy(5100);
    const t1 = now();
    await sleep(3000);
    res.tradicional.push({ rep: i + 1, version: nueva, duracionMs: t1 - t0, ...outage(load.log, t0 - 500, now()) });
    console.log('A', res.tradicional.at(-1));
  }
  await load.stop(); await stopInstance(inst);

  // --- B: blue-green detras del proxy inverso ---
  const proxy = spawn(process.execPath, [path.join(import.meta.dirname, 'proxy.mjs'), '5200', '5299'], { stdio: 'ignore' });
  setAffinity(proxy.pid, 0xF00); await sleep(500);
  const slots = { blue: { dir: path.join(SITES, 'blue'), port: 5201, mask: MASKS[0] }, green: { dir: path.join(SITES, 'green'), port: 5202, mask: MASKS[1] } };
  copyRelease('v1', slots.blue.dir);
  let activo = startInstance('blue', slots.blue.dir, 5201, MASKS[0]); let activoSlot = 'blue';
  await waitHealthy(5201);
  await post(5299, '/pool', [{ host: '127.0.0.1', port: 5201, name: 'blue' }]);
  load = startLoad(5200, conc);
  await sleep(3000);
  for (let i = 0; i < reps; i++) {
    const nueva = i % 2 === 0 ? 'v2' : 'v1';
    const idleSlot = activoSlot === 'blue' ? 'green' : 'blue'; const s = slots[idleSlot];
    const t0 = now();
    copyRelease(nueva, s.dir);
    const nuevo = startInstance(idleSlot, s.dir, s.port, s.mask);
    await waitHealthy(s.port);
    const smoke = await get(s.port, '/api/pedidos/1');                    // prueba de humo antes de exponer
    if (!smoke.ok) throw new Error('smoke test fallido');
    const tSwitch = now();
    await post(5299, '/pool', [{ host: '127.0.0.1', port: s.port, name: idleSlot }]);   // conmutacion atomica
    const t1 = now();
    await sleep(2000);                                                     // drenado de conexiones
    await stopInstance(activo);
    activo = nuevo; activoSlot = idleSlot;
    await sleep(1000);
    res.blueGreen.push({ rep: i + 1, version: nueva, duracionMs: t1 - t0, conmutacionMs: t1 - tSwitch, ...outage(load.log, t0 - 500, now()) });
    console.log('B', res.blueGreen.at(-1));
  }
  await load.stop(); await stopInstance(activo); proxy.kill();
  return res;
}

// ======================================================================
// ESCENARIO 2: reversion ante una version defectuosa (5 repeticiones)
// ======================================================================
async function detectarFallo(port, desde) {
  // monitor sintetico: sondea cada 100 ms hasta observar un HTTP 500
  while (true) { const r = await get(port, '/api/pedidos/1'); if (r.status >= 500) return now(); if (now() - desde > 60000) throw new Error('sin deteccion'); await sleep(100); }
}
function primerExitoTras(log, t) {
  const e = log.filter(x => x.t >= t && x.ok && x.version === '1.0.0').sort((a, b) => a.end - b.end)[0];
  return e ? e.end : NaN;
}
async function escenarioRollback(reps = 5, conc = 16) {
  const res = { tradicional: [], blueGreen: [], blueGreenConGate: [] };

  // --- A: tradicional ---
  const dirA = path.join(SITES, 'A');
  copyRelease('v1', dirA);
  let inst = startInstance('A', dirA, 5100, MASKS[0]); await waitHealthy(5100);
  let load = startLoad(5100, conc); await sleep(2000);
  for (let i = 0; i < reps; i++) {
    const t0 = now();
    await stopInstance(inst); copyRelease('v3bug', dirA);
    inst = startInstance('A', dirA, 5100, MASKS[0]);
    const tDet = await detectarFallo(5100, t0);
    await stopInstance(inst); copyRelease('v1', dirA);                     // reversion manual: volver a subir la version previa
    inst = startInstance('A', dirA, 5100, MASKS[0]); await waitHealthy(5100);
    await sleep(2500);
    const tOk = primerExitoTras(load.log, tDet);
    res.tradicional.push({ rep: i + 1, mttrMs: tOk - tDet, ...outage(load.log, t0, now()) });
    console.log('RA', res.tradicional.at(-1));
  }
  await load.stop(); await stopInstance(inst);

  // --- B: blue-green; la version previa sigue viva durante la ventana de reversion ---
  const proxy = spawn(process.execPath, [path.join(import.meta.dirname, 'proxy.mjs'), '5200', '5299'], { stdio: 'ignore' });
  setAffinity(proxy.pid, 0xF00); await sleep(500);
  const blueDir = path.join(SITES, 'blue'), greenDir = path.join(SITES, 'green');
  copyRelease('v1', blueDir); copyRelease('v3bug', greenDir);
  const blue = startInstance('blue', blueDir, 5201, MASKS[0]); await waitHealthy(5201);
  const BLUE = [{ host: '127.0.0.1', port: 5201, name: 'blue' }], GREEN = [{ host: '127.0.0.1', port: 5202, name: 'green' }];
  await post(5299, '/pool', BLUE);
  load = startLoad(5200, conc); await sleep(2000);
  for (let i = 0; i < reps; i++) {
    // B1: el defecto pasa el health check (/healthz responde 200) y llega a produccion
    const green = startInstance('green', greenDir, 5202, MASKS[1]); await waitHealthy(5202);
    const t0 = now();
    await post(5299, '/pool', GREEN);
    const tDet = await detectarFallo(5200, t0);
    await post(5299, '/pool', BLUE);                                       // reversion = devolver el trafico a blue
    await sleep(2500);
    const tOk = primerExitoTras(load.log, tDet);
    res.blueGreen.push({ rep: i + 1, mttrMs: tOk - tDet, ...outage(load.log, t0, now()) });
    console.log('RB', res.blueGreen.at(-1));

    // B2: mismo despliegue, pero con prueba de humo como compuerta antes de conmutar
    const t2 = now();
    const smoke = await get(5202, '/api/pedidos/1');
    const bloqueado = !smoke.ok;
    if (!bloqueado) await post(5299, '/pool', GREEN);
    await sleep(2500);
    res.blueGreenConGate.push({ rep: i + 1, bloqueado, smokeStatus: smoke.status, ...outage(load.log, t2, now()) });
    console.log('RG', res.blueGreenConGate.at(-1));
    await stopInstance(green);
  }
  await load.stop(); await stopInstance(blue); proxy.kill();
  return res;
}

// ======================================================================
// ESCENARIO 3: escalabilidad (capacidad fija 1/2/4 instancias y autoescalado)
// ======================================================================
const ITER = 200000;   // trabajo de CPU por solicitud (~ calculo de un pedido pesado)
async function capacidad(n, conc = 64, durMs = 15000) {
  const proxy = spawn(process.execPath, [path.join(import.meta.dirname, 'proxy.mjs'), '5200', '5299'], { stdio: 'ignore' });
  setAffinity(proxy.pid, 0xF00); await sleep(500);
  const insts = [];
  for (let k = 0; k < n; k++) {
    const dir = path.join(SITES, 'r' + k); copyRelease('v1', dir);
    insts.push(startInstance('r' + k, dir, 5301 + k, MASKS[k]));
  }
  for (const s of insts) await waitHealthy(s.port);
  await post(5299, '/pool', insts.map(s => ({ host: s.host, port: s.port, name: s.name })));
  const warm = startLoad(5200, 8, ITER); await sleep(3000); await warm.stop();
  const load = startLoad(5200, conc, ITER);
  const t0 = now(); await sleep(durMs); const log = await load.stop(); const t1 = now();
  const okL = log.filter(e => e.ok);
  for (const s of insts) await stopInstance(s); proxy.kill(); await sleep(500);
  return { instancias: n, concurrencia: conc, rps: okL.length / ((t1 - t0) / 1000), p50: pct(okL.map(e => e.lat), 0.5), p95: pct(okL.map(e => e.lat), 0.95), errores: log.length - okL.length };
}

async function rampa(autoescalar) {
  const proxy = spawn(process.execPath, [path.join(import.meta.dirname, 'proxy.mjs'), '5200', '5299'], { stdio: 'ignore' });
  setAffinity(proxy.pid, 0xF00); await sleep(500);
  const insts = [];
  const add = async () => {
    const k = insts.length; const dir = path.join(SITES, 'r' + k); copyRelease('v1', dir);
    const s = startInstance('r' + k, dir, 5301 + k, MASKS[k]); await waitHealthy(s.port);
    await get(s.port, `/api/pedidos/1?iter=${ITER}`);   // calentamiento antes de recibir trafico
    insts.push(s);
    await post(5299, '/pool', insts.map(x => ({ host: x.host, port: x.port, name: x.name })));
  };
  await add();
  await get(5299, '/stats');
  const etapas = [4, 8, 16, 32, 64]; const ETAPA_MS = 12000;
  const load = startLoad(5200, etapas[0], ITER);
  const serie = []; const eventos = []; const t0 = now(); let ultimoEscalado = -1e9;
  let etapa = 0;
  while (now() - t0 < etapas.length * ETAPA_MS) {
    const e = Math.min(etapas.length - 1, Math.floor((now() - t0) / ETAPA_MS));
    if (e !== etapa) { etapa = e; load.setConcurrency(etapas[e]); }
    await sleep(1000);
    const st = JSON.parse((await get(5299, '/stats')).body);
    const seg = Math.round((now() - t0) / 1000);
    serie.push({ seg, usuarios: etapas[etapa], rps: st.n, p95: Math.round(st.p95), instancias: insts.length, errores: st.err });
    // politica: si p95 > 30 ms (SLO) durante 2 s consecutivos, agregar una instancia (enfriamiento 5 s, max 4)
    const ult = serie.slice(-2);
    if (autoescalar && insts.length < 4 && ult.length === 2 && ult.every(x => x.p95 > 30) && now() - ultimoEscalado > 5000) {
      const ta = now(); await add(); ultimoEscalado = now();
      eventos.push({ seg, accion: `escala a ${insts.length} instancias`, tiempoAprovisionamientoMs: Math.round(now() - ta) });
      console.log('scale-out', eventos.at(-1));
    }
  }
  await load.stop();
  for (const s of insts) await stopInstance(s); proxy.kill(); await sleep(500);
  return { autoescalar, serie, eventos };
}

async function escenarioEscala() {
  const fijo = [];
  for (const n of [1, 2, 4]) { fijo.push(await capacidad(n)); console.log('cap', fijo.at(-1)); }
  const sinAuto = await rampa(false); console.log('rampa fija hecha');
  const conAuto = await rampa(true); console.log('rampa auto hecha');
  return { fijo, sinAuto, conAuto };
}

// ======================================================================
const cual = process.argv[2] ?? 'todo';
const salida = { fecha: new Date().toISOString(), equipo: 'Intel Core i7-13650HX, 16 GB RAM, Windows 11 Pro, .NET 9.0.318, Node ' + process.version };
if (cual === 'deploy' || cual === 'todo') salida.deploy = await escenarioDeploy();
if (cual === 'rollback' || cual === 'todo') salida.rollback = await escenarioRollback();
if (cual === 'escala' || cual === 'todo') salida.escala = await escenarioEscala();
fs.writeFileSync(path.join(OUT, `resultados_${cual}.json`), JSON.stringify(salida, null, 2));
console.log('OK ->', path.join(OUT, `resultados_${cual}.json`));
process.exit(0);
