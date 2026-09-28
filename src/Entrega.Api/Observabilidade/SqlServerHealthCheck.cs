using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Entrega.Api.Observabilidade;

// Verifica o SQL Server DE VERDADE (abre conexão e roda SELECT 1) —
// o container SQL Server já está provisionado no Compose; a persistência EF Core é evolução futura sem mudar este check.
public sealed class SqlServerHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var conexao = new SqlConnection(connectionString);
            await conexao.OpenAsync(ct);
            using var comando = new SqlCommand("SELECT 1", conexao);
            await comando.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy("SQL Server respondendo (SELECT 1).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQL Server inacessível.", ex);
        }
    }
}
