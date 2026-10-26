// ============================================================================
// HISTÓRICO DA NRE — o que mudou no Detalhado, fácil de ler (pedido do usuário em 13/10/2026: o Histórico trazia a
// lista inteira das DIs e itens "antes → depois" e não dava para entender o que tinha mudado).
// Agora sai só a diferença, uma por linha:
//     DI 2600989810:
//         • PTAX: 5,3795 → 5,4000
//         • Item incluído: 2 x [CR-0889] CORREIA (US$ 79,30)
//         • Item [Z000-0536] CARRIER JIG: quantidade 800 → 900
//     DI 2528729044 incluída (sem itens)
//   - Retrato do Detalhado antes/depois de salvar .... Fotografar (usado em Pages/Nre/Editar.cshtml.cs)
//   - As linhas da diferença ......................... Diferencas
//   - Linhas antigas (texto inteiro "antes → depois") . Legivel: a tela Histórico converte na hora de mostrar; o que
//                                                      está gravado no banco não é mexido. Não entendeu o texto: mostra
//                                                      como está.
// ============================================================================
using System.Text.RegularExpressions;
using MS.Modelos;

namespace MS.Servicos;

public static class HistoricoNre
{
    // Linha do Detalhado. Item antigo que não deu para separar: o texto inteiro fica na Descricao.
    public record Item(string Codigo, string Descricao, string Quantidade, string Valor)
    {
        public string Nome => (Codigo == "" ? "" : $"[{Codigo}] ") + Descricao;
        public string Texto => Quantidade == "" ? Descricao : $"{Quantidade} x {Nome} ({Valor})";
    }

    // Bloco do Detalhado: uma DI ou uma compra nacional (NF), com os campos do cabeçalho e os itens.
    public record Bloco(string Rotulo, List<(string Nome, string? Valor)> Campos, List<Item> Itens);

    public static List<Bloco> Fotografar(Nre n) => n.RemessasEmOrdem.Select(r => new Bloco(r.Rotulo(),
        [
            ("Invoice", r.Invoice), (r.Importacao ? "PTAX" : "Ptax de referência", r.Ptax?.Ptax()),
            ("Frete", r.Importacao && r.Frete > 0 ? r.Frete.Dolares() : null),
            ("Seguro", r.Importacao && r.Seguro > 0 ? r.Seguro.Dolares() : null), ("Fornecedor", r.Fornecedor),
        ],
        r.Itens.Select(i => new Item(i.Codigo?.Trim() ?? "", i.Descricao.Trim(), i.Quantidade.ToString("0.##"),
            r.Importacao && i.ValorUnitarioUsd != null ? i.ValorUnitarioUsd.Value.Dolares() : i.ValorUnitario.Reais())).ToList())).ToList();

