// ============================================================================
// REGRA 07 — ACESSO PELO GRUPO (sistema NRE: só as telas NRE, Usuários, Áreas e Grupos): o que cada grupo (Visitante, Auditor, Técnico, Coordenador, Administrador)
// vê e edita. As permissões em uso ficam no banco e são trocadas na tela Grupos; aqui fica o PADRÃO de fábrica.
// ONDE MEXER:
//   - Tela nova no controle de acesso .......... lista Modulos (chave, nome, página inicial, ajuda)
//   - Padrão de fábrica ("Restaurar padrão") ... PadraoVer / PadraoEditar
//   - Tela que abre ao entrar no sistema ....... PrioridadeInicial
//   - Quem só vê finalizações aprovadas ........ SoAprovadas
// Mudar o padrão aqui só vale para bancos novos ou ao apertar "Restaurar padrão" na tela Grupos.
// ============================================================================
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;

namespace MS.Servicos;

// Regra 07 — o que cada pessoa vê depende do grupo.
// As permissões ficam no banco (tela Grupos) e são mantidas em memória para consulta rápida.
public static class Acesso
{
    const Grupo V = Grupo.Visitante, A = Grupo.Auditor, T = Grupo.Tecnico, C = Grupo.Coordenador, ADM = Grupo.Administrador, E = Grupo.Ehs;

    // Grupos do sistema NRE (pedido do usuário, 24/10/2026): sem Auditor e sem EHS; o Técnico aparece como "Analista"
    // (Textos.Nome). Os valores do enum ficam iguais aos do MS (o banco veio dele); Auditor/EHS só não são usados.
    // Quem ainda estiver num deles passa para Visitante quando o sistema liga (Program.cs).
    public static readonly Grupo[] Grupos = [Grupo.Visitante, Grupo.Tecnico, Grupo.Coordenador, Grupo.Administrador];

    // TemVer/TemEditar: quais caixas fazem sentido para a tela. Pagina: destino ao entrar no sistema.
    public record Modulo(string Chave, string Nome, bool TemVer, bool TemEditar, string? Pagina, string Ajuda = "");

    public static readonly Modulo[] Modulos =
    {
        new("grupos", "Grupos", true, true, "/Grupos/Index", "Editar = alterar as permissões dos grupos"),
        new("usuarios", "Usuários", true, true, "/Usuarios/Index"),
        new("celulas", "Áreas", true, true, "/Celulas/Index"),
        new("nre", "NRE", true, true, "/Nre/Index", "Editar = abrir NRE e acompanhar até concluir"),
        new("nre_aprovacao", "NRE — aprovação", true, true, null, "Editar = remover NRE (na lista e na própria NRE)"),
        new("manual", "Manual", true, false, "/Manual/Index", "Como usar o sistema NRE (só leitura)"),
    };

    // Padrão de fábrica (usado na primeira execução e no botão "Restaurar padrão").
    static readonly Dictionary<string, Grupo[]> PadraoVer = new()
    {
        ["grupos"] = [ADM],
        ["usuarios"] = [C, ADM],
        ["celulas"] = [C, ADM],
        ["nre"] = [T, C, ADM],
        ["nre_aprovacao"] = [C, ADM],
        ["manual"] = [V, T, C, ADM],    // todos leem o manual
    };

    static readonly Dictionary<string, Grupo[]> PadraoEditar = new()
    {
        ["grupos"] = [ADM],
        ["usuarios"] = [C, ADM],
        ["celulas"] = [C, ADM],
        ["nre"] = [T, C, ADM],
        ["nre_aprovacao"] = [C, ADM],   // técnico abre a NRE; coordenador aprova ou recusa
    };

    public static (bool Ver, bool Editar) Padrao(Grupo g, string modulo) =>
        (PadraoVer.GetValueOrDefault(modulo)?.Contains(g) == true, PadraoEditar.GetValueOrDefault(modulo)?.Contains(g) == true);

    static volatile Dictionary<(Grupo, string), (bool Ver, bool Editar)> atual =
        Enum.GetValues<Grupo>().SelectMany(g => Modulos.Select(m => (g, m.Chave)))
            .ToDictionary(k => k, k => Padrao(k.g, k.Chave));

    // Administrador nunca perde o acesso a Grupos — senão ninguém conseguiria desfazer.
    public static bool Travado(Grupo g, string modulo) => g == ADM && modulo == "grupos";

    public static bool PodeVer(Grupo g, string modulo) =>
        Travado(g, modulo) || atual.TryGetValue((g, modulo), out var p) && p.Ver;

    public static bool PodeEditar(Grupo g, string modulo) =>
        Travado(g, modulo) || atual.TryGetValue((g, modulo), out var p) && p.Editar;

    // Cria a tabela se o banco for de uma versão anterior, grava o padrão na primeira vez e carrega.
    public static void Carregar(AppDb db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Permissoes" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Permissoes" PRIMARY KEY AUTOINCREMENT,
                "Grupo" INTEGER NOT NULL, "Modulo" TEXT NOT NULL, "Ver" INTEGER NOT NULL, "Editar" INTEGER NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Permissoes_Grupo_Modulo" ON "Permissoes" ("Grupo", "Modulo");
            """);
        var existentes = db.Permissoes.ToList();
        foreach (var g in Grupos)
            foreach (var m in Modulos.Where(m => !existentes.Any(p => p.Grupo == g && p.Modulo == m.Chave)))
            {
                var (ver, editar) = Padrao(g, m.Chave);
                db.Permissoes.Add(new PermissaoGrupo { Grupo = g, Modulo = m.Chave, Ver = ver, Editar = editar });
            }
        // Editar inclui Ver (linhas antigas de telas que não tinham a caixinha Ver ficaram com Ver desmarcado).
        foreach (var p in existentes.Where(p => p.Editar && !p.Ver)) p.Ver = true;
        db.SaveChanges();
        Recarregar(db);
    }

    public static void Recarregar(AppDb db) =>
        atual = db.Permissoes.AsNoTracking().ToList().ToDictionary(p => (p.Grupo, p.Modulo), p => (p.Ver, p.Editar));

    // Visitante e Auditor só veem as finalizações aprovadas.
    public static bool SoAprovadas(Grupo g) => g is V or A;

    // Tela ao entrar: a primeira desta lista que o grupo pode ver.
    static readonly string[] PrioridadeInicial =
        ["nre", "usuarios", "celulas", "grupos", "manual"];

    public static string PaginaInicial(Grupo g) =>
        PrioridadeInicial.Select(k => Modulos.First(m => m.Chave == k))
            .FirstOrDefault(m => PodeVer(g, m.Chave))?.Pagina ?? "/Conta/Negado";

    public static string Descricao(Grupo g)
    {
        var ver = Modulos.Count(m => m.TemVer && PodeVer(g, m.Chave));
        var editar = Modulos.Count(m => m.TemEditar && PodeEditar(g, m.Chave));
        return ver == 0 ? "Sem acesso" : $"Vê {ver} tela(s) · edita {editar}";
    }
}
