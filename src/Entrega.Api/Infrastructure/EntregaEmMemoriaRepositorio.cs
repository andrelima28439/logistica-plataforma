using System.Collections.Concurrent;
using Entrega.Api.Application;
using Entrega.Api.Domain;

namespace Entrega.Api.Infrastructure;

// Implementação em memória atrás de IEntregaRepositorio. Troca por EF Core + SQL Server
// não muda o contrato — ver ADR-003.
public sealed class EntregaEmMemoriaRepositorio : IEntregaRepositorio
{
    private readonly ConcurrentDictionary<Guid, Domain.Entrega> _entregas = new();

    public void Adicionar(Domain.Entrega entrega) =>
        _entregas[entrega.Id] = entrega;

    public Domain.Entrega? ObterPorId(Guid id) =>
        _entregas.TryGetValue(id, out var entrega) ? entrega : null;

    public IReadOnlyList<Domain.Entrega> Listar(StatusEntrega? status, string? transportadorId) =>
        _entregas.Values
            .Where(e => status is null || e.Status == status)
            .Where(e => string.IsNullOrWhiteSpace(transportadorId)
                || string.Equals(e.TransportadorId, transportadorId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.DataCriacao)
            .ToList();
}
