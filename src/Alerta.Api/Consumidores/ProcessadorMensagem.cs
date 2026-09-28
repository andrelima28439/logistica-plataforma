using System.Text;
using System.Text.Json;
using Shared.Kernel;

namespace Alerta.Api.Consumidores;

public enum DecisaoMensagem
{
    Ack,
    NackSemRequeue // => broker roteia para a DLQ (dead-letter exchange)
}

public sealed record MensagemValida(
    string RoutingKey,
    Guid EntregaId,
    string CorrelationId,
    string MotivoOuAtraso,
    string Severidade,
    int AtrasoEstimadoMinutos);

// Decisão pura de consumo (testável sem broker): desserializa conforme a
// routing key; qualquer falha => nack sem requeue => DLQ de verdade.
public static class ProcessadorMensagem
{
    public static (DecisaoMensagem Decisao, MensagemValida? Mensagem) Avaliar(
        string routingKey, byte[] corpo)
    {
        try
        {
            if (routingKey == "desvio.detectado")
            {
                var e = JsonSerializer.Deserialize<DesvioDetectadoEvent>(
                    Encoding.UTF8.GetString(corpo));
                if (e is null || e.EntregaId == Guid.Empty)
                    return (DecisaoMensagem.NackSemRequeue, null);
                return (DecisaoMensagem.Ack,
                    new MensagemValida(routingKey, e.EntregaId, e.CorrelationId,
                        e.Motivo, e.Severidade, 0));
            }

            if (routingKey == "atraso.detectado")
            {
                var e = JsonSerializer.Deserialize<AtrasoDetectadoEvent>(
                    Encoding.UTF8.GetString(corpo));
                if (e is null || e.EntregaId == Guid.Empty)
                    return (DecisaoMensagem.NackSemRequeue, null);
                return (DecisaoMensagem.Ack,
                    new MensagemValida(routingKey, e.EntregaId, e.CorrelationId,
                        string.Empty, e.Severidade, e.AtrasoEstimadoMinutos));
            }

            return (DecisaoMensagem.NackSemRequeue, null); // routing key desconhecida
        }
        catch (JsonException)
        {
            return (DecisaoMensagem.NackSemRequeue, null); // poison message => DLQ
        }
    }
}
