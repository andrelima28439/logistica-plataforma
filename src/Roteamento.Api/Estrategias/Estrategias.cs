using Roteamento.Api.Domain;

namespace Roteamento.Api.Estrategias;

// Lógica compartilhada: distância simplificada ponto–rota (menor distância
// a qualquer waypoint; propositalmente simples — ADR-013) e idade da
// última posição. Cada estratégia concreta só define os limiares.
public abstract class EstrategiaBase : IEstrategiaAlerta
{
    public abstract TipoCarga TipoCarga { get; }

    // Desvio máximo tolerado, em graus decimais (~111 km por grau).
    protected abstract double LimiarDesvioGraus { get; }

    // Inatividade máxima (sem posição nova) antes de configurar atraso.
    protected abstract TimeSpan ToleranciaInatividade { get; }

    protected abstract string SeveridadeDesvio { get; }
    protected abstract string SeveridadeAtraso { get; }

    public ResultadoAnalise Analisar(ContextoRota contexto)
    {
        if (contexto.Posicoes.Count == 0)
            return new ResultadoAnalise(TipoResultado.NENHUM, "INFO",
                "Sem posições para analisar.");

        var ultima = contexto.Posicoes[^1];
        var desvio = MenorDistanciaGraus(ultima, contexto.RotaEsperada);

        if (desvio > LimiarDesvioGraus)
            return new ResultadoAnalise(TipoResultado.DESVIO, SeveridadeDesvio,
                $"Última posição a {desvio:F3}° da rota esperada (limiar {LimiarDesvioGraus:F3}°).");

        if (ultima.Quando.HasValue
            && contexto.Agora - ultima.Quando.Value > ToleranciaInatividade)
            return new ResultadoAnalise(TipoResultado.ATRASO, SeveridadeAtraso,
                $"Sem posição nova há {(contexto.Agora - ultima.Quando.Value).TotalMinutes:F0} min (tolerância {ToleranciaInatividade.TotalMinutes:F0} min).");

        return new ResultadoAnalise(TipoResultado.NENHUM, "INFO", "Rota e cadência normais.");
    }

    private static double MenorDistanciaGraus(PontoRota ponto, IReadOnlyList<PontoRota> rota)
    {
        if (rota.Count == 0)
            return 0; // sem rota esperada cadastrada: não há como acusar desvio

        var menor = double.MaxValue;
        foreach (var wp in rota)
        {
            var dLat = ponto.Latitude - wp.Latitude;
            var dLon = ponto.Longitude - wp.Longitude;
            var d = Math.Sqrt(dLat * dLat + dLon * dLon);
            if (d < menor)
                menor = d;
        }
        return menor;
    }
}

// Carga refrigerada: regra mais rígida (cadeia do frio não espera).
public sealed class EstrategiaCargaRefrigerada : EstrategiaBase
{
    public override TipoCarga TipoCarga => TipoCarga.REFRIGERADA;
    protected override double LimiarDesvioGraus => 0.02; // ~2,2 km
    protected override TimeSpan ToleranciaInatividade => TimeSpan.FromMinutes(3);
    protected override string SeveridadeDesvio => "CRITICA";
    protected override string SeveridadeAtraso => "CRITICA";
}

public sealed class EstrategiaCargaFragil : EstrategiaBase
{
    public override TipoCarga TipoCarga => TipoCarga.FRAGIL;
    protected override double LimiarDesvioGraus => 0.03; // ~3,3 km
    protected override TimeSpan ToleranciaInatividade => TimeSpan.FromMinutes(10);
    protected override string SeveridadeDesvio => "ALTA";
    protected override string SeveridadeAtraso => "MEDIA";
}

public sealed class EstrategiaCargaPadrao : EstrategiaBase
{
    public override TipoCarga TipoCarga => TipoCarga.PADRAO;
    protected override double LimiarDesvioGraus => 0.05; // ~5,5 km
    protected override TimeSpan ToleranciaInatividade => TimeSpan.FromMinutes(15);
    protected override string SeveridadeDesvio => "MEDIA";
    protected override string SeveridadeAtraso => "BAIXA";
}
