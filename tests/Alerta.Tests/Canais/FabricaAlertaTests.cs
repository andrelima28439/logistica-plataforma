using Alerta.Api.Canais;
using Alerta.Api.Domain;

namespace Alerta.Tests.Canais;

// Prova isolada (mesmo estilo do teste de Strategy):
// cada combinação tipo × severidade resolve a combinação certa de canais.
public class FabricaAlertaTests
{
    private static FabricaAlerta NovaFabrica() => new(
        new AlertaPainel(),
        new AlertaEmailMock(new OutboxEmail(new List<string>())),
        new AlertaSmsMock(new OutboxSms(new List<string>())));

    private static string[] CanaisDe(TipoEventoAlerta tipo, string severidade) =>
        NovaFabrica().Criar(tipo, severidade).Select(c => c.Nome).OrderBy(n => n).ToArray();

    [Fact]
    public void Desvio_critico_dispara_email_sms_e_painel() =>
        Assert.Equal(new[] { "email", "painel", "sms" },
            CanaisDe(TipoEventoAlerta.DESVIO, "CRITICA"));

    [Fact]
    public void Desvio_alto_dispara_email_e_painel() =>
        Assert.Equal(new[] { "email", "painel" },
            CanaisDe(TipoEventoAlerta.DESVIO, "ALTA"));

    [Fact]
    public void Desvio_medio_dispara_so_painel() =>
        Assert.Equal(new[] { "painel" },
            CanaisDe(TipoEventoAlerta.DESVIO, "MEDIA"));

    [Fact]
    public void Atraso_critico_dispara_email_e_painel() =>
        Assert.Equal(new[] { "email", "painel" },
            CanaisDe(TipoEventoAlerta.ATRASO, "CRITICA"));

    [Fact]
    public void Atraso_leve_dispara_so_painel() =>
        Assert.Equal(new[] { "painel" },
            CanaisDe(TipoEventoAlerta.ATRASO, "BAIXA"));

    [Fact]
    public void Severidade_e_case_insensitive() =>
        Assert.Equal(new[] { "email", "painel", "sms" },
            CanaisDe(TipoEventoAlerta.DESVIO, "critica"));
}
