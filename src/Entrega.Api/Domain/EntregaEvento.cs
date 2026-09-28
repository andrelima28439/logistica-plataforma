namespace Entrega.Api.Domain;

// Eventos de domínio da Entrega. O detalhe GET /entregas/{id} os expõe;
// os eventos de integração (RabbitMQ) vivem em Shared.Kernel.
public abstract record EntregaEvento(Guid EntregaId, DateTimeOffset Quando);

public sealed record EntregaCriada(Guid EntregaId, DateTimeOffset Quando)
    : EntregaEvento(EntregaId, Quando);

public sealed record StatusAlterado(
    Guid EntregaId,
    StatusEntrega De,
    StatusEntrega Para,
    DateTimeOffset Quando) : EntregaEvento(EntregaId, Quando);
