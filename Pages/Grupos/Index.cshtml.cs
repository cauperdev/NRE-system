// ============================================================================
// TELA: GRUPOS — lista (lógica)  ·  endereço /Grupos  ·  permissão "grupos"
// Mostra os 5 grupos; clicar num grupo abre as permissões dele (Editar).
// ============================================================================
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Grupos;

public class IndexModel(AppDb db) : MsPage
{
    protected override string Modulo => "grupos";
    public Dictionary<Grupo, int> Contagem { get; set; } = new();

    public async Task OnGetAsync() =>
        Contagem = await db.Usuarios.GroupBy(u => u.Grupo).ToDictionaryAsync(g => g.Key, g => g.Count());
}
