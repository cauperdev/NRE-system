// ============================================================================
// TELA: GRUPOS — permissões de um grupo (lógica)  ·  endereço /Grupos/Editar/{grupo}
// Regra 07: marca o que o grupo VÊ e o que EDITA em cada tela. Grava no banco e vale na hora.
// ONDE MEXER:
//   - Salvar as caixinhas ................. OnPostAsync
//   - Botão Restaurar padrão .............. OnPostPadraoAsync (o padrão está em Servicos/Acesso.cs)
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Grupos;

public class EditarModel(AppDb db, Auditoria auditoria) : MsPage
{
    protected override string Modulo => "grupos";

    [BindProperty(SupportsGet = true, Name = "grupo")] public Grupo GrupoAlvo { get; set; }
    public Dictionary<string, (bool Ver, bool Editar)> Atual { get; set; } = new();
    public int Usuarios { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!Acesso.Grupos.Contains(GrupoAlvo)) return NotFound(); // endereço com grupo que não existe (ou Auditor/EHS, fora do NRE)
        Atual = Acesso.Modulos.ToDictionary(m => m.Chave, m => (Acesso.PodeVer(GrupoAlvo, m.Chave), Acesso.PodeEditar(GrupoAlvo, m.Chave)));
        Usuarios = await db.Usuarios.CountAsync(u => u.Grupo == GrupoAlvo);
        return Page();
    }

    public Task<IActionResult> OnPostAsync(string[] ver, string[] editar, string acao) =>
        Salvar(m => (ver.Contains(m.Chave), editar.Contains(m.Chave)), acao, "Alterado");

    public Task<IActionResult> OnPostPadraoAsync() =>
        Salvar(m => Acesso.Padrao(GrupoAlvo, m.Chave), "continuar", "Padrão restaurado");

    async Task<IActionResult> Salvar(Func<Acesso.Modulo, (bool Ver, bool Editar)> novo, string acao, string titulo)
    {
        if (!Acesso.Grupos.Contains(GrupoAlvo)) return NotFound();
        var registros = await db.Permissoes.Where(p => p.Grupo == GrupoAlvo).ToListAsync();
        var mudancas = new List<string>();
        foreach (var m in Acesso.Modulos)
        {
            if (Acesso.Travado(GrupoAlvo, m.Chave)) continue;
            var (ver, editar) = novo(m);
            if (!m.TemVer) ver = editar;          // telas sem "Ver" (ex.: gestão de OS)
            if (!m.TemEditar) editar = false;     // telas só de consulta
            if (editar) ver = true;                // Editar inclui Ver

            var p = registros.FirstOrDefault(r => r.Modulo == m.Chave);
            if (p == null) { p = new PermissaoGrupo { Grupo = GrupoAlvo, Modulo = m.Chave }; db.Permissoes.Add(p); }
            if (p.Ver != ver || p.Editar != editar)
                mudancas.Add($"{m.Nome}: {Texto(p.Ver, p.Editar)} → {Texto(ver, editar)}");
            p.Ver = ver;
            p.Editar = editar;
        }
        if (mudancas.Count > 0) auditoria.Registrar("Grupo", (int)GrupoAlvo, titulo, string.Join("\n", mudancas));
        await db.SaveChangesAsync();
        Acesso.Recarregar(db);

        var padrao = titulo == "Padrão restaurado";
        Ok(mudancas.Count == 0
            ? (padrao ? $"As permissões de {GrupoAlvo.Nome()} já estavam no padrão." : "Nenhuma permissão alterada.")
            : padrao ? $"Permissões de {GrupoAlvo.Nome()} voltaram ao padrão ({mudancas.Count} alteração(ões)). Vale na hora para todos do grupo."
            : $"Permissões de {GrupoAlvo.Nome()} salvas ({mudancas.Count} alteração(ões)). Vale na hora para todos do grupo.");
        return acao == "continuar" ? RedirectToPage("Editar", new { grupo = GrupoAlvo }) : RedirectToPage("Index");
    }

    static string Texto(bool ver, bool editar) => editar ? "editar" : ver ? "ver" : "sem acesso";
}
