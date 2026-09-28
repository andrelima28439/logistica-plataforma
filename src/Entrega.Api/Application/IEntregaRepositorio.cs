using Entrega.Api.Domain;

namespace Entrega.Api.Application;

// A troca da implementação (ex.: EF Core + SQL Server) não muda
// este contrato — ver ADR-003.
public interface IEntregaRepositorio
{
    void Adicionar(Domain.Entrega entrega);
    Domain.Entrega? ObterPorId(Guid id);
    IReadOnlyList<Domain.Entrega> Listar(StatusEntrega? status, string? transportadorId);
}
