// ============================================================================
// ÁREA ESCOLHIDA — o cliente (área) com que se está trabalhando (pedido do usuário em 13/10/2026).
// Depois do login vem a tela dos cartões dos clientes (Pages/Conta/Area): o cartão clicado fica guardado num cookie
// ("ms-area", até fechar o navegador) e as telas com filtro de Área já abrem filtradas por ele:
// Dashboard, Ordens de serviço, Cronograma, Equipamentos, Finalização, NRE e Calendário (IFiltroArea).
// Desde 13/10/2026 as telas NÃO têm mais o filtro "Área" (pedido do usuário: os cartões são o filtro). Usuários não é
// filtrada (usuário sem área sumiria). Não há cartão "Todas as áreas" (tirado a pedido).
//   - Abrir a tela sem CelulaId na URL (menu, filtros, link) → usa a área escolhida.
//   - Link com CelulaId (paginação, Extração) → mantém e grava a mesma área.
//   - Item "Escolher área" no alto do menu (Shared/_Layout) → volta para a tela dos cartões.
//   - Fora das listas (MsPage.AreaId): o campo Equipamento do Cronograma e da OS, as listas de Localização/Prédio,
//     os tipos de máquina (NRE, Spare Parts) e o sino só mostram o que é da área escolhida; máquina e NRE novas já
//     nascem nela. Abrir um registro pelo link (QR code, e-mail) continua funcionando em qualquer área.
//   - Spare Parts: cada área tem as suas peças (SparePart.CelulaId) — a tela só mexe nas da área escolhida.
// ============================================================================
namespace MS.Servicos;

// Tela com filtro de Área (propriedade CelulaId ligada à URL): recebe a área escolhida (MsPage.OnPageHandlerExecuting).
public interface IFiltroArea
{
    int? CelulaId { get; set; }
}

public static class AreaEscolhida
{
    const string Cookie = "nre-area";   // nome próprio: o MS (outra porta do mesmo PC) usa "ms-area"

    // null = ainda não escolheu (vai para a tela dos cartões) · 0 = todas as áreas · id = a área.
    public static int? Ler(HttpRequest r) => int.TryParse(r.Cookies[Cookie], out var id) && id >= 0 ? id : null;

    // A área em que se está trabalhando, para filtrar o que não é lista com filtro (cadastros, listas de escolha, sino):
    // id da área ou null (ainda não escolheu) — pedido do usuário em 13/10/2026: nada de uma área aparece em outra.
    public static int? Area(HttpRequest r) => Ler(r) is int id && id > 0 ? id : null;

    public static void Gravar(HttpResponse r, int? celulaId) =>
        r.Cookies.Append(Cookie, (celulaId ?? 0).ToString(), new CookieOptions
        {
            HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Lax, Secure = r.HttpContext.Request.IsHttps,
        });

    public static void Apagar(HttpResponse r) => r.Cookies.Delete(Cookie);

    // GET de uma tela com filtro de Área: sem "CelulaId" na URL usa a escolhida; com ele (até vazio = todas), ele vira a escolhida.
    public static void Aplicar(IFiltroArea tela, HttpRequest r, HttpResponse resp)
    {
        if (!HttpMethods.IsGet(r.Method)) return;
        if (r.Query.ContainsKey("CelulaId")) Gravar(resp, tela.CelulaId);
        else if (Ler(r) is int id) tela.CelulaId = id == 0 ? null : id;
    }
}
