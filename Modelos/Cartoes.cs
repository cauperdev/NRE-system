// ============================================================================
// CARTÃO DE OS (celular e tablet) — dados que o desenho Pages/Shared/_CartaoOs.cshtml recebe.
// Usado na lista de Ordens de serviço e na lista do dia do Calendário.
// ============================================================================
namespace MS.Modelos;

// Cartão de OS das telas de celular e tablet (lista de OS e OS do dia no calendário).
// Selecao: classe da caixinha de marcar (ex.: "sel-os" para Remover selecionados); vazio = cartão sem caixinha.
public record CartaoOs(OrdemServico Os, string Href, DateTime Agora, bool Destaque = false, string? Selecao = null);
