using Alerta.Api.Application;
using Alerta.Api.Canais;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alerta.Tests.Application;

public class ServicoAlertaTests
{
    private static (ServicoAlerta servico, OutboxEmail emails, OutboxSms sms) Montar()
    {
        var emails = new OutboxEmail(new List<string>());
        var sms = new OutboxSms(new List<string>());
        var fabrica = new FabricaAlerta(new AlertaPainel(),
            new AlertaEmailMock(emails), new AlertaSmsMock(sms));
        // Notificador best-effort apontando para lugar nenhum: falha de
        // conexão é engolida (warning) e não quebra o processamento.
        var notificador = new Alerta.Api.Infrastructure.NotificadorEntrega(
            new HttpClient { BaseAddress = new Uri("http://127.0.0.1:9"), Timeout = TimeSpan.FromSeconds(1) },
            NullLogger<Alerta.Api.Infrastructure.NotificadorEntrega>.Instance);
        var servico = new ServicoAlerta(new AlertaEmMemoriaRepositorio(), fabrica,
            notificador, NullLogger<ServicoAlerta>.Instance);
        return (servico, emails, sms);
    }

    [Fact]
    public async Task Desvio_critico_persiste_e_aciona_email_sms_painel()
    {
        var (servico, emails, sms) = Montar();

        var alerta = await servico.ProcessarDesvioAsync(Guid.NewGuid(),
            "saiu da rota", "CRITICA", "corr-9");

        Assert.Equal(new[] { "email", "painel", "sms" }, alerta.CanaisAcionados.OrderBy(n => n));
        Assert.Single(emails.Itens);
        Assert.Single(sms.Itens);
        Assert.Equal("corr-9", alerta.CorrelationId);
    }

    [Fact]
    public async Task Atraso_leve_persiste_e_aciona_so_painel()
    {
        var (servico, emails, sms) = Montar();

        var alerta = await servico.ProcessarAtrasoAsync(Guid.NewGuid(), 12, "BAIXA", "corr-10");

        Assert.Equal(new[] { "painel" }, alerta.CanaisAcionados);
        Assert.Empty(emails.Itens);
        Assert.Empty(sms.Itens);
        Assert.Contains("12 min", alerta.Mensagem);
    }
}
