// Contratos de eventos de integração entre os microsserviços (RabbitMQ).
// Produtores/consumidores ficam em Rastreamento, Roteamento e Alerta; os contratos
// vivem aqui em Shared.Kernel para que todos os serviços compartilhem os mesmos tipos.
namespace Shared.Kernel;

public abstract record EventoIntegracao(
    Guid EventoId,
    DateTimeOffset OcorridoEm,
    string CorrelationId);

/// <summary>Publicado por Rastreamento.Api a cada posição GPS recebida.</summary>
public sealed record PosicaoAtualizadaEvent(
    Guid EventoId,
    DateTimeOffset OcorridoEm,
    string CorrelationId,
    Guid EntregaId,
    double Latitude,
    double Longitude,
    DateTimeOffset CapturadaEm) : EventoIntegracao(EventoId, OcorridoEm, CorrelationId);

/// <summary>Publicado por Roteamento.Api ao detectar saída da rota esperada.</summary>
public sealed record DesvioDetectadoEvent(
    Guid EventoId,
    DateTimeOffset OcorridoEm,
    string CorrelationId,
    Guid EntregaId,
    string Motivo,
    string Severidade) : EventoIntegracao(EventoId, OcorridoEm, CorrelationId);

/// <summary>Publicado por Roteamento.Api ao detectar risco de atraso.</summary>
public sealed record AtrasoDetectadoEvent(
    Guid EventoId,
    DateTimeOffset OcorridoEm,
    string CorrelationId,
    Guid EntregaId,
    int AtrasoEstimadoMinutos,
    string Severidade) : EventoIntegracao(EventoId, OcorridoEm, CorrelationId);
