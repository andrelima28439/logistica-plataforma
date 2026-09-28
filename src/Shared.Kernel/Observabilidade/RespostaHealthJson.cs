using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Shared.Kernel.Observabilidade;

// Resposta JSON padronizada do /health (usada pelos 4 serviços e
// pelas probes do Kubernetes).
public static class RespostaHealthJson
{
    public static HealthCheckOptions Opcoes => new()
    {
        ResponseWriter = EscreverResposta
    };

    // Para o livenessProbe: só o check "self" (processo no ar), SEM
    // dependências — pod com dependência fora NÃO deve reiniciar em loop.
    public static HealthCheckOptions OpcoesApenas(params string[] nomes) => new()
    {
        Predicate = check => nomes.Contains(check.Name),
        ResponseWriter = EscreverResposta
    };

    private static async Task EscreverResposta(HttpContext contexto, HealthReport relatorio)
    {
        contexto.Response.ContentType = "application/json";
        var corpo = JsonSerializer.Serialize(new
        {
            status = relatorio.Status.ToString(),
            totalDuration = relatorio.TotalDuration.ToString(),
            checks = relatorio.Entries.Select(e => new
            {
                nome = e.Key,
                status = e.Value.Status.ToString(),
                descricao = e.Value.Description,
                duracao = e.Value.Duration.ToString()
            })
        });
        await contexto.Response.WriteAsync(corpo);
    }
}
