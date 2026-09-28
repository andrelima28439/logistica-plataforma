using System.Text;
using System.Text.Json;

namespace Roteamento.Api.Infrastructure;

// Avisa a Entrega.Api (best-effort) para transitar o status e fazer
// broadcast via SignalR. Falha aqui NUNCA quebra a análise/roteamento:
// loga e segue (o evento RabbitMQ já foi publicado).
public sealed class NotificadorEntrega(HttpClient http, ILogger<NotificadorEntrega> logger)
{
    public async Task TransicaoAsync(
        Guid entregaId, string status, CancellationToken ct = default)
    {
        try
        {
            using var resposta = await http.PostAsync($"/entregas/{entregaId}/transicao",
                new StringContent(
                    JsonSerializer.Serialize(new { status }),
                    Encoding.UTF8, "application/json"), ct);
            resposta.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Não foi possível notificar transição {Status} da entrega {EntregaId}.",
                status, entregaId);
        }
    }
}
