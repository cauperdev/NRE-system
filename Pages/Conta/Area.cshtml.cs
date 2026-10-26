// ============================================================================
// TELA: ESCOLHER O CLIENTE (lógica)  ·  endereço /Conta/Area  ·  visual em Area.cshtml  ·  qualquer usuário logado
// Fica entre o login e o sistema (pedido do usuário em 13/10/2026): um cartão por área (cliente), com o logo enviado na
// tela Áreas. Clicar no cartão guarda a área (Servicos/AreaEscolhida.cs) e abre a tela inicial do grupo já filtrada.
// Também abre pelo item "Escolher área" no alto do menu, para trocar.
// ONDE MEXER: lista dos cartões → OnGetAsync · o que acontece ao clicar → OnGetEscolher
// ============================================================================
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Conta;

public class AreaModel(AppDb db) : PageModel
{
    public List<Celula> Celulas { get; set; } = new();
    public int? Escolhida { get; set; }   // área que está valendo agora (0 = todas), para destacar o cartão

    Grupo Grupo => Enum.TryParse<Grupo>(User.FindFirstValue(ClaimTypes.Role), out var g) ? g : Grupo.Visitante;

    public async Task<IActionResult> OnGetAsync()
    {
        Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
        if (Celulas.Count == 0) return OnGetEscolher(null); // sem áreas cadastradas: não tem o que escolher
        Escolhida = AreaEscolhida.Ler(Request);
        return Page();
    }

    // id vazio = todas as áreas.
    public IActionResult OnGetEscolher(int? id)
    {
        AreaEscolhida.Gravar(Response, id);
        return RedirectToPage(Acesso.PaginaInicial(Grupo));
    }
}
