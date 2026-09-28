using Rastreamento.Api.Domain;

namespace Rastreamento.Api.Application;

// Cache da última posição conhecida (Redis em produção local; a leitura
// do GET ultima-posicao NÃO passa pelo repositório/banco).
public interface ICacheUltimaPosicao
{
    Task DefinirAsync(Posicao posicao, CancellationToken ct = default);
    Task<Posicao?> ObterAsync(Guid entregaId, CancellationToken ct = default);
}

// Publicação do evento posicao.atualizada (Shared.Kernel) no RabbitMQ.
// Retorna false quando o broker está fora (para a métrica de falhas).
public interface IPublicadorPosicao
{
    Task<bool> PublicarAsync(Posicao posicao, string correlationId, CancellationToken ct = default);
}
