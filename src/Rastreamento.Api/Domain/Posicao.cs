namespace Rastreamento.Api.Domain;

// Posição GPS de uma entrega. O histórico fica em memória
// (mesma decisão do ADR-003); a "última posição conhecida" vai ao Redis.
public sealed record Posicao(
    Guid EntregaId,
    double Latitude,
    double Longitude,
    DateTimeOffset CapturadaEm);
