// ============================================================================
// VOLTAR PARA A LISTA COM O FILTRO (pedido do usuário em 20/10/2026: "quero poder voltar para a parte do filtro e não
// para a parte com todos os dados"). Ex.: Cronograma filtrado por "CONVEYOR" → abre um plano → Salvar (ou o
// "Cronograma" do caminho no alto, ou Voltar) → volta para a lista AINDA filtrada por CONVEYOR, na mesma rolagem.
// Como funciona:
//   1. A lista guarda o próprio filtro num cookie "ms-lista-<pasta>" (wwwroot/js/interacoes.js, seção 15).
//   2. Saindo de uma tela da mesma pasta (Editar/Ver) para a lista — RedirectToPage("Index") depois de Salvar/Remover,
//      ou os links do caminho e do Voltar (?volta=1) — a lista é aberta com o filtro guardado (#voltou = volta a rolagem).
//   3. O MENU LATERAL abre a lista limpa, como sempre (escolha do usuário).
// ONDE MEXER: telas que lembram o filtro ..... Listas
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MS.Servicos;

public class VoltaParaLista : IPageFilter
{
    public static readonly string[] Listas =
        ["Equipamentos", "Cronograma", "Ordens", "Finalizacao", "Nre", "SpareParts", "Usuarios", "HistoricoJms", "Grupos"];

    public static string Cookie(string pasta) => "ms-lista-" + pasta.ToLowerInvariant();

    // "/Cronograma/Editar" → "Cronograma" (só as pastas de Listas).
    static string? Pasta(string? caminho)
    {
        var partes = (caminho ?? "").Trim('/').Split('/');
        return partes.Length == 2 ? Listas.FirstOrDefault(l => l.Equals(partes[0], StringComparison.OrdinalIgnoreCase)) : null;
    }

    public void OnPageHandlerSelected(PageHandlerSelectedContext context) { }

    // Lista aberta com ?volta=1: redireciona para a mesma lista com o filtro guardado.
    public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        var req = context.HttpContext.Request;
        var caminho = context.ActionDescriptor.ViewEnginePath;
        var pasta = Pasta(caminho);
        if (pasta == null || !caminho.EndsWith("/Index") || !HttpMethods.IsGet(req.Method) || !req.Query.ContainsKey("volta")) return;
        var consulta = req.Cookies[Cookie(pasta)] ?? "";
        if (!consulta.StartsWith('?')) consulta = Uri.UnescapeDataString(consulta);
        if (!consulta.StartsWith('?') || consulta.Contains("volta=")) consulta = "";
        context.Result = new LocalRedirectResult($"{req.PathBase}/{pasta}{consulta}#voltou");
    }

    // Editar/Ver que volta para a lista depois de salvar/remover: marca ?volta=1 (o passo acima põe o filtro).
    public void OnPageHandlerExecuted(PageHandlerExecutedContext context)
    {
        var caminho = context.ActionDescriptor.ViewEnginePath;
        if (Pasta(caminho) == null || caminho.EndsWith("/Index")) return;
        if (context.Result is RedirectToPageResult r && r.PageName == "Index")
        {
            r.RouteValues ??= new Microsoft.AspNetCore.Routing.RouteValueDictionary();
            r.RouteValues["volta"] = 1;
        }
    }
}
