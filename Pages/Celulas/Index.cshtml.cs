// ============================================================================
// TELA: CÉLULAS (lógica)  ·  endereço /Celulas  ·  visual em Index.cshtml  ·  permissão "celulas"
// Cadastro simples: adicionar, renomear (clicando na célula) e remover as marcadas.
// ONDE MEXER:
//   - Lista ............................... OnGetAsync (páginas de 50: PorPagina, como a lista de OS)
//   - Adicionar / salvar nome e logo ...... OnPostSalvarAsync (logo do cliente: cartões do alto do Dashboard; PNG por
//                                           Servicos/Fotos.cs → SalvarLogoAsync; mostrado por Pages/Index → OnGetLogoAsync)
//   - Remover marcadas .................... OnPostRemoverAsync (usuários e equipamentos ficam sem célula; com "marcar
//                                           todas" em várias páginas: todas)
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Celulas;

public class IndexModel(AppDb db, Auditoria auditoria, Fotos fotos) : MsPage
{
    protected override string Modulo => "celulas";
    const int PorPagina = 50;

    public record Linha(int Id, string Nome, int Usuarios, int Equipamentos, string? Logo);
    public List<Linha> Lista { get; set; } = new();
    public int EditandoId { get; set; }
    public string EditandoNome { get; set; } = "";
    public string? EditandoLogo { get; set; }
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;
    public int Total { get; set; }
    public int Paginas => Math.Max(1, (int)Math.Ceiling(Total / (double)PorPagina));

    public async Task OnGetAsync(int? editar)
    {
        Total = await db.Celulas.CountAsync();
        Pagina = Math.Clamp(Pagina, 1, Paginas);
        Lista = await db.Celulas.OrderBy(c => c.Nome).Skip((Pagina - 1) * PorPagina).Take(PorPagina)
            .Select(c => new Linha(c.Id, c.Nome, db.Usuarios.Count(u => u.CelulaId == c.Id), db.Equipamentos.Count(e => e.CelulaId == c.Id), c.Logo))
            .ToListAsync();
        if (editar is int id && await db.Celulas.FindAsync(id) is Celula c)
        {
            EditandoId = c.Id;
            EditandoNome = c.Nome;
            EditandoLogo = c.Logo;
        }
    }

    // logo: imagem escolhida agora (troca a anterior) · removerLogo: tira o logo (o cartão do Dashboard mostra o nome).
    public async Task<IActionResult> OnPostSalvarAsync(int id, string nome, IFormFile? logo, bool removerLogo)
    {
        nome = (nome ?? "").Trim();
        if (nome == "") return RedirectToPage();
        string? novoLogo = null;
        if (logo is { Length: > 0 })
        {
            try { novoLogo = await fotos.SalvarLogoAsync(logo); } catch (Exception) { }
            if (novoLogo == null)
            {
                Erro("Não foi possível ler o logo. Escolha uma imagem (PNG ou JPG).");
                return RedirectToPage(new { Pagina, editar = id > 0 ? (int?)id : null });
            }
        }
        var c = id > 0 ? await db.Celulas.FindAsync(id) : null;
        var nomeAntes = c?.Nome;
        if (c == null) { c = new Celula(); db.Celulas.Add(c); }
        c.Nome = nome;
        var logoAntigo = c.Logo;
        if (novoLogo != null) c.Logo = novoLogo;
        else if (removerLogo) c.Logo = null;
        await db.SaveChangesAsync();
        if (logoAntigo != null && logoAntigo != c.Logo) fotos.Apagar(logoAntigo);
        var mudouLogo = logoAntigo == c.Logo ? null : c.Logo == null ? "Logo: removido" : logoAntigo == null ? "Logo: adicionado" : "Logo: trocado";
        if (nomeAntes == null) auditoria.Registrar("Celula", c.Id, "Criado", nome + (mudouLogo != null ? $"\n{mudouLogo}" : ""));
        else if (string.Join("\n", new[] { Auditoria.Mudancas(("Nome", nomeAntes, nome)), mudouLogo }.Where(s => !string.IsNullOrEmpty(s))) is { Length: > 0 } mudou)
            auditoria.Registrar("Celula", c.Id, "Alterado", mudou);
        await db.SaveChangesAsync();
        Ok($"Área {nome} salva.");
        return RedirectToPage(new { Pagina });
    }

    // Remove as células marcadas; usuários, equipamentos e peças (Spare Parts) delas ficam sem célula. Com "marcar todas" (todos): todas.
    public async Task<IActionResult> OnPostRemoverAsync(int[] ids, bool todos)
    {
        if (todos) ids = await db.Celulas.Select(c => c.Id).ToArrayAsync();
        var lista = await db.Celulas.Where(c => ids.Contains(c.Id)).ToListAsync();
        if (lista.Count == 0) return RedirectToPage(new { Pagina });
        var idsValidos = lista.Select(c => c.Id).ToList();
        // Área com NRE não sai (24/10/2026): a NRE ficaria sem área e sumiria de todas as listas.
        var comNre = await db.Nres.Where(n => n.CelulaId != null && idsValidos.Contains(n.CelulaId.Value))
            .Select(n => n.Celula!.Nome).Distinct().ToListAsync();
        if (comNre.Count > 0)
        {
            Erro($"Nada foi removido: {string.Join(", ", comNre)} tem NRE. Passe as NRE para outra área (aba Máquina e descrição da NRE) antes de remover a área.");
            return RedirectToPage(new { Pagina });
        }
        await db.Usuarios.Where(u => u.CelulaId != null && idsValidos.Contains(u.CelulaId.Value)).ExecuteUpdateAsync(s => s.SetProperty(u => u.CelulaId, (int?)null));
        await db.Equipamentos.Where(e => e.CelulaId != null && idsValidos.Contains(e.CelulaId.Value)).ExecuteUpdateAsync(s => s.SetProperty(e => e.CelulaId, (int?)null));
        await db.SpareParts.Where(p => p.CelulaId != null && idsValidos.Contains(p.CelulaId.Value)).ExecuteUpdateAsync(s => s.SetProperty(p => p.CelulaId, (int?)null));
        foreach (var c in lista) auditoria.Registrar("Celula", c.Id, "Removido", c.Nome);
        db.Celulas.RemoveRange(lista);
        await db.SaveChangesAsync();
        foreach (var c in lista) if (c.Logo != null) fotos.Apagar(c.Logo);
        Ok(lista.Count == 1 ? $"Área {lista[0].Nome} removida." : $"{lista.Count} áreas removidas.");
        return RedirectToPage(new { Pagina });
    }
}
