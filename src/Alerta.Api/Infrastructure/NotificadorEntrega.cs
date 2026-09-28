using System.Text;
using System.Text.Json;

namespace Alerta.Api.Infrastructure;

// Avisa a Entrega.Api (best-effort) para fazer broadcast "AlertaGerado"
// via SignalR. Falha aqui NUNCA quebra o alerta (já persistido).
public sealed class NotificadorEntrega(HttpClient http, ILogger<NotificadorEntrega> logger)
{
    public async Task NotificarAlertaAsync(
        Guid entregaId, string tipo, string severidade, string mensagem,
        CancellationToken ct = default)
    {
        try
        {
            using var resposta = await http.PostAsync(
                $"/entregas/{entregaId}/notificacoes/alerta",
                new StringContent(
                    JsonSerializer.Serialize(new { tipo, severidade, mensagem }),
                    Encoding.UTF8, "application/json"), ct);
            resposta.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Não foi possível notificar alerta da entrega {EntregaId}.", entregaId);
        }
    }
}
