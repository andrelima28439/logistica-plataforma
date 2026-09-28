using Prometheus;

namespace Roteamento.Api.Observabilidade;

// Métricas customizadas — incluindo o ESTADO DO CIRCUIT BREAKER
// como gauge (0=fechado, 1=aberto, 2=meio-aberto), atualizado nas
// transições do Polly (ver CircuitoHistorico).
public static class MetricasRoteamento
{
    public static readonly Counter Analises = Metrics.CreateCounter(
        "roteamento_analises_total",
        "Total de análises de rota concluídas.",
        "resultado"); // DESVIO | ATRASO | NENHUM

    public static readonly Histogram DuracaoAnalise = Metrics.CreateHistogram(
        "roteamento_analise_duracao_seconds",
        "Latência da análise (histórico + estratégia + publish).");

    public static readonly Counter EventosPublicados = Metrics.CreateCounter(
        "roteamento_eventos_publicados_total",
        "Eventos desvio/atraso publicados no RabbitMQ.",
        "routing_key");

    public static readonly Gauge CircuitoEstado = Metrics.CreateGauge(
        "roteamento_circuito_estado",
        "Estado do circuit breaker da Rastreamento.Api (0=fechado, 1=aberto, 2=meio-aberto).");

    public static void DefinirEstadoCircuito(string estado) =>
        CircuitoEstado.Set(estado switch
        {
            "Open" => 1,
            "HalfOpen" => 2,
            _ => 0
        });
}
