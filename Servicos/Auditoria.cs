// ============================================================================
// HISTÓRICO — grava quem mudou o quê e quando (aparece na tela Histórico de cada registro).
// Uso: auditoria.Registrar("Equipamento", id, "Alterado", "detalhe").
// Alteração: o detalhe traz só os campos que mudaram, de quanto para quanto (Auditoria.Mudancas):
//   "Localização: SMT LINE 02 → LINHA 03"   (texto comprido: "antes:" e "depois:" em linhas separadas)
// NRE: o Detalhado sai item por item (Servicos/HistoricoNre.cs).
// ============================================================================
using MS.BancoDeDados;
using MS.Modelos;

namespace MS.Servicos;

// Botão "Histórico" — mostra quem mudou o quê e quando.
public class Auditoria(AppDb db, IHttpContextAccessor http)
{
    // Só os campos que mudaram, um por linha: "Campo: antes → depois". Nada mudou: "".
    // Uso: Auditoria.Mudancas(("Localização", antes.Localizacao, e.Localizacao), ("Status", ..., ...))
    // Texto em maiúscula, como é gravado (AppDb.Maiusculas): digitar igual em minúscula não conta como mudança.
    public static string Mudancas(params (string Campo, object? Antes, object? Depois)[] campos) =>
        string.Join("\n", campos
            .Select(c => (c.Campo, Antes: Texto(c.Antes), Depois: Texto(c.Depois)))
            .Where(c => c.Antes != c.Depois)
            .Select(c => Longo(c.Antes, c.Depois) ? $"{c.Campo}:\n    antes: {c.Antes}\n    depois: {c.Depois}"
                : $"{c.Campo}: {c.Antes} → {c.Depois}"));

    // Texto comprido (descrição, lista de tarefas): "antes" e "depois" em linhas separadas, para dar para comparar.
    static bool Longo(string antes, string depois) => antes.Length + depois.Length > 100 || antes.Contains('\n') || depois.Contains('\n');

    static string Texto(object? v) => v switch
    {
        null => "—",
        string s => string.IsNullOrWhiteSpace(s) ? "—" : s.Trim().ToUpperInvariant(),
        bool b => b ? "Sim" : "Não",
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("dd/MM/yyyy") : d.ToString("dd/MM/yyyy HH:mm"),
        decimal m => m.ToString("0.##"),
        _ => v.ToString() ?? "—",
    };

    public void Registrar(string entidade, int id, string acao, string detalhes = "")
    {
        db.Historicos.Add(new Historico
        {
            Entidade = entidade,
            EntidadeId = id,
            Acao = acao,
            Detalhes = detalhes,
            Usuario = http.HttpContext?.User.Identity?.Name ?? "sistema",
            Quando = DateTime.Now,
        });
    }
}
