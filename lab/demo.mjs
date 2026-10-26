// Demo blue-green local de TecnoMarket (para la defensa, sin Azure).
// Uso:  node demo.mjs            -> publica v1 (blue) y v2 (green) si hace falta y abre la tienda en http://localhost:5200
//       node demo.mjs --rebuild  -> vuelve a publicar ambas versiones (despues de cambiar el codigo)
// Teclas: g = trafico a GREEN, b = trafico a BLUE, s = estado, q = salir
import { spawn, execSync } from 'node:child_process';
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import readline from 'node:readline';

const ROOT = path.resolve(import.meta.dirname, '..');
const PROYECTO = path.join(ROOT, 'BlazorPub', 'BlazorPub', 'BlazorPub.csproj');
const DEMO = path.join(ROOT, 'demo');
const DB = path.join(DEMO, 'tienda.db');   // base de datos compartida por ambas versiones
const PUERTO = 5200, ADMIN = 5299;
const SLOTS = {
  blue: { version: '1.0.0', puerto: 5201, dir: path.join(DEMO, 'blue') },
  green: { version: '2.0.0', puerto: 5202, dir: path.join(DEMO, 'green') },
};
const sleep = ms => new Promise(r => setTimeout(r, ms));
const procesos = [];

function get(port, p) {
  return new Promise(res => {
    http.get({ host: '127.0.0.1', port, path: p, timeout: 2000 }, r => {
      let b = ''; r.on('data', d => b += d); r.on('end', () => res({ ok: r.statusCode === 200, body: b }));
    }).on('error', () => res({ ok: false, body: '' })).on('timeout', function () { this.destroy(); });
  });
}
function post(port, p, data) {
  return new Promise(res => {
    const req = http.request({ host: '127.0.0.1', port, path: p, method: 'POST' }, r => { r.resume(); r.on('end', res); });
    req.on('error', res); req.end(JSON.stringify(data));
  });
}

// 1. Publicar ambas versiones
fs.mkdirSync(DEMO, { recursive: true });
for (const [nombre, s] of Object.entries(SLOTS)) {
  if (process.argv.includes('--rebuild') || !fs.existsSync(path.join(s.dir, 'BlazorPub.dll'))) {
    console.log(`Publicando ${nombre.toUpperCase()} (v${s.version})...`);
    execSync(`dotnet publish "${PROYECTO}" -c Release -o "${s.dir}" -p:InformationalVersion=${s.version} --nologo -v q`, { stdio: 'inherit' });
  }
}

// 2. Levantar el proxy y las instancias de a una: la primera crea la base de datos compartida,
//    asi la segunda no compite por crear las mismas tablas
procesos.push(spawn(process.execPath, [path.join(import.meta.dirname, 'proxy.mjs'), String(PUERTO), String(ADMIN)], { stdio: 'ignore' }));
for (const [nombre, s] of Object.entries(SLOTS)) {
  const log = fs.openSync(path.join(DEMO, `${nombre}.log`), 'w');
  procesos.push(spawn('dotnet', ['BlazorPub.dll'], {
    cwd: s.dir, stdio: ['ignore', log, log],
    env: { ...process.env, ASPNETCORE_URLS: `http://127.0.0.1:${s.puerto}`, ASPNETCORE_ENVIRONMENT: 'Production', INSTANCIA: nombre, ConnectionStrings__Tienda: `Data Source=${DB}` },
  }));
  process.stdout.write(`Iniciando ${nombre.toUpperCase()}`);
  let ok = false;
  for (let i = 0; i < 120 && !ok; i++) { ok = (await get(s.puerto, '/healthz')).ok; if (!ok) { process.stdout.write('.'); await sleep(500); } }
  if (!ok) { console.error(`\n${nombre.toUpperCase()} no arrancó. Revisa demo/${nombre}.log`); salir(1); }
  console.log(' sano');
}

let activo = null;
async function conmutar(nombre) {
  const s = SLOTS[nombre];
  const salud = await get(s.puerto, '/healthz');
  const humo = await get(s.puerto, '/api/tienda/productos');
  if (!salud.ok || !humo.ok) { console.log(`✗ ${nombre.toUpperCase()} no pasó el health check / prueba de humo: se mantiene ${activo}`); return; }
  const t0 = performance.now();
  await post(ADMIN, '/pool', [{ host: '127.0.0.1', port: s.puerto, name: nombre }]);
  activo = nombre;
  console.log(`→ Tráfico en ${nombre.toUpperCase()} (v${s.version}) · conmutación en ${(performance.now() - t0).toFixed(1)} ms`);
}
await conmutar('blue');

console.log(`\n\nTecnoMarket listo en  http://localhost:${PUERTO}`);
console.log('  g = pasar el tráfico a GREEN (v2.0.0)   b = volver a BLUE (v1.0.0)   s = estado   q = salir');
console.log('  Las dos versiones siguen encendidas: cambiar es instantáneo y la reversión también.');
console.log('  Tras conmutar, recarga la página: el pie de página muestra la versión y el servidor.\n');

function salir(code = 0) {
  for (const p of procesos) { try { p.kill(); } catch { } }
  process.exit(code);
}
process.on('SIGINT', () => salir());

if (process.stdin.isTTY) {
  readline.emitKeypressEvents(process.stdin);
  process.stdin.setRawMode(true);
  process.stdin.on('keypress', async (_, key) => {
    if (!key) return;
    if (key.name === 'q' || (key.ctrl && key.name === 'c')) salir();
    else if (key.name === 'g') await conmutar('green');
    else if (key.name === 'b') await conmutar('blue');
    else if (key.name === 's') {
      for (const [n, s] of Object.entries(SLOTS)) console.log(`  ${n.padEnd(5)} :${s.puerto}  ${(await get(s.puerto, '/healthz')).ok ? 'sano' : 'caído'}${n === activo ? '  ← recibe el tráfico' : ''}`);
    }
  });
}
