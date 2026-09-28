using System.Diagnostics;
using Rastreamento.Api.Domain;
using Rastreamento.Api.Observabilidade;

namespace Rastreamento.Api.Application;

// Orquestra o fluxo de cada posição: histórico + cache Redis + evento.
// Usado tanto pelo POST manual quanto pelo worker de simulação GPS.
public sealed class ServicoRastreamento(
    IPosicaoRepositorio repositorio,
    ICacheUltimaPosicao cache,
    IPublicadorPosicao publicador,
    ILogger<ServicoRastreamento> logger)
{
    public async Task<Posicao> RegistrarPosicaoAsync(
        Guid entregaId,
        double latitude,
        double longitude,
        string? correlationId = null,
        string origem = "manual",
        CancellationToken ct = default)
    {
        var inicio = Stopwatch.GetTimestamp();
        try
        {
            var posicao = new Posicao(entregaId, latitude, longitude, DateTimeOffset.UtcNow);
            var correlacao = string.IsNullOrWhiteSpace(correlationId)
                ? Guid.NewGuid().ToString("N")
                : correlationId;

            repositorio.Registrar(posicao);
            await cache.DefinirAsync(posicao, ct);
            if (!await publicador.PublicarAsync(posicao, correlacao, ct))
                MetricasRastreamento.PublicacaoFalhas.Inc();

            MetricasRastreamento.PosicoesRegistradas.Labels(origem).Inc();
            logger.LogDebug(
                "Posicao registrada entrega {EntregaId} lat {Latitude} lon {Longitude} correlacao {Correlacao}",
                entregaId, latitude, longitude, correlacao);

            return posicao;
        }
        finally
        {
            MetricasRastreamento.DuracaoRegistro.Observe(
                Stopwatch.GetElapsedTime(inicio).TotalSeconds);
        }
    }

    public IReadOnlyList<Posicao> Historico(Guid entregaId) =>
        repositorio.Listar(entregaId);

    public Task<Posicao?> UltimaConhecidaAsync(Guid entregaId, CancellationToken ct = default) =>
        cache.ObterAsync(entregaId, ct);
}
