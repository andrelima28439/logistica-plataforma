using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace Alerta.Api.Infrastructure;

public sealed record ManagementOpcoes(string BaseUrl, string User, string Pass);

// Publicador cru (bytes) — usado pelo endpoint de simulação de falha:
// publica uma poison message de verdade no exchange, com routing key real.
public sealed class PublicadorCruRabbitMq(ConexaoRabbitOpcoes opcoes)
{
    public void PublicarBytes(string routingKey, byte[] corpo)
    {
        var factory = new ConnectionFactory
        {
            HostName = opcoes.Host,
            Port = opcoes.Port,
            UserName = opcoes.User,
            Password = opcoes.Pass,
            AutomaticRecoveryEnabled = true
        };
        using var conexao = factory.CreateConnection("alerta-api-simular-falha");
        using var canal = conexao.CreateModel();
        canal.ExchangeDeclare(opcoes.Exchange, ExchangeType.Topic, durable: true);
        var props = canal.CreateBasicProperties();
        props.Persistent = true;
        props.CorrelationId = "simulacao-falha";
        canal.BasicPublish(opcoes.Exchange, routingKey, props, corpo);
    }
}

public sealed record ConexaoRabbitOpcoes(
    string Host,
    int Port,
    string User,
    string Pass,
    string Exchange);

// Inspetor da DLQ via Management API do RabbitMQ (só leitura + peek com
// requeue: a mensagem continua na DLQ após a inspeção).
public sealed class InspetorDlq(HttpClient http)
{
    public async Task<(long Mensagens, List<DlqMensagem> Amostra)> InspecionarAsync(
        CancellationToken ct = default)
    {
        var info = JsonDocument.Parse(
            await http.GetStringAsync("/api/queues/%2F/alerta.eventos.dlq", ct)).RootElement;
        var total = info.TryGetProperty("messages", out var m) ? m.GetInt64() : 0;

        var amostra = new List<DlqMensagem>();
        if (total > 0)
        {
            var payload = new
            {
                count = Math.Min(total, 5),
                ackmode = "ack_requeue_true",
                encoding = "auto"
            };
            var resposta = await http.PostAsync("/api/queues/%2F/alerta.eventos.dlq/get",
                new StringContent(JsonSerializer.Serialize(payload),
                    Encoding.UTF8, "application/json"), ct);
            resposta.EnsureSuccessStatusCode();
            foreach (var item in JsonDocument.Parse(
                await resposta.Content.ReadAsStringAsync(ct)).RootElement.EnumerateArray())
            {
                amostra.Add(new DlqMensagem(
                    item.TryGetProperty("routing_key", out var rk) ? rk.GetString() ?? "" : "",
                    item.TryGetProperty("payload", out var p) ? p.GetString() ?? "" : ""));
            }
        }

        return (total, amostra);
    }
}

public sealed record DlqMensagem(string RoutingKey, string Payload);

public static class ManagementHttpClient
{
    public static HttpClient Criar(ManagementOpcoes opcoes)
    {
        var http = new HttpClient { BaseAddress = new Uri(opcoes.BaseUrl) };
        var cred = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{opcoes.User}:{opcoes.Pass}"));
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", cred);
        return http;
    }
}
