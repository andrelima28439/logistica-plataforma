using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace Alerta.Api.Observabilidade;

public sealed class RabbitMqHealthCheck(
    string host, int port, string user, string pass) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = host,
                Port = port,
                UserName = user,
                Password = pass,
                RequestedHeartbeat = TimeSpan.FromSeconds(5)
            };
            using var conexao = factory.CreateConnection("alerta-api-healthcheck");
            return Task.FromResult(conexao.IsOpen
                ? HealthCheckResult.Healthy("RabbitMQ aceitando conexões AMQP.")
                : HealthCheckResult.Unhealthy("Conexão AMQP não abriu."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ inacessível.", ex));
        }
    }
}
