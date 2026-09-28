using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Roteamento.Api.Application;
using Serilog.Context;
using Shared.Kernel;

namespace Roteamento.Api.Consumidores;

// Consome posicao.atualizada e dispara a análise.
// Não derruba a API se o broker estiver fora: tenta reconectar a cada 5s.
public sealed class ConsumidorPosicoesWorker : BackgroundService
{
    private const string Exchange = "logistica.eventos";
    private const string Fila = "roteamento.posicoes";
    private const string RoutingKey = "posicao.atualizada";

    private readonly IServiceProvider _services;
    private readonly Infrastructure.RabbitMqOpcoes _opcoes;
    private readonly ILogger<ConsumidorPosicoesWorker> _logger;

    public ConsumidorPosicoesWorker(
        IServiceProvider services,
        Infrastructure.RabbitMqOpcoes opcoes,
        ILogger<ConsumidorPosicoesWorker> logger)
    {
        _services = services;
        _opcoes = opcoes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumirAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "RabbitMQ indisponível para consumo; nova tentativa em 5s.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ConsumirAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = _opcoes.Host,
            Port = _opcoes.Port,
            UserName = _opcoes.User,
            Password = _opcoes.Pass,
            AutomaticRecoveryEnabled = true
        };

        using var conexao = factory.CreateConnection("roteamento-api-consumer");
        using var canal = conexao.CreateModel();
        canal.ExchangeDeclare(Exchange, ExchangeType.Topic, durable: true);
        canal.QueueDeclare(Fila, durable: true, exclusive: false, autoDelete: false);
        canal.QueueBind(Fila, Exchange, RoutingKey);
        canal.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

        var consumidor = new EventingBasicConsumer(canal);
        consumidor.Received += async (_, ea) =>
        {
            string? correlationId = ea.BasicProperties?.CorrelationId;
            try
            {
                var evento = JsonSerializer.Deserialize<PosicaoAtualizadaEvent>(
                    Encoding.UTF8.GetString(ea.Body.ToArray()));
                if (evento is not null)
                {
                    // Correlation-id do evento entra no LogContext: todos os
                    // logs da análise saem com a mesma CorrelationId (Seq).
                    using (LogContext.PushProperty("CorrelationId",
                        correlationId ?? evento.CorrelationId))
                    {
                        using var scope = _services.CreateScope();
                        var servico = scope.ServiceProvider
                            .GetRequiredService<ServicoRoteamento>();
                        await servico.AnalisarAsync(evento.EntregaId,
                            correlationId ?? evento.CorrelationId, ct);
                    }
                }
                canal.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao processar posicao.atualizada; descartado.");
                canal.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        canal.BasicConsume(Fila, autoAck: false, consumidor);
        _logger.LogInformation("Consumindo posicao.atualizada na fila {Fila}.", Fila);

        await Task.Delay(Timeout.Infinite, ct);
    }
}
