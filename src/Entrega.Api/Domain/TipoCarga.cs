namespace Entrega.Api.Domain;

// Nomes em maiúsculas (fora da convenção PascalCase do C#) para que a
// serialização JSON exponha exatamente PADRAO/REFRIGERADA/FRAGIL — ver ADR-005.
public enum TipoCarga
{
    PADRAO,
    REFRIGERADA,
    FRAGIL
}
