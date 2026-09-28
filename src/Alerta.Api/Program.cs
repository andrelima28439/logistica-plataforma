using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Alerta.Api.Api;
using Alerta.Api.Application;
using Alerta.Api.Canais;
using Alerta.Api.Consumidores;
using Alerta.Api.Infrastructure;
using Alerta.Api.Observabilidade;
using Prometheus;
using Serilog;
using Serilog.Formatting.Json;
using Shared.Kernel.Observabilidade;

var builder = WebApplication.CreateBuilder(args);

var seqUrl = builder.Configuration["Observabilidade:Seq:Url"] ?? "http://localhost:5341";
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Servico", "alerta-api")
    .WriteTo.Console(new JsonFormatter())
    .WriteTo.Seq(seqUrl)
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var cfg = builder.Configuration;
var rabbitHost = cfg["Alerta:RabbitMq:Host"] ?? "localhost";
var rabbitPort = int.TryParse(cfg["Alerta:RabbitMq:Port"], out var porta) ? porta : 5672;
var rabbitUser = cfg["Alerta:RabbitMq:User"] ?? "logistica";
var rabbitPass = cfg["Alerta:RabbitMq:Pass"] ?? "logistica-dev-2026";
var rabbitOpcoes = new RabbitMqOpcoes(rabbitHost, rabbitPort, rabbitUser, rabbitPass,
    cfg["Alerta:RabbitMq:Exchange"] ?? "logistica.eventos");
var managementOpcoes = new ManagementOpcoes(
    cfg["Alerta:Management:BaseUrl"] ?? "http://localhost:15672",
    cfg["Alerta:Management:User"] ?? "logistica",
    cfg["Alerta:Management:Pass"] ?? "logistica-dev-2026");

builder.Services.AddSingleton<IAlertaRepositorio, AlertaEmMemoriaRepositorio>();
builder.Services.AddSingleton(new OutboxEmail(new List<string>()));
builder.Services.AddSingleton(new OutboxSms(new List<string>()));
builder.Services.AddSingleton<AlertaPainel>();
builder.Services.AddSingleton<AlertaEmailMock>();
builder.Services.AddSingleton<AlertaSmsMock>();
builder.Services.AddSingleton<FabricaAlerta>();
builder.Services.AddSingleton(sp => new NotificadorEntrega(
    new HttpClient
    {
        BaseAddress = new Uri(cfg["Alerta:Entrega:BaseUrl"] ?? "http://localhost:5001"),
        Timeout = TimeSpan.FromSeconds(3)
    },
    sp.GetRequiredService<ILogger<NotificadorEntrega>>()));
builder.Services.AddSingleton<ServicoAlerta>();
builder.Services.AddSingleton(rabbitOpcoes);
builder.Services.AddSingleton(new ConexaoRabbitOpcoes(
    rabbitOpcoes.Host, rabbitOpcoes.Port, rabbitOpcoes.User,
    rabbitOpcoes.Pass, rabbitOpcoes.Exchange));
builder.Services.AddSingleton<PublicadorCruRabbitMq>();
builder.Services.AddSingleton(ManagementHttpClient.Criar(managementOpcoes));
builder.Services.AddSingleton<InspetorDlq>();
builder.Services.AddHostedService<ConsumidorAlertasWorker>();
builder.Services.AddHostedService<DlqMetricasWorker>();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Processo no ar."))
    .AddCheck("rabbitmq", new RabbitMqHealthCheck(rabbitHost, rabbitPort, rabbitUser, rabbitPass));
// Dev-only: libera o frontend Angular local (ver ADR-028).
builder.Services.AddCors(o => o.AddPolicy("frontend-dev", p => p
    .WithOrigins("http://localhost:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseCors("frontend-dev");
app.UseCorrelationId();
app.UseSerilogRequestLogging();
app.UseHttpMetrics();
app.MapHealthChecks("/health", RespostaHealthJson.Opcoes);
app.MapHealthChecks("/health/live", RespostaHealthJson.OpcoesApenas("self"));
app.MapMetrics();
app.MapAlertas();

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
