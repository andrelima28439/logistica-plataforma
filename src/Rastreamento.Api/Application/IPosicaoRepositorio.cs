using Rastreamento.Api.Domain;

namespace Rastreamento.Api.Application;

public interface IPosicaoRepositorio
{
    void Registrar(Posicao posicao);
    IReadOnlyList<Posicao> Listar(Guid entregaId);
    Posicao? Ultima(Guid entregaId);
}
