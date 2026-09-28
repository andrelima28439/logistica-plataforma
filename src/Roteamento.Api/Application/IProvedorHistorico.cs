using Roteamento.Api.Domain;

namespace Roteamento.Api.Application;

// Histórico de posições obtido da Rastreamento.Api (primário) ou do
// cache Redis direto (fallback quando o primário falha).
public sealed record ResultadoHistorico(
    IReadOnlyList<PontoRota> Posicoes,
    string Fonte); // "rastreamento" | "fallback-cache" | "indisponivel"

public interface IProvedorHistorico
{
    Task<ResultadoHistorico> ObterHistoricoAsync(Guid entregaId, CancellationToken ct = default);
}
