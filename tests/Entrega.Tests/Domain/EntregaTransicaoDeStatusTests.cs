using Entrega.Api.Domain;

namespace Entrega.Tests.Domain;

// TDD (RED): estes testes foram escritos ANTES da entidade Entrega existir.
// Regras de transição de status.
public class EntregaTransicaoDeStatusTests
{
    private static Entrega.Api.Domain.Entrega NovaEntrega() =>
        Entrega.Api.Domain.Entrega.Criar(
            origem: "São Paulo/SP",
            destino: "Rio de Janeiro/RJ",
            transportadorId: "transp-1",
            tipoCarga: TipoCarga.PADRAO,
            prazoEstimado: DateTimeOffset.UtcNow.AddDays(2));

    [Fact]
    public void Criar_entrega_nasce_com_status_CRIADA_e_evento_EntregaCriada()
    {
        var entrega = NovaEntrega();

        Assert.Equal(StatusEntrega.CRIADA, entrega.Status);
        Assert.NotEqual(Guid.Empty, entrega.Id);
        var criado = Assert.Single(entrega.Eventos);
        var evento = Assert.IsType<EntregaCriada>(criado);
        Assert.Equal(entrega.Id, evento.EntregaId);
    }

    [Fact]
    public void CRIADA_pode_ir_para_EM_TRANSITO_e_registra_StatusAlterado()
    {
        var entrega = NovaEntrega();

        entrega.TransitarPara(StatusEntrega.EM_TRANSITO);

        Assert.Equal(StatusEntrega.EM_TRANSITO, entrega.Status);
        Assert.Equal(2, entrega.Eventos.Count);
        var alterado = Assert.IsType<StatusAlterado>(entrega.Eventos.Last());
        Assert.Equal(StatusEntrega.CRIADA, alterado.De);
        Assert.Equal(StatusEntrega.EM_TRANSITO, alterado.Para);
    }

    [Theory]
    [InlineData(StatusEntrega.ENTREGUE)]
    [InlineData(StatusEntrega.ATRASADA)]
    [InlineData(StatusEntrega.DESVIO_DETECTADO)]
    public void CRIADA_nao_pode_pular_direto_para_status_avancado(StatusEntrega destino)
    {
        var entrega = NovaEntrega();

        Assert.Throws<TransicaoDeStatusInvalidaException>(() => entrega.TransitarPara(destino));
        Assert.Equal(StatusEntrega.CRIADA, entrega.Status); // estado preservado
    }

    [Theory]
    [InlineData(StatusEntrega.ENTREGUE)]
    [InlineData(StatusEntrega.ATRASADA)]
    [InlineData(StatusEntrega.DESVIO_DETECTADO)]
    public void EM_TRANSITO_pode_ir_para_fim_ou_excecao_de_rota(StatusEntrega destino)
    {
        var entrega = NovaEntrega();
        entrega.TransitarPara(StatusEntrega.EM_TRANSITO);

        entrega.TransitarPara(destino);

        Assert.Equal(destino, entrega.Status);
    }

    [Fact]
    public void EM_TRANSITO_nao_pode_voltar_para_CRIADA()
    {
        var entrega = NovaEntrega();
        entrega.TransitarPara(StatusEntrega.EM_TRANSITO);

        Assert.Throws<TransicaoDeStatusInvalidaException>(() => entrega.TransitarPara(StatusEntrega.CRIADA));
    }

    [Fact]
    public void DESVIO_DETECTADO_pode_retomar_para_EM_TRANSITO()
    {
        var entrega = NovaEntrega();
        entrega.TransitarPara(StatusEntrega.EM_TRANSITO);
        entrega.TransitarPara(StatusEntrega.DESVIO_DETECTADO);

        entrega.TransitarPara(StatusEntrega.EM_TRANSITO);

        Assert.Equal(StatusEntrega.EM_TRANSITO, entrega.Status);
    }

    [Fact]
    public void ATRASADA_pode_ser_finalizada_com_ENTREGUE()
    {
        var entrega = NovaEntrega();
        entrega.TransitarPara(StatusEntrega.EM_TRANSITO);
        entrega.TransitarPara(StatusEntrega.ATRASADA);

        entrega.TransitarPara(StatusEntrega.ENTREGUE);

        Assert.Equal(StatusEntrega.ENTREGUE, entrega.Status);
    }

    [Theory]
    [InlineData(StatusEntrega.CRIADA)]
    [InlineData(StatusEntrega.EM_TRANSITO)]
    [InlineData(StatusEntrega.ATRASADA)]
    [InlineData(StatusEntrega.DESVIO_DETECTADO)]
    public void ENTREGUE_e_terminal_nenhuma_transicao_e_permitida(StatusEntrega destino)
    {
        var entrega = NovaEntrega();
        entrega.TransitarPara(StatusEntrega.EM_TRANSITO);
        entrega.TransitarPara(StatusEntrega.ENTREGUE);

        Assert.Throws<TransicaoDeStatusInvalidaException>(() => entrega.TransitarPara(destino));
        Assert.Equal(StatusEntrega.ENTREGUE, entrega.Status);
    }

    [Fact]
    public void Transicao_para_o_mesmo_status_e_invalida()
    {
        var entrega = NovaEntrega();

        Assert.Throws<TransicaoDeStatusInvalidaException>(() => entrega.TransitarPara(StatusEntrega.CRIADA));
    }
}
