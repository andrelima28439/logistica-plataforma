using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using Shared.Kernel;

namespace Roteamento.Api.Infrastructure;

public sealed record RabbitMqOpcoes(
    string Host,
    int Port,
    string User,
    string Pass,
    string Exchange);

// Publica desvio.detectado / atraso.detectado no mesmo
// exchange topic "logistica.eventos". Resiliente como os demais publishers (warning sem derrubar o fluxo).
public sealed class RabbitMqPublicadorRoteamento : IDisposable
{
    private readonly RabbitMqOpcoes _opcoes;
    private readonly ILogger<RabbitMqPublicadorRoteamento> _logger;
    private readonly object _trava = new();
    private IConnection? _conexao;
    private IModel? _canal;

    public RabbitMqPublicadorRoteamento(
        RabbitMqOpcoes opcoes, ILogger<RabbitMqPublicadorRoteamento> logger)
    {
        _opcoes = opcoes;
        _logger = logger;
    }

    public void Publicar(string routingKey, object evento, string correlationId)
    {
        try
        {
            var canal = ObterCanal();
            if (canal is null)
            {
                _logger.LogWarning(
                    "RabbitMQ indisponível; evento {RoutingKey} não publicado.", routingKey);
                return;
            }

            var props = canal.CreateBasicProperties();
            props.Persistent = true;
            props.CorrelationId = correlationId;
            props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            canal.BasicPublish(_opcoes.Exchange, routingKey, props,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evento)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao publicar {RoutingKey}.", routingKey);
        }
    }

    public void Dispose()
    {
        try { _canal?.Close(); } catch { /* ignore */ }
        try { _conexao?.Close(); } catch { /* ignore */ }
        _canal?.Dispose();
        _conexao?.Dispose();
    }

    private IModel? ObterCanal()
    {
        lock (_trava)
        {
            if (_canal?.IsOpen == true)
                return _canal;

            try
            {
                _canal?.Dispose();
                _conexao?.Dispose();
                _conexao = new ConnectionFactory
                {
                    HostName = _opcoes.Host,
                    Port = _opcoes.Port,
                    UserName = _opcoes.User,
                    Password = _opcoes.Pass,
                    AutomaticRecoveryEnabled = true
                }.CreateConnection("roteamento-api");
                _canal = _conexao.CreateModel();
                _canal.ExchangeDeclare(_opcoes.Exchange, ExchangeType.Topic, durable: true);
                return _canal;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sem conexão RabbitMQ.");
                return null;
            }
        }
    }
}
