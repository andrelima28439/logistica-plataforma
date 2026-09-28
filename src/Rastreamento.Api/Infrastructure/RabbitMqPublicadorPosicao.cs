using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using Rastreamento.Api.Application;
using Rastreamento.Api.Domain;
using Shared.Kernel;

namespace Rastreamento.Api.Infrastructure;

public sealed record RabbitMqOpcoes(
    string Host,
    int Port,
    string User,
    string Pass,
    string Exchange);

// Publica posicao.atualizada no exchange "logistica.eventos" (topic,
// durável). Resiliente: se o broker estiver fora, registra warning e
// segue — a posição já está no histórico e no cache.
public sealed class RabbitMqPublicadorPosicao : IPublicadorPosicao, IDisposable
{
    private const string RoutingKey = "posicao.atualizada";

    private readonly RabbitMqOpcoes _opcoes;
    private readonly ILogger<RabbitMqPublicadorPosicao> _logger;
    private readonly object _trava = new();
    private IConnection? _conexao;
    private IModel? _canal;

    public RabbitMqPublicadorPosicao(RabbitMqOpcoes opcoes, ILogger<RabbitMqPublicadorPosicao> logger)
    {
        _opcoes = opcoes;
        _logger = logger;
    }

    public Task<bool> PublicarAsync(Posicao posicao, string correlationId, CancellationToken ct = default)
    {
        try
        {
            var canal = ObterCanal();
            if (canal is null)
            {
                _logger.LogWarning(
                    "RabbitMQ indisponível; evento posicao.atualizada da entrega {EntregaId} não publicado",
                    posicao.EntregaId);
                return Task.FromResult(false);
            }

            var evento = new PosicaoAtualizadaEvent(
                Guid.NewGuid(), DateTimeOffset.UtcNow, correlationId,
                posicao.EntregaId, posicao.Latitude, posicao.Longitude, posicao.CapturadaEm);

            var props = canal.CreateBasicProperties();
            props.Persistent = true;
            props.CorrelationId = correlationId;
            props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            canal.BasicPublish(
                _opcoes.Exchange, RoutingKey, props,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evento)));

            _logger.LogDebug("posicao.atualizada publicado entrega {EntregaId} correlacao {Correlacao}",
                posicao.EntregaId, correlationId);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao publicar posicao.atualizada da entrega {EntregaId}",
                posicao.EntregaId);
            return Task.FromResult(false);
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
                _conexao?.Dispose();
                var factory = new ConnectionFactory
                {
                    HostName = _opcoes.Host,
                    Port = _opcoes.Port,
                    UserName = _opcoes.User,
                    Password = _opcoes.Pass,
                    AutomaticRecoveryEnabled = true,
                    RequestedHeartbeat = TimeSpan.FromSeconds(30)
                };
                _conexao = factory.CreateConnection("rastreamento-api");
                _canal = _conexao.CreateModel();
                _canal.ExchangeDeclare(_opcoes.Exchange, ExchangeType.Topic, durable: true);
                return _canal;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não foi possível conectar ao RabbitMQ em {Host}:{Port}",
                    _opcoes.Host, _opcoes.Port);
                return null;
            }
        }
    }
}
