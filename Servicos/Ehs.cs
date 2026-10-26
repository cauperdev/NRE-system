// ============================================================================
// EHS / NR — tarefa de Norma Regulamentadora da máquina (pedido do usuário em 09/10/2026).
// Na aba "Tarefas do plano" do Cronograma, o botão "Adicionar NR (EHS)" cria um quadro vermelho só com o texto.
// Ele é gravado entre as tarefas do plano com "EHS — " na frente ("EHS — BOTÃO DE EMERGÊNCIA: ..."), SEMPRE POR ÚLTIMO.
// Desde 14/10/2026 não há mais campo de NR (pedido do usuário). As cadastradas antes ficam como estavam, com a NR na
// frente ("NR-15 — USAR LUVA ..."): continuam sendo de EHS e, no quadro, a NR aparece dentro do texto (dá para apagar).
// Desde 14/10/2026 é TAREFA do checklist (antes era só aviso): vai para a OS (Regras.CopiarTarefas), o técnico marca
// ao fazer a manutenção e fica registrada na Finalização — sempre no fim, em vermelho (Shared/_LinhaChecklist.cshtml).
// Só quem tem a permissão "planos_ehs" (grupo EHS e administrador) cria, altera ou remove; as OS abertas acompanham
// na hora (Regras.SincronizarNrAsync).
// ============================================================================
using System.Text.RegularExpressions;

namespace MS.Servicos;

public static partial class Ehs
{
    // Marca gravada na frente das tarefas de EHS novas.
    const string Marca = "EHS — ";

    // Tarefa antiga, com uma ou mais NRs na frente: "NR-15 — ..." ou "NR-15, NR-9 — ...".
    [GeneratedRegex(@"^(NR-\d{1,2}(?:, NR-\d{1,2})*)\s+[—–-]\s*(.*)$", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Antiga();

    // Para o selo da OS/Finalização: "EHS — TESTAR" → ("", "TESTAR"); "NR-15 — USAR LUVA" → ("NR-15", "USAR LUVA");
    // tarefa comum → (null, texto). Nr != null = tarefa de EHS (o selo mostra "EHS" ou "EHS · NR-15").
    public static (string? Nr, string Texto) Ler(string? descricao)
    {
        var d = descricao ?? "";
        if (d.StartsWith(Marca)) return ("", d[Marca.Length..]);
        var m = Antiga().Match(d);
        return m.Success ? (m.Groups[1].Value.ToUpper(), m.Groups[2].Value) : (null, d);
    }

    public static bool EhNr(string? descricao) => Ler(descricao).Nr != null;

    // O que aparece na caixa de texto do quadro vermelho: sem a marca "EHS — "; a antiga mostra a NR dentro do texto.
    public static string TextoNoQuadro(string descricao) => descricao.StartsWith(Marca) ? descricao[Marca.Length..] : descricao;

    // Texto do quadro vermelho → texto gravado. Em branco = descartada (""). Texto que já começa com NR ("NR-15 — ...")
    // fica como está; o resto ganha a marca "EHS — ".
    public static string Gravar(string? texto)
    {
        var t = (texto ?? "").Trim();
        return t == "" || Antiga().IsMatch(t) ? t : Marca + t;
    }

    // Tarefas de EHS de uma lista de tarefas, na ordem em que foram cadastradas.
    public static List<string> TarefasNr(IEnumerable<string> descricoes) => descricoes.Where(EhNr).ToList();
}
