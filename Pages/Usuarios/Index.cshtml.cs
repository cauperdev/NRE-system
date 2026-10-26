// ============================================================================
// TELA: USUÁRIOS — lista (lógica)  ·  endereço /Usuarios  ·  visual em Index.cshtml  ·  permissão "usuarios"
// ONDE MEXER:
//   - Filtros (grupo, célula, pesquisa) ... Filtrar
//   - Itens por página (50) ............... PorPagina (páginas como a lista de OS)
//   - Quem pode ser removido .............. PodeRemover (ninguém remove a si mesmo; só administrador remove administrador)
//   - Remover selecionados ................ OnPostRemoverAsync (com "marcar todos" em várias páginas: todos da lista filtrada)
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Usuarios;

// Sem o filtro do cartão do cliente (IFiltroArea): usuário sem área (ex.: 1º acesso pela rede, Visitante) sumiria da lista.
public class IndexModel(AppDb db, Auditoria auditoria) : MsPage
{
    protected override string Modulo => "usuarios";
    const int PorPagina = 50;

    [BindProperty(SupportsGet = true)] public Grupo? GrupoFiltro { get; set; }
    [BindProperty(SupportsGet = true)] public int? CelulaId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

    public List<Celula> Celulas { get; set; } = new();
    public List<Usuario> Lista { get; set; } = new();
    public int Total { get; set; }
    public int Paginas => Math.Max(1, (int)Math.Ceiling(Total / (double)PorPagina));

    IQueryable<Usuario> Filtrar()
    {
        var q = db.Usuarios.Include(u => u.Celula).AsQueryable();
        if (GrupoFiltro != null) q = q.Where(u => u.Grupo == GrupoFiltro);
        if (CelulaId != null) q = q.Where(u => u.CelulaId == CelulaId);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            // Nomes gravados em maiúscula; login e e-mail como foram digitados.
            var t = Q.Trim().ToUpperInvariant();
            q = q.Where(u => u.Login.ToUpper().Contains(t) || u.PrimeiroNome.Contains(t) || u.UltimoNome.Contains(t) || u.Email.ToUpper().Contains(t));
        }
        return q.OrderBy(u => u.Login);
    }

    public async Task OnGetAsync()
    {
        Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
        var q = Filtrar();
        Total = await q.CountAsync();
        Pagina = Math.Clamp(Pagina, 1, Paginas);
        Lista = await q.Skip((Pagina - 1) * PorPagina).Take(PorPagina).ToListAsync();
    }

    // Ninguém remove o próprio usuário, e só administrador remove administrador.
    public bool PodeRemover(Usuario u) => u.Id != UsuarioId && (u.Grupo != Grupo.Administrador || Grupo == Grupo.Administrador);

    // Remove os usuários marcados. OS e finalizações deles continuam, só ficam sem técnico/coordenador.
    // Com "marcar todos" (todos), todos os da lista filtrada (todas as páginas) — menos os que não podem ser removidos.
    public async Task<IActionResult> OnPostRemoverAsync(int[] ids, bool todos)
    {
        if (todos) ids = await Filtrar().Select(u => u.Id).ToArrayAsync();
        var lista = (await db.Usuarios.Where(u => ids.Contains(u.Id)).ToListAsync()).Where(PodeRemover).ToList();
        if (lista.Count > 0)
        {
            foreach (var u in lista) auditoria.Registrar("Usuario", u.Id, "Removido", u.Login);
            await db.SoltarDosPlanosAsync(lista.Select(u => u.Id).ToList());
            db.Usuarios.RemoveRange(lista);
            await db.SaveChangesAsync();
            Ok(lista.Count == 1 ? $"Usuário {lista[0].Login} removido." : $"{lista.Count} usuário(s) removido(s).");
        }
        return RedirectToPage(new { GrupoFiltro, CelulaId, Q, Pagina });
    }
}
