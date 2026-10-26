// ============================================================================
// ENDEREÇO "/" do sistema NRE (lógica). No MS aqui ficava o Dashboard de Manutenção; no NRE não há dashboard:
//   - ainda não escolheu a área ... vai para os cartões dos clientes (Conta/Area)
//   - já escolheu ................. vai para a tela inicial do grupo (NRE — Acesso.PaginaInicial)
// Logo do cliente da área (cartões da tela Conta/Area e tela Áreas): OnGetLogoAsync.
// ============================================================================
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages;

public class IndexModel(AppDb db, Fotos fotos) : PageModel
{
    public IActionResult OnGet()
    {
        if (AreaEscolhida.Ler(Request) == null) return RedirectToPage("/Conta/Area");
        var grupo = Enum.TryParse<Grupo>(User.FindFirstValue(ClaimTypes.Role), out var g) ? g : Grupo.Visitante;
        return RedirectToPage(Acesso.PaginaInicial(grupo));
    }

    // Logo do cliente da área. Trocar o logo muda o endereço (&v=arquivo).
    public async Task<IActionResult> OnGetLogoAsync(int id)
    {
        var arquivo = await db.Celulas.Where(c => c.Id == id).Select(c => c.Logo).FirstOrDefaultAsync();
        if (arquivo == null || !System.IO.File.Exists(fotos.Caminho(arquivo))) return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";
        return PhysicalFile(fotos.Caminho(arquivo), "image/png");
    }
}
