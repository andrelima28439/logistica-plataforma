namespace Entrega.Api.Domain;

// Entidade Entrega. A matriz de transições válidas
// é a regra de negócio central coberta por testes de domínio.
public sealed class Entrega
{
    private static readonly IReadOnlyDictionary<StatusEntrega, IReadOnlySet<StatusEntrega>> TransicoesValidas =
        new Dictionary<StatusEntrega, IReadOnlySet<StatusEntrega>>
        {
            [StatusEntrega.CRIADA] = new HashSet<StatusEntrega> { StatusEntrega.EM_TRANSITO },
            [StatusEntrega.EM_TRANSITO] = new HashSet<StatusEntrega>
            {
                StatusEntrega.ENTREGUE, StatusEntrega.ATRASADA, StatusEntrega.DESVIO_DETECTADO
            },
            [StatusEntrega.DESVIO_DETECTADO] = new HashSet<StatusEntrega>
            {
                StatusEntrega.EM_TRANSITO, StatusEntrega.ATRASADA
            },
            [StatusEntrega.ATRASADA] = new HashSet<StatusEntrega>
            {
                StatusEntrega.EM_TRANSITO, StatusEntrega.ENTREGUE
            },
            [StatusEntrega.ENTREGUE] = new HashSet<StatusEntrega>(), // terminal
        };

    private readonly List<EntregaEvento> _eventos = new();

    private Entrega() { } // para futuros mapeadores (EF)

    public Guid Id { get; private set; }
    public string Origem { get; private set; } = string.Empty;
    public string Destino { get; private set; } = string.Empty;
    public string TransportadorId { get; private set; } = string.Empty;
    public TipoCarga TipoCarga { get; private set; }
    public StatusEntrega Status { get; private set; }
    public DateTimeOffset DataCriacao { get; private set; }
    public DateTimeOffset PrazoEstimado { get; private set; }
    public IReadOnlyList<EntregaEvento> Eventos => _eventos.AsReadOnly();

    public static Entrega Criar(
        string origem,
        string destino,
        string transportadorId,
        TipoCarga tipoCarga,
        DateTimeOffset prazoEstimado)
    {
        if (string.IsNullOrWhiteSpace(origem))
            throw new ArgumentException("Origem é obrigatória.", nameof(origem));
        if (string.IsNullOrWhiteSpace(destino))
            throw new ArgumentException("Destino é obrigatório.", nameof(destino));
        if (string.IsNullOrWhiteSpace(transportadorId))
            throw new ArgumentException("Transportador é obrigatório.", nameof(transportadorId));

        var agora = DateTimeOffset.UtcNow;
        var entrega = new Entrega
        {
            Id = Guid.NewGuid(),
            Origem = origem,
            Destino = destino,
            TransportadorId = transportadorId,
            TipoCarga = tipoCarga,
            Status = StatusEntrega.CRIADA,
            DataCriacao = agora,
            PrazoEstimado = prazoEstimado
        };
        entrega._eventos.Add(new EntregaCriada(entrega.Id, agora));
        return entrega;
    }

    public void TransitarPara(StatusEntrega novoStatus)
    {
        if (!TransicoesValidas.TryGetValue(Status, out var destinos) || !destinos.Contains(novoStatus))
            throw new TransicaoDeStatusInvalidaException(Status, novoStatus);

        var anterior = Status;
        Status = novoStatus;
        _eventos.Add(new StatusAlterado(Id, anterior, novoStatus, DateTimeOffset.UtcNow));
    }
}
