using Prometheus;

namespace Rastreamento.Api.Observabilidade;

// Taxa de eventos processados + latência de registro.
public static class MetricasRastreamento
{
    public static readonly Counter PosicoesRegistradas = Metrics.CreateCounter(
        "rastreamento_posicoes_total",
        "Total de posições registradas (manuais + simuladas).",
        "origem"); // manual | simulada

    public static readonly Histogram DuracaoRegistro = Metrics.CreateHistogram(
        "rastreamento_registro_duracao_seconds",
        "Latência do registro de posição (histórico + cache + publish).");

    public static readonly Counter PublicacaoFalhas = Metrics.CreateCounter(
        "rastreamento_publicacao_falhas_total",
        "Falhas ao publicar posicao.atualizada (broker fora).");
}
