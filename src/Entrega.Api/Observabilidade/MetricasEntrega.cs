using Prometheus;

namespace Entrega.Api.Observabilidade;

// Métricas customizadas: taxa de criação + latência HTTP vem do
// middleware UseHttpMetrics (histograma http_request_duration_seconds).
// Classe estática: registro único por processo (seguro nos testes).
public static class MetricasEntrega
{
    public static readonly Counter EntregasCriadas = Metrics.CreateCounter(
        "entregas_criadas_total",
        "Total de entregas criadas via POST /entregas.");
}
