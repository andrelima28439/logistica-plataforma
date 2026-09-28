// Prova de tempo real: conecta no Hub SignalR da
// Entrega.Api como um navegador faria, dispara o fluxo pelo backend
// (criar entrega -> GPS mockado -> desvio) e verifica se StatusAlterado +
// AlertaGerado chegam SEM refresh (sem polling do Hub).
//
// Uso: 1) docker compose up -d  2) subir as 4 APIs (:5001-:5004)
//      3) node scripts/smoke-signalr.mjs
import * as signalR from '../frontend/node_modules/@microsoft/signalr/dist/cjs/index.js';

const API = {
  entrega: 'http://localhost:5001',
  rastreamento: 'http://localhost:5002',
  roteamento: 'http://localhost:5003',
};

const post = async (url, body) => {
  const r = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!r.ok) throw new Error(`POST ${url} -> ${r.status}`);
  const ct = r.headers.get('content-type') ?? '';
  return ct.includes('json') ? r.json() : null;
};

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const recebeu = { status: null, alerta: null };
const t0 = Date.now();

const conn = new signalR.HubConnectionBuilder()
  .withUrl(`${API.entrega}/entregas/stream`)
  .withAutomaticReconnect()
  .build();

conn.on('StatusAlterado', (e) => {
  if (!recebeu.status) {
    recebeu.status = e;
    console.log(`[+${((Date.now() - t0) / 1000).toFixed(1)}s] StatusAlterado via SignalR:`, JSON.stringify(e));
  }
});
conn.on('AlertaGerado', (e) => {
  if (!recebeu.alerta) {
    recebeu.alerta = e;
    console.log(`[+${((Date.now() - t0) / 1000).toFixed(1)}s] AlertaGerado via SignalR:`, JSON.stringify(e));
  }
});

await conn.start();
console.log('SignalR conectado (como o dashboard faz).');

const entrega = await post(`${API.entrega}/entregas`, {
  origem: 'São Paulo/SP',
  destino: 'Santos/SP',
  transportadorId: 'transp-smoke',
  tipoCarga: 'REFRIGERADA',
  prazoEstimado: new Date(Date.now() + 2 * 864e5).toISOString(),
});
console.log('Entrega criada:', entrega.id);

await post(`${API.roteamento}/roteamento/entregas`, {
  entregaId: entrega.id,
  tipoCarga: 'REFRIGERADA',
  rotaEsperada: [{ latitude: -22.9068, longitude: -43.1729 }],
});
await post(`${API.rastreamento}/rastreamento/${entrega.id}/simulacao/iniciar`, { intervaloSegundos: 2 });
console.log('GPS mockado ligado + contexto com desvio; aguardando eventos (máx 40s)...');

const limite = Date.now() + 40000;
while (Date.now() < limite && (!recebeu.status || !recebeu.alerta)) await sleep(1000);

await post(`${API.rastreamento}/rastreamento/${entrega.id}/simulacao/parar`, {});
await conn.stop();

if (recebeu.status && recebeu.alerta) {
  console.log('SMOKE OK: status e alerta chegaram via SignalR, sem refresh.');
  process.exit(0);
}
console.error('SMOKE FALHOU:', JSON.stringify(recebeu));
process.exit(1);
