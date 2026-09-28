namespace Roteamento.Api.Domain;

// Cópia local por bounded context (cada serviço é dono do seu modelo;
// só eventos de integração são compartilhados via Shared.Kernel) — ADR-012.
public enum TipoCarga
{
    PADRAO,
    REFRIGERADA,
    FRAGIL
}
