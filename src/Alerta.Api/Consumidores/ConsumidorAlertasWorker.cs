using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Alerta.Api.Application;
using Alerta.Api.Observabilidade;
using Serilog.Context;

namespace Alerta.Api.Consumidores;

public sealed record RabbitMqOpcoes(
    string Host,
    int Port,
    string User,
    string Pass,
    string Exchange);

// Consome desvio.detectado + atraso.detectado com DLQ:
// fila "alerta.eventos" declara dead-letter-exchange "logistica.dlx";
// nack sem requeue => mensagem cai em "alerta.eventos.dlq".
// Sem broker no boot: tenta reconectar a cada 5s, sem derrubar a API.
public sealed class ConsumidorAlertasWorker : BackgroundService
{
    public const string Exchange = "logistica.eventos";
    public const string Fila = "alerta.eventos";
    public const string Dlx = "logistica.dlx";
    public const string FilaDlq = "alerta.eventos.dlq";

    private readonly IServiceProvider _services;
    private readonly RabbitMqOpcoes _opcoes;
    private readonly ILogger<ConsumidorAlertasWorker> _logger;

    public ConsumidorAlertasWorker(
        IServiceProvider services,
        RabbitMqOpcoes opcoes,
        ILogger<ConsumidorAlertasWorker> logger)
    {
        _services = services;
        _opcoes = opcoes;
        _logger = logger;
    }

    public static void DeclararTopologia(IModel canal)
    {
        canal.ExchangeDeclare(Exchange, ExchangeType.Topic, durable: true);
        canal.ExchangeDeclare(Dlx, ExchangeType.Direct, durable: true);
        canal.QueueDeclare(FilaDlq, durable: true, exclusive: false, autoDelete: false);
        canal.QueueBind(FilaDlq, Dlx, "alerta.dlq");
        var args = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = Dlx,
            ["x-dead-letter-routing-key"] = "alerta.dlq"
        };
        canal.QueueDeclare(Fila, durable: true, exclusive: false, autoDelete: false, args);
        canal.QueueBind(Fila, Exchange, "desvio.detectado");
        canal.QueueBind(Fila, Exchange, "atraso.detectado");
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
                _logger.LogWarning(ex, "RabbitMQ indisponível para consumo; retry em 5s.");
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

        using var conexao = factory.CreateConnection("alerta-api-consumer");
        using var canal = conexao.CreateModel();
        DeclararTopologia(canal);
        canal.BasicQos(0, 10, false);

        var consumidor = new EventingBasicConsumer(canal);
        consumidor.Received += async (_, ea) =>
        {
            var (decisao, mensagem) = ProcessadorMensagem.Avaliar(
                ea.RoutingKey, ea.Body.ToArray());

            if (decisao == DecisaoMensagem.NackSemRequeue || mensagem is null)
            {
                MetricasAlerta.FalhasProcessamento.Inc();
                _logger.LogWarning(
                    "Mensagem inválida (routing={Routing}); nack sem requeue => DLQ.",
                    ea.RoutingKey);
                canal.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            try
            {
                using (LogContext.PushProperty("CorrelationId", mensagem.CorrelationId))
                {
                    using var scope = _services.CreateScope();
                    var servico = scope.ServiceProvider.GetRequiredService<ServicoAlerta>();
                    if (mensagem.RoutingKey == "desvio.detectado")
                        await servico.ProcessarDesvioAsync(mensagem.EntregaId,
                            mensagem.MotivoOuAtraso, mensagem.Severidade,
                            mensagem.CorrelationId, ct);
                    else
                        await servico.ProcessarAtrasoAsync(mensagem.EntregaId,
                            mensagem.AtrasoEstimadoMinutos, mensagem.Severidade,
                            mensagem.CorrelationId, ct);
                }

                canal.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                MetricasAlerta.FalhasProcessamento.Inc();
                _logger.LogError(ex, "Falha ao processar alerta; nack sem requeue => DLQ.");
                canal.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        canal.BasicConsume(Fila, autoAck: false, consumidor);
        _logger.LogInformation("Consumindo desvio/atraso na fila {Fila} (com DLQ).", Fila);

        await Task.Delay(Timeout.Infinite, ct);
    }
}