    // Digitar igual em minúscula ou com espaço sobrando não conta como mudança (o texto é gravado em maiúscula).
    static bool Mesmo(string? a, string? b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    static bool Igual(Item a, Item b) => Mesmo(a.Codigo, b.Codigo) && Mesmo(a.Descricao, b.Descricao) && Mesmo(a.Quantidade, b.Quantidade) && Mesmo(a.Valor, b.Valor);
    static bool Igual(Bloco a, Bloco b) => Mesmo(a.Rotulo, b.Rotulo) && CamposMudados(a, b).Count == 0
        && a.Itens.Count == b.Itens.Count && a.Itens.Zip(b.Itens).All(p => Igual(p.First, p.Second));
    static string Ou(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v.Trim();

    const string Marca = "    • ";

    // Só o que mudou no Detalhado, uma mudança por linha (lista vazia: nada mudou).
    public static List<string> Diferencas(List<Bloco> antes, List<Bloco> depois)
    {
        var a = antes.ToList();
        var d = depois.ToList();
        var linhas = new List<string>();
        // Blocos que ficaram como estavam saem da conta.
        Casar(a, d, Igual);
        // Mesma DI / NF dos dois lados: mostra o que mudou dentro dela.
        foreach (var (x, y) in Casar(a, d, (x, y) => Mesmo(x.Rotulo, y.Rotulo)))
            if (DentroDoBloco(x, y) is { Count: > 0 } dentro) { linhas.Add($"{y.Rotulo}:"); linhas.AddRange(dentro); }
        // Número da DI / NF trocado: sobrou um bloco de cada lado com os mesmos itens (ou os dois sem itens).
        foreach (var (x, y) in Casar(a, d, Parecidos))
        {
            linhas.Add($"{x.Rotulo} passou a ser {y.Rotulo}");
            linhas.AddRange(DentroDoBloco(x, y));
        }
        foreach (var y in d) linhas.AddRange(BlocoInteiro(y, "incluída"));
        foreach (var x in a) linhas.AddRange(BlocoInteiro(x, "retirada"));
        return linhas;
    }

    // Tira das duas listas os pares que combinam (cada elemento entra em um par só) e devolve os pares.
    static List<(T Antes, T Depois)> Casar<T>(List<T> antes, List<T> depois, Func<T, T, bool> combina)
    {
        var pares = new List<(T, T)>();
        foreach (var x in antes.ToList())
        {
            var j = depois.FindIndex(y => combina(x, y));
            if (j < 0) continue;
            pares.Add((x, depois[j]));
            antes.Remove(x);
            depois.RemoveAt(j);
        }
        return pares;
    }

    static bool Parecidos(Bloco x, Bloco y) => x.Itens.Count == 0 && y.Itens.Count == 0
        || x.Itens.Any(i => y.Itens.Any(j => MesmoItem(i, j)));

    // O mesmo item dos dois lados: pelo código; sem código, pela descrição.
    static bool MesmoItem(Item i, Item j) => i.Codigo != "" && j.Codigo != "" ? Mesmo(i.Codigo, j.Codigo) : Mesmo(i.Descricao, j.Descricao);

    static List<(string Nome, string? Antes, string? Depois)> CamposMudados(Bloco x, Bloco y) =>
        x.Campos.Select(c => c.Nome).Union(y.Campos.Select(c => c.Nome))
            .Select(nome => (nome, x.Campos.FirstOrDefault(c => c.Nome == nome).Valor, y.Campos.FirstOrDefault(c => c.Nome == nome).Valor))
            .Where(c => !Mesmo(c.Item2, c.Item3)).ToList();

    static List<string> DentroDoBloco(Bloco x, Bloco y)
    {
        var linhas = CamposMudados(x, y).Select(c => $"{Marca}{c.Nome}: {Ou(c.Antes)} → {Ou(c.Depois)}").ToList();
        var a = x.Itens.ToList();
        var d = y.Itens.ToList();
        Casar(a, d, Igual);
        // Mesmo código (ou mesma descrição) com outra quantidade, valor ou texto.
        foreach (var (i, j) in Casar(a, d, MesmoItem).Concat(Casar(a, d, (i, j) => Mesmo(i.Descricao, j.Descricao))))
        {
            var mudou = new List<string>();
            if (!Mesmo(i.Codigo, j.Codigo)) mudou.Add($"código {Ou(i.Codigo)} → {Ou(j.Codigo)}");
            if (!Mesmo(i.Descricao, j.Descricao)) mudou.Add($"descrição antes: {i.Descricao}");
            if (!Mesmo(i.Quantidade, j.Quantidade)) mudou.Add($"quantidade {i.Quantidade} → {j.Quantidade}");
            if (!Mesmo(i.Valor, j.Valor)) mudou.Add($"valor unitário {i.Valor} → {j.Valor}");
            linhas.Add($"{Marca}Item {j.Nome}: {string.Join(" · ", mudou)}");
        }
        linhas.AddRange(d.Select(j => $"{Marca}Item incluído: {j.Texto}"));
        linhas.AddRange(a.Select(i => $"{Marca}Item retirado: {i.Texto}"));
        return linhas;
    }

    // DI / NF que entrou ou saiu inteira: o cabeçalho numa linha e os itens dela embaixo.
    static List<string> BlocoInteiro(Bloco b, string oQue)
    {
        var campos = string.Concat(b.Campos.Where(c => !string.IsNullOrWhiteSpace(c.Valor)).Select(c => $" · {c.Nome} {c.Valor!.Trim()}"));
        var linhas = new List<string> { $"{b.Rotulo} {oQue}{campos}" + (b.Itens.Count == 0 ? " (sem itens)" : $" — {b.Itens.Count} item(ns):") };
        linhas.AddRange(b.Itens.Select(i => Marca + i.Texto));
        return linhas;
    }

    // ---------- Linhas antigas: "Detalhado: <tudo antes> → <tudo depois>" ----------

    static readonly Regex FimDoItem = new(@"(?<=\((?:US\$|R\$) [\d.,]+\)); ", RegexOptions.IgnoreCase);
    static readonly Regex PartesDoItem = new(@"^(?<q>[\d.,]+) x (?:\[(?<c>[^\]]*)\] )?(?<d>.*) \((?<v>(?:US\$|R\$) [\d.,]+)\)$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    // Detalhe gravado no formato antigo: troca a parte do Detalhado (ou "Itens", mais antigo ainda) pela diferença.
    public static string Legivel(string detalhes)
    {
        try
        {
            var linhas = detalhes.Split('\n').ToList();
            var ini = linhas.FindIndex(l => l.StartsWith("Detalhado: ") || l.StartsWith("Itens: "));
            if (ini < 0) return detalhes;
            var soItens = linhas[ini].StartsWith("Itens: ");
            var fim = linhas.FindIndex(ini + 1, l => l.StartsWith("Fechamento: "));
            if (fim < 0) fim = linhas.Count;
            var texto = string.Join("\n", linhas.Skip(ini).Take(fim - ini))[(soItens ? "Itens: " : "Detalhado: ").Length..];
            var lados = texto.Split(" → ");
            if (lados.Length != 2) return detalhes;
            var antes = Ler(lados[0], soItens);
            var depois = Ler(lados[1], soItens);
            if (antes == null || depois == null) return detalhes;
            var dif = Diferencas(antes, depois);
            if (dif.Count == 0) dif.Add("Detalhado: os mesmos itens, só em outra ordem.");
            return string.Join("\n", linhas.Take(ini).Concat(dif).Concat(linhas.Skip(fim)));
        }
        catch (Exception)
        {
            return detalhes;   // texto que não deu para entender: mostra como está gravado
        }
    }

    // Um lado do texto antigo: um bloco por linha — "DI 123 · invoice X · PTAX 5,3795 · frete US$ 1,00 · seguro US$ 1,00: itens".
    static List<Bloco>? Ler(string lado, bool soItens)
    {
        var vazio = lado.Trim() is "—" or "";
        if (soItens) return [new Bloco("Itens", [], vazio ? [] : LerItens(lado))];
        if (vazio) return [];
        var blocos = new List<Bloco>();
        foreach (var linha in lado.Split('\n').Select(l => l.TrimEnd()).Where(l => l != ""))
        {
            var corte = linha.IndexOf(": ", StringComparison.Ordinal);
            if (corte < 0 && !linha.EndsWith(':')) return null;
            var cabecalho = (corte < 0 ? linha[..^1] : linha[..corte]).Split(" · ");
            var campos = new List<(string, string?)>();
            foreach (var parte in cabecalho.Skip(1))
            {
                var nome = new[] { "Invoice", "PTAX", "Frete", "Seguro" }.FirstOrDefault(c => parte.StartsWith(c + " ", StringComparison.OrdinalIgnoreCase));
                campos.Add(nome == null ? ("Fornecedor", parte) : (nome, parte[(nome.Length + 1)..]));
            }
            blocos.Add(new Bloco(cabecalho[0], campos, corte < 0 ? [] : LerItens(linha[(corte + 2)..])));
        }
        return blocos;
    }

    static List<Item> LerItens(string texto) => FimDoItem.Split(texto).Select(t => t.Trim()).Where(t => t != "")
        .Select(t => PartesDoItem.Match(t) is { Success: true } m
            ? new Item(m.Groups["c"].Value, m.Groups["d"].Value, m.Groups["q"].Value, m.Groups["v"].Value)
            : new Item("", t, "", "")).ToList();
}
