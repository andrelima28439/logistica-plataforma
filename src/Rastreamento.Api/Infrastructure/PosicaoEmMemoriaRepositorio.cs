using System.Collections.Concurrent;
using Rastreamento.Api.Application;
using Rastreamento.Api.Domain;

namespace Rastreamento.Api.Infrastructure;

public sealed class PosicaoEmMemoriaRepositorio : IPosicaoRepositorio
{
    private readonly ConcurrentDictionary<Guid, List<Posicao>> _historico = new();

    public void Registrar(Posicao posicao)
    {
        var lista = _historico.GetOrAdd(posicao.EntregaId, _ => new List<Posicao>());
        lock (lista)
        {
            lista.Add(posicao);
        }
    }

    public IReadOnlyList<Posicao> Listar(Guid entregaId) =>
        _historico.TryGetValue(entregaId, out var lista)
            ? lockAndCopy(lista)
            : Array.Empty<Posicao>();

    public Posicao? Ultima(Guid entregaId)
    {
        var lista = Listar(entregaId);
        return lista.Count == 0 ? null : lista[^1];
    }

    private static IReadOnlyList<Posicao> lockAndCopy(List<Posicao> lista)
    {
        lock (lista)
        {
            return lista.OrderBy(p => p.CapturadaEm).ToList();
        }
    }
}
