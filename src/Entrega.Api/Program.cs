using System.Text.Json.Serialization;
using Entrega.Api.Api;
using Entrega.Api.Application;
using Entrega.Api.Infrastructure;
using Entrega.Api.Observabilidade;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;
using Serilog;
using Serilog.Formatting.Json;
using Shared.Kernel.Observabilidade;

var builder = WebApplication.CreateBuilder(args);

var seqUrl = builder.Configuration["Observabilidade:Seq:Url"] ?? "http://localhost:5341";
var sqlConnection = builder.Configuration["Entrega:Sql:ConnectionString"]
    ?? "Server=localhost,1433;Database=master;User Id=sa;Password=Logistica@2026!Dev;TrustServerCertificate=True;Connect Timeout=3";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Servico", "entrega-api")
    .WriteTo.Console(new JsonFormatter())
    .WriteTo.Seq(seqUrl)
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR();
builder.Services.AddSingleton<IEntregaRepositorio, EntregaEmMemoriaRepositorio>();
// Dev-only: libera o frontend Angular local (http://localhost:4200).
// Em produção, restringir às origens reais — ver ADR-028.
builder.Services.AddCors(o => o.AddPolicy("frontend-dev", p => p
    .WithOrigins("http://localhost:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Processo no ar."))
    .AddCheck("sqlserver", new SqlServerHealthCheck(sqlConnection));

var app = builder.Build();

app.UseCors("frontend-dev");
app.UseCorrelationId();
app.UseSerilogRequestLogging();
app.UseHttpMetrics();
app.MapHealthChecks("/health", RespostaHealthJson.Opcoes);
app.MapHealthChecks("/health/live", RespostaHealthJson.OpcoesApenas("self"));
app.MapMetrics();
app.MapEntregas();
app.MapHub<EntregasHub>("/entregas/stream");

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

// Necessário para WebApplicationFactory<Program> nos testes de API.
public partial class Program { }
