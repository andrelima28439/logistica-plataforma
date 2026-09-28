using Polly.CircuitBreaker;
using Roteamento.Api.Application;
using Roteamento.Api.Resiliencia;

namespace Roteamento.Api.Infrastructure;

// Compõe primário (sob circuit breaker) + fallback (Redis direto).
// Quebra do primário OU breaker aberto => tenta o cache; sem cache,
// responde "indisponivel" (degradada, sem exceção para o chamador).
public sealed class ProvedorHistoricoResiliente(
    CircuitoHistorico circuito,
    IProvedorHistoricoPrimario primario,
    IFallbackHistorico fallback,
    ILogger<ProvedorHistoricoResiliente> logger) : IProvedorHistorico
{
    public async Task<ResultadoHistorico> ObterHistoricoAsync(
        Guid entregaId, CancellationToken ct = default)
    {
        try
        {
            var posicoes = await circuito.ExecutarAsync(
                token => primario.ObterAsync(entregaId, token), ct);
            return new ResultadoHistorico(posicoes, "rastreamento");
        }
        catch (BrokenCircuitException)
        {
            logger.LogWarning(
                "Breaker aberto para {EntregaId}; usando fallback Redis.", entregaId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Falha ao buscar histórico da {EntregaId}; usando fallback Redis.", entregaId);
        }

        var cache = await fallback.ObterAsync(entregaId, ct);
        return cache.Count > 0
            ? new ResultadoHistorico(cache, "fallback-cache")
            : new ResultadoHistorico(cache, "indisponivel");
    }
}
