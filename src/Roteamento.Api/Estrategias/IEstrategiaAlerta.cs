using Roteamento.Api.Domain;

namespace Roteamento.Api.Estrategias;

// Strategy Pattern: uma implementação por tipo de carga,
// selecionada pelo ResolvedorEstrategia.
public interface IEstrategiaAlerta
{
    TipoCarga TipoCarga { get; }
    ResultadoAnalise Analisar(ContextoRota contexto);
}
