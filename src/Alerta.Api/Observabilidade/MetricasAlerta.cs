using Prometheus;

namespace Alerta.Api.Observabilidade;

// Métricas customizadas: taxa de alertas por tipo, canais
// acionados, falhas e profundidade da DLQ (gauge via poller).
public static class MetricasAlerta
{
    public static readonly Counter AlertasProcessados = Metrics.CreateCounter(
        "alerta_processados_total",
        "Total de alertas processados.",
        "tipo"); // DESVIO | ATRASO

    public static readonly Counter CanaisAcionados = Metrics.CreateCounter(
        "alerta_canais_total",
        "Total de acionamentos por canal.",
        "canal"); // email | sms | painel

    public static readonly Counter FalhasProcessamento = Metrics.CreateCounter(
        "alerta_falhas_total",
        "Falhas ao processar mensagens (vão para a DLQ).");

    public static readonly Histogram DuracaoProcessamento = Metrics.CreateHistogram(
        "alerta_processamento_duracao_seconds",
        "Latência do processamento de um evento.");

    public static readonly Gauge DlqMensagens = Metrics.CreateGauge(
        "alerta_dlq_mensagens",
        "Profundidade atual da DLQ (alerta.eventos.dlq).");
}
