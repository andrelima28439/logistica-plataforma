// Tipos espelhando os contratos das APIs (JSON camelCase).
export interface EntregaResumo {
  id: string;
  origem: string;
  destino: string;
  transportadorId: string;
  tipoCarga: string;
  status: string;
  dataCriacao: string;
  prazoEstimado: string;
}

export interface Posicao {
  entregaId: string;
  latitude: number;
  longitude: number;
  capturadaEm: string;
}

export interface AlertaItem {
  id: string;
  entregaId: string;
  tipo: string;
  severidade: string;
  mensagem: string;
  canaisAcionados: string[];
  correlationId: string;
  criadoEm: string;
}

export interface HealthCheckEntry {
  nome: string;
  status: string;
  descricao: string;
  duracao: string;
}

export interface HealthResponse {
  status: string;
  checks: HealthCheckEntry[];
}

export interface CircuitoEstado {
  estado: string;
  timestamp: string;
}

export interface DlqInfo {
  fila: string;
  total: number;
  amostra: { routingKey: string; payload: string }[];
}

export interface AnaliseResultado {
  entregaId: string;
  fonteHistorico: string;
  tipo: string;
  severidade: string;
  motivo: string;
}

// Descoberta de ambiente sem build por ambiente (ADR-034):
// - ng serve (localhost) ......... APIs em localhost:5001-5004
// - servido pelo cluster (minikube IP) ... APIs nos NodePorts 30501-30504
// do MESMO host (window.location.hostname).
function apiBase(portaLocal: number, portaNodePort: number): string {
  if (typeof window === 'undefined') return `http://localhost:${portaLocal}`;
  const host = window.location.hostname;
  const local = host === 'localhost' || host === '127.0.0.1';
  return `http://${host}:${local ? portaLocal : portaNodePort}`;
}

const HOST_API = {
  entrega: apiBase(5001, 30501),
  rastreamento: apiBase(5002, 30502),
  roteamento: apiBase(5003, 30503),
  alerta: apiBase(5004, 30504),
};

export const API_URLS = {
  entrega: HOST_API.entrega,
  rastreamento: HOST_API.rastreamento,
  roteamento: HOST_API.roteamento,
  alerta: HOST_API.alerta,
  hubEntregas: `${HOST_API.entrega}/entregas/stream`,
};

export const CORES_STATUS: Record<string, string> = {
  CRIADA: '#9ca3af',
  EM_TRANSITO: '#3b82f6',
  DESVIO_DETECTADO: '#ef4444',
  ATRASADA: '#f59e0b',
  ENTREGUE: '#22c55e',
};
