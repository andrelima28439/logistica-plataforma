using System.Text;
using System.Text.Json;
using Shared.Kernel;

namespace Alerta.Tests.Consumidores;

// Lógica de decisão do consumer (sem broker): válido => ack,
// inválido/poison => nack sem requeue (=> DLQ no broker de verdade).
public class ProcessadorMensagemTests
{
    [Fact]
    public void Desvio_valido_da_ack()
    {
        var corpo = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new DesvioDetectadoEvent(Guid.NewGuid(), DateTimeOffset.UtcNow,
                "corr-1", Guid.NewGuid(), "saiu da rota", "CRITICA")));

        var (decisao, mensagem) = Alerta.Api.Consumidores.ProcessadorMensagem
            .Avaliar("desvio.detectado", corpo);

        Assert.Equal(Alerta.Api.Consumidores.DecisaoMensagem.Ack, decisao);
        Assert.NotNull(mensagem);
        Assert.Equal("corr-1", mensagem!.CorrelationId);
    }

    [Fact]
    public void Atraso_valido_da_ack()
    {
        var corpo = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new AtrasoDetectadoEvent(Guid.NewGuid(), DateTimeOffset.UtcNow,
                "corr-2", Guid.NewGuid(), 25, "BAIXA")));

        var (decisao, mensagem) = Alerta.Api.Consumidores.ProcessadorMensagem
            .Avaliar("atraso.detectado", corpo);

        Assert.Equal(Alerta.Api.Consumidores.DecisaoMensagem.Ack, decisao);
        Assert.Equal(25, mensagem!.AtrasoEstimadoMinutos);
    }

    [Fact]
    public void Poison_message_da_nack_sem_requeue()
    {
        var (decisao, mensagem) = Alerta.Api.Consumidores.ProcessadorMensagem
            .Avaliar("desvio.detectado",
                "{isto-nao-e-json-valido-"u8.ToArray());

        Assert.Equal(Alerta.Api.Consumidores.DecisaoMensagem.NackSemRequeue, decisao);
        Assert.Null(mensagem);
    }

    [Fact]
    public void Routing_key_desconhecida_da_nack_sem_requeue()
    {
        var (decisao, _) = Alerta.Api.Consumidores.ProcessadorMensagem
            .Avaliar("evento.que-nao-existe", "{}"u8.ToArray());

        Assert.Equal(Alerta.Api.Consumidores.DecisaoMensagem.NackSemRequeue, decisao);
    }

    [Fact]
    public void Evento_com_tipo_trocado_da_nack_sem_requeue()
    {
        // Corpo de ATRASO com routing de DESVIO: desserializa com EntregaId vazio.
        var corpo = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new AtrasoDetectadoEvent(Guid.NewGuid(), DateTimeOffset.UtcNow,
                "corr-3", Guid.Empty, 10, "BAIXA")));

        var (decisao, _) = Alerta.Api.Consumidores.ProcessadorMensagem
            .Avaliar("desvio.detectado", corpo);

        Assert.Equal(Alerta.Api.Consumidores.DecisaoMensagem.NackSemRequeue, decisao);
    }
}
