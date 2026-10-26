// Proxy inverso minimo (rol equivalente a Nginx/Traefik/YARP) para el experimento.
// - Balancea round-robin entre las instancias "activas".
// - Permite conmutar el pool en caliente (blue-green) via API de administracion.
// - Mantiene las conexiones WebSocket ya abiertas con su instancia original (drenado).
import http from 'node:http';
import net from 'node:net';

const PORT = Number(process.argv[2] ?? 5200);
const ADMIN = Number(process.argv[3] ?? 5299);

let pool = [];          // [{host, port, name}]
let rr = 0;
const agent = new http.Agent({ keepAlive: true, maxSockets: 512 });
const stats = { lat: [], ok: 0, err: 0 };

function pick() {
  if (pool.length === 0) return null;
  rr = (rr + 1) % pool.length;
  return pool[rr];
}

const server = http.createServer((req, res) => {
  const t = pick();
  if (!t) { res.writeHead(503); return res.end('sin instancias'); }
  const t0 = performance.now();
  const up = http.request({ host: t.host, port: t.port, method: req.method, path: req.url,
    headers: { ...req.headers, 'x-forwarded-for': req.socket.remoteAddress }, agent }, (r) => {
    res.writeHead(r.statusCode, r.headers);
    r.pipe(res);
    r.on('end', () => { stats.lat.push(performance.now() - t0); r.statusCode < 500 ? stats.ok++ : stats.err++; });
  });
  up.on('error', () => { stats.err++; if (!res.headersSent) { res.writeHead(502); } res.end('bad gateway'); });
  req.pipe(up);
});

// WebSocket (circuitos SignalR de Blazor Server): tunel TCP fijo a la instancia elegida
server.on('upgrade', (req, socket, head) => {
  const t = pick();
  if (!t) return socket.destroy();
  const up = net.connect(t.port, t.host, () => {
    let raw = `${req.method} ${req.url} HTTP/1.1\r\n`;
    for (let i = 0; i < req.rawHeaders.length; i += 2) raw += `${req.rawHeaders[i]}: ${req.rawHeaders[i + 1]}\r\n`;
    up.write(raw + '\r\n'); up.write(head);
    up.pipe(socket); socket.pipe(up);
  });
  up.on('error', () => socket.destroy()); socket.on('error', () => up.destroy());
});

server.keepAliveTimeout = 30000;
server.listen(PORT, '127.0.0.1');

http.createServer((req, res) => {
  if (req.method === 'POST' && req.url === '/pool') {
    let b = ''; req.on('data', d => b += d);
    req.on('end', () => { pool = JSON.parse(b); res.end(JSON.stringify({ pool })); });
  } else if (req.url === '/stats') {
    const l = stats.lat.sort((a, b) => a - b);
    const p95 = l.length ? l[Math.floor(l.length * 0.95)] : 0;
    res.end(JSON.stringify({ n: l.length, ok: stats.ok, err: stats.err, p95, pool: pool.map(p => p.name) }));
    stats.lat = []; stats.ok = 0; stats.err = 0;
  } else { res.end(JSON.stringify({ pool })); }
}).listen(ADMIN, '127.0.0.1');

console.log(`proxy :${PORT} admin :${ADMIN}`);
