// ============================================================================
// IMPORTAR NRE DO EXCEL — leitura das planilhas de NRE da Samsung para o botão "Importar Excel" da lista de NRE
// (Pages/Nre/Index.cshtml.cs → OnPostImportarAsync, que cria ou atualiza as NRE). Pedido do usuário em 23/10/2026:
// "ler o Excel e trazer os dados que o sistema precisa".
//   - AMARELA (detalhe): título "1 - Project A076 - Toolings SMD, IM" e, embaixo, o cabeçalho Item · Material ·
//     Invoice No. · Qtde · Unit Price $ · R$ Unit Price · $ Total Price · R$ Total Price · Desconto · Acréscimo ·
//     R$ Total NF · PTAX · Invoice No. · DI No. Cada linha com preço é um item; os itens são agrupados pela DI (um bloco
//     do Detalhado por DI) e "Frete DI ..." / "Seguro DI ..." viram o frete e o seguro da DI. Material
//     "OM03001-001617-112016400-Metal Mask" → código OM03001-001617-112016400 + descrição Metal Mask.
//     Linha sem preço (grupo "1 Itens consumíveis - SMT", totais, títulos) fica de fora.
//     Compra nacional (planilha da A075, "NFs SEDA"): coluna "NF No." em vez de DI/Invoice → um bloco "compra nacional"
//     por NF, preço = R$ Unit Price (o Unit Price $ dela é só R$ ÷ PTAX de referência), PTAX = Ptax de referência.
//     Preço unitário com até 4 casas (a A075 tem 345,5738): a tela da NRE guarda e mostra as 4.
//     Vários títulos na aba ("1 - ...", "2 - ..."), cada um com o seu cabeçalho = várias NRE.
//     No fechamento, "Tax = 10.67015204" é a taxa (Pis/Cofins, CSLL, R&D) da NRE, e a célula "MAQUINA" com o nome ao
//     lado ("MAQUINA | ADESIVADORA") é o Equipamento da NRE (pedido do usuário em 23/10/2026).
//   - AZUL (Summary): Project · Item/Project · Approved Value (USD) · Freight + Insurance (USD) · Ptax · Final Amount ·
//     Pis/Cofins... · Final Amount (BRL) + Taxes. As linhas são as DIs de UMA NRE: Project = Modelo, a soma do
//     Approved Value = Valor aprovado (US$; "Approved Value (BRL)" é em R$: vira US$ pela Ptax de referência da
//     amarela), a Ptax só se for a mesma em todas as linhas, a taxa = Pis/Cofins ÷ Final
//     Amount e o título junta os Item/Project ("Project A076 - Toolings SMD" + "... IM" = "... SMD, IM").
//     Só sem a coluna Freight + Insurance e com Approval Date / NRE Order / Status (planilha de controle) cada linha é
//     uma NRE. A coluna Approver sozinha não conta: na planilha da A076 ela está escondida e vazia (23/10/2026, o
//     importar separou a NRE em duas por causa dela).
//   Arquivo com uma azul e uma amarela = a mesma NRE. As colunas são achadas pelo nome do cabeçalho (em qualquer linha e
//   coluna da aba); célula com fórmula vale o número que o Excel mostra.
// NÃO vem da planilha: tipo, área (é a escolhida nos cartões), custo absorvido (só avisa se tiver) e os arquivos
// colados dentro do Excel (ícones da coluna email) — só são contados, para avisar. Sem a célula MAQUINA, o Equipamento
// fica como está (NRE nova: para preencher).
// ============================================================================
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MS.Modelos;

namespace MS.Servicos;

public static class ImportarNre
{
    // Uma NRE lida da planilha. Vazio (null) = a planilha não traz: o que a NRE já tem fica como está.
    public class Lida
    {
        public string Titulo { get; set; } = "";
        public bool TituloDaAba { get; set; }             // sem título na planilha: veio do nome da aba
        public string? Modelo { get; set; }
        public bool ModeloDoTitulo { get; set; }          // não veio da coluna Project: tirado do título (ex.: A076)
        public string? CodigoNre { get; set; }
        public string? Maquina { get; set; }              // Equipamento (tipo de máquina): "MAQUINA | ADESIVADORA" da amarela
        public DateTime? AprovadaEm { get; set; }
        public string? Aprovador { get; set; }
        public decimal? AprovadoUsd { get; set; }
        public decimal? AprovadoBrl { get; set; }         // "Approved Value (BRL)" (A075): vira US$ pela Ptax de referência
        public decimal? Ptax { get; set; }
        public List<NreRemessa>? Detalhado { get; set; }  // null = o arquivo não tem a amarela desta NRE
        public decimal? CustoPlanilha { get; set; }       // soma do R$ Total Price das linhas lidas (para conferir)
        public decimal? Taxa { get; set; }                // % Pis/Cofins, CSLL, R&D: "Tax = ..." da amarela ou taxas ÷ Final Amount da azul
        public decimal? TotalPlanilha { get; set; }       // Final Amount + Taxes da azul (para conferir)
        public List<decimal> PtaxesAzul { get; set; } = new();   // Ptax de cada linha da azul (uma por DI)
        public bool TemCustoAbsorvido { get; set; }       // a amarela tem custo absorvido pela empresa diferente de zero
        public bool DeControle { get; set; }              // linha da planilha de controle (uma NRE por linha)
        public List<string> Avisos { get; } = new();
        public string Origem { get; set; } = "";          // aba(s) de onde veio
    }

    // O que saiu do arquivo: as NRE e quantos arquivos estão colados dentro do Excel (não são importados).
    public record Leitura(List<Lida> Nres, int ArquivosDentro);

    public static Leitura Ler(Stream arquivo)
    {
        using var copia = new MemoryStream();
        arquivo.CopyTo(copia);
        copia.Position = 0;
        var dentro = ArquivosDentro(copia);
        copia.Position = 0;
        using var wb = new XLWorkbook(copia);
        var amarelas = new List<Lida>();
        var azuis = new List<Lida>();
        foreach (var ws in wb.Worksheets)
        {
            amarelas.AddRange(LerAmarela(ws));
            azuis.AddRange(LerAzul(ws));
        }
        var nres = Juntar(amarelas, azuis);
        foreach (var l in nres.Where(l => l.Modelo == null && ModeloNoTitulo.Match(l.Titulo.ToUpperInvariant()) is { Success: true }))
        {
            l.Modelo = ModeloNoTitulo.Match(l.Titulo.ToUpperInvariant()).Value;
            l.ModeloDoTitulo = true;
        }
        // Aprovado em R$ (A075): a NRE guarda o aprovado em US$ × Ptax. Usa a Ptax da azul ou, se todos os blocos da
        // amarela têm a mesma (a de referência, 5), essa: 513.093,40 ÷ 5 = US$ 102.618,68 (o "$ Total Price" da planilha).
        foreach (var l in nres.Where(l => l.AprovadoUsd == null && l.AprovadoBrl != null))
        {
            var ptaxes = (l.Detalhado ?? []).Select(r => r.Ptax).OfType<decimal>().Distinct().ToList();
            var ptax = l.Ptax ?? (ptaxes.Count == 1 ? ptaxes[0] : null);
            if (ptax is > 0)
            {
                l.AprovadoUsd = Nre.Centavos(l.AprovadoBrl!.Value / ptax.Value);
                l.Ptax = ptax;
            }
            else l.Avisos.Add($"o valor aprovado está em R$ ({l.AprovadoBrl.Value.Reais()}) e não há uma Ptax para passar para US$: preencha no Resumo.");
        }
        return new(nres.Where(l => l.Titulo != "" || l.Detalhado is { Count: > 0 }).ToList(), dentro);
    }

    // Arquivos colados dentro do Excel (e-mails, PDFs...) ficam na pasta xl/embeddings do .xlsx.
    static int ArquivosDentro(Stream s)
    {
        try
        {
            using var zip = new ZipArchive(s, ZipArchiveMode.Read, leaveOpen: true);
            return zip.Entries.Count(e => e.FullName.StartsWith("xl/embeddings/", StringComparison.OrdinalIgnoreCase) && e.Length > 0);
        }
        catch (InvalidDataException) { return 0; }
    }

    // ---------- NRE que já existe (importar de novo não duplica) ----------

    // A mesma NRE: pela NRE Order (se a planilha tem); senão pelo Item/Project (sem ligar para maiúscula, espaço e
    // pontuação: "SMD & IM" = "SMD, IM"). Pelas DIs NÃO: a mesma DI pode estar em outra NRE (em 23/10/2026 a planilha
    // da A076 foi parar na NRE-2026-0001, "VENUS4-14", que tinha as mesmas DIs, e trocou o título dela).
    public static Nre? Achar(Lida l, IEnumerable<Nre> nres)
    {
        var lista = nres.ToList();
        if (Chave(l.CodigoNre) is { Length: > 0 } ordem && lista.Where(n => Chave(n.CodigoNre) == ordem).ToList() is { Count: 1 } porOrdem)
            return porOrdem[0];
        var dis = Dis(l.Detalhado ?? []);
        if (Chave(l.Titulo) is { Length: > 0 } titulo && lista.Where(n => Chave(n.Titulo) == titulo).ToList() is { Count: > 0 } mesmoTitulo)
            return mesmoTitulo.OrderByDescending(n => Dis(n.Remessas).Overlaps(dis)).ThenByDescending(n => n.Id).First();
        return null;
    }

    static HashSet<string> Dis(IEnumerable<NreRemessa> rs) => rs.Select(r => Chave(r.Di)).Where(d => d != "").ToHashSet();

    // Só letras e números, em maiúscula e sem acento: para comparar título, DI e NRE Order.
    public static string Chave(string? s) => new(Excel.SemAcento(s ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    // ---------- juntar a azul com a amarela ----------

    static List<Lida> Juntar(List<Lida> amarelas, List<Lida> azuis)
    {
        // Arquivo de uma NRE só (uma amarela + um Summary): é a mesma NRE, mesmo com o título escrito diferente.
        if (amarelas.Count == 1 && azuis.Count == 1 && !azuis[0].DeControle)
        {
            Completar(amarelas[0], azuis[0]);
            return amarelas;
        }
        var todas = amarelas.ToList();
        foreach (var a in azuis)
        {
            var par = Chave(a.Titulo) == "" ? null : amarelas.FirstOrDefault(m => Chave(m.Titulo) == Chave(a.Titulo));
            if (par != null) Completar(par, a); else todas.Add(a);
        }
        return todas;
    }

    static void Completar(Lida amarela, Lida azul)
    {
        if ((amarela.TituloDaAba || amarela.Titulo == "") && azul.Titulo != "")
        {
            amarela.Titulo = azul.Titulo;
            amarela.TituloDaAba = false;
        }
        amarela.Modelo ??= azul.Modelo;
        amarela.CodigoNre ??= azul.CodigoNre;
        amarela.AprovadaEm ??= azul.AprovadaEm;
        amarela.Aprovador ??= azul.Aprovador;
        amarela.AprovadoUsd ??= azul.AprovadoUsd;
        amarela.AprovadoBrl ??= azul.AprovadoBrl;
        amarela.Ptax ??= azul.Ptax;
        amarela.PtaxesAzul = azul.PtaxesAzul;
        amarela.Taxa ??= azul.Taxa;   // a escrita na amarela ("Tax = ...") vale mais que a conta da azul
        amarela.TotalPlanilha ??= azul.TotalPlanilha;
        amarela.Origem += " + " + azul.Origem;
        amarela.Avisos.AddRange(azul.Avisos);
    }

    // ---------- planilha AMARELA (detalhe) ----------

    class ColunasAmarela
    {
        public int Material, Qtd;
        public int? UsdUnit, BrlUnit, UsdTotal, BrlTotal, Desconto, Acrescimo, Ptax, Di;
        public List<int> Invoices { get; } = new();
        public List<int> Nfs { get; } = new();          // "NF No." / "NF" (compra nacional, planilha da A075)
    }

    // Linha de cabeçalho da amarela: tem Material, Qtde e um preço unitário.
    static ColunasAmarela? CabecalhoAmarela(IXLRow row)
    {
        int? material = null, qtd = null;
        var c = new ColunasAmarela();
        foreach (var cel in row.CellsUsed())
        {
            var t = Norm(Texto(cel));
            var col = cel.Address.ColumnNumber;
            if (t == "") continue;
            if (t == "material") material ??= col;
            else if (t.StartsWith("qtd") || t is "quantidade" or "qty" or "quantity") qtd ??= col;
            else if (t.Contains("unit price")) { if (t.StartsWith("r$")) c.BrlUnit ??= col; else c.UsdUnit ??= col; }
            else if (t.Contains("total price")) { if (t.StartsWith("r$")) c.BrlTotal ??= col; else c.UsdTotal ??= col; }
            else if (t.StartsWith("invoice") && !t.Contains('&')) c.Invoices.Add(col);
            else if (Regex.IsMatch(t, @"^(nf|nota fiscal)\b") && !t.Contains('&')) c.Nfs.Add(col);
            else if (Regex.IsMatch(t, @"^di\b")) c.Di ??= col;
            else if (t.StartsWith("ptax")) c.Ptax ??= col;
            else if (t.StartsWith("desconto") || t.StartsWith("discount")) c.Desconto ??= col;
            else if (t.StartsWith("acrescimo")) c.Acrescimo ??= col;
        }
        if (material == null || qtd == null || (c.UsdUnit == null && c.BrlUnit == null)) return null;
        c.Material = material.Value;
        c.Qtd = qtd.Value;
        return c;
    }

    record LinhaItem(int Linha, string Material, decimal Qtd, decimal? UsdUnit, decimal? BrlUnit, decimal? UsdTotal,
        decimal? BrlTotal, string? Invoice, string? Nf, string? Di, decimal? Ptax, bool Frete, bool Seguro);

    static List<Lida> LerAmarela(IXLWorksheet ws)
    {
        var cabecalhos = new List<(int Linha, ColunasAmarela Colunas)>();
        foreach (var row in ws.RowsUsed())
            if (CabecalhoAmarela(row) is { } c) cabecalhos.Add((row.RowNumber(), c));
        var ultima = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lidas = new List<Lida>();
        for (var k = 0; k < cabecalhos.Count; k++)
        {
            var (linha, c) = cabecalhos[k];
            var fim = k + 1 < cabecalhos.Count ? cabecalhos[k + 1].Linha - 1 : ultima;
            var titulo = TituloAcima(ws, linha, k > 0 ? cabecalhos[k - 1].Linha : 0);
            var lida = new Lida { Titulo = titulo ?? SemNumero(ws.Name), TituloDaAba = titulo == null, Origem = $"aba \"{ws.Name}\"" };
            lida.Detalhado = Blocos(ws, c, linha + 1, fim, lida);
            // Fechamento e máquina: embaixo dos itens (no primeiro bloco também o que estiver acima do cabeçalho).
            LerFechamento(ws, k == 0 ? 1 : linha + 1, fim, lida);
            if (lida.Detalhado.Count > 0) lidas.Add(lida);
        }
        return lidas;
    }

    // Título do bloco: a linha de texto logo acima do cabeçalho ("1 - Project A076 - Toolings SMD, IM" → sem o "1 - ").
    static string? TituloAcima(IXLWorksheet ws, int cabecalho, int anterior)
    {
        for (var r = cabecalho - 1; r > anterior && r >= cabecalho - 6; r--)
        {
            var textos = ws.Row(r).CellsUsed().Select(Texto).Where(t => t != "").ToList();
            if (textos.Count is >= 1 and <= 2 && textos[0].Any(char.IsLetter)) return SemNumero(textos[0]);
        }
        return null;
    }

    // Fechamento da amarela: "Tax = 10.67015204" (taxa), "Custo absorvido pela empresa" com algum valor ≠ 0 (só avisa) e
    // "MAQUINA" com o nome da máquina na próxima célula preenchida à direita ("MAQUINA | ADESIVADORA" → Equipamento).
    static readonly HashSet<string> RotulosMaquina = ["maquina", "maquinas", "equipamento", "tipo de maquina", "machine", "equipment"];

    static void LerFechamento(IXLWorksheet ws, int de, int ate, Lida lida)
    {
        for (var r = de; r <= ate; r++)
        {
            var celulas = ws.Row(r).CellsUsed().OrderBy(x => x.Address.ColumnNumber).ToList();
            for (var i = 0; i < celulas.Count; i++)
            {
                var t = Norm(Texto(celulas[i]));
                if (TaxaTexto.Match(t) is { Success: true } tx && NumeroDoTexto(tx.Groups[1].Value, true) is decimal taxa && taxa is > 0 and < 100)
                    lida.Taxa = taxa;
                if (lida.Maquina == null && RotulosMaquina.Contains(t.TrimEnd(':', '.', ' ')))
                    lida.Maquina = celulas.Skip(i + 1).Select(Texto).FirstOrDefault(v => v.Any(char.IsLetter));
            }
            if (celulas.Any(x => Norm(Texto(x)).StartsWith("custo absorvido")) && celulas.Any(x => (Numero(x) ?? 0) != 0))
                lida.TemCustoAbsorvido = true;
        }
    }

    static string SemNumero(string t) => Regex.Replace(t, @"^\s*\d+(?:\.\d+)?\s*[-–—.)]\s*", "").Trim();

    static readonly Regex TaxaTexto = new(@"^tax\s*=\s*([\d.,]+)\s*%?$");
    static readonly Regex DiNoTexto =new(@"\bDI\b\D{0,6}(\d[\d/.\-]{4,}\d)", RegexOptions.IgnoreCase);
    // "OM03001-001617-112016400-Metal Mask": código = as três primeiras partes (a 2ª e a 3ª só com números).
    static readonly Regex CodigoMaterial = new(@"^\s*([A-Z0-9]*\d[A-Z0-9]*-\d+-\d+)\s*-\s*(.+)$", RegexOptions.IgnoreCase);
    // "Q430-107314 - APARELHO LEITOR" / "BA41-02893A - CIRCUITO" (A075): código com hífen, depois " - " e a descrição.
    // "SEM CODIGO - ..." e "CFOP6102 - ..." ficam inteiros na descrição.
    static readonly Regex CodigoTraco = new(@"^\s*([A-Z0-9]*\d[A-Z0-9]*-[A-Z0-9]*\d[A-Z0-9]*)\s+-\s+(.+)$", RegexOptions.IgnoreCase);
    static readonly Regex ModeloNoTitulo = new(@"\b[A-Z]\d{3}\b");

    static (string? Codigo, string Descricao) Separar(string material) =>
        CodigoMaterial.Match(material) is { Success: true } m ? (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim())
        : CodigoTraco.Match(material) is { Success: true } t ? (t.Groups[1].Value.Trim(), t.Groups[2].Value.Trim())
        : (null, material.Trim());

    // Preço unitário: até 4 casas (a tela da NRE guarda e mostra as 4; a A075 tem R$ 345,5738).
    static decimal Unitario(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);

    // As linhas com preço entre o cabeçalho e o fim do bloco, agrupadas pela DI (na ordem em que aparecem).
    static List<NreRemessa> Blocos(IXLWorksheet ws, ColunasAmarela c, int ini, int fim, Lida lida)
    {
        var linhas = new List<LinhaItem>();
        var temDesconto = false;
        for (var r = ini; r <= fim; r++)
        {
            var row = ws.Row(r);
            decimal? N(int? col, bool ptax = false) => col is int x ? Numero(row.Cell(x), ptax) : null;
            string? T(int? col) => col is int x && Texto(row.Cell(x)) is { Length: > 0 } s ? s : null;
            if ((N(c.Desconto) ?? 0) != 0 || (N(c.Acrescimo) ?? 0) != 0) temDesconto = true;
            var material = T(c.Material);
            var usd = N(c.UsdUnit);
            var brl = N(c.BrlUnit);
            if (material == null || (usd == null && brl == null)) continue;
            var m = Norm(material);
            var frete = Regex.IsMatch(m, @"^(frete|freight)\b");
            var seguro = Regex.IsMatch(m, @"^(seguro|insurance)\b");
            var di = T(c.Di);
            if (di == null && (frete || seguro) && DiNoTexto.Match(material) is { Success: true } d) di = d.Groups[1].Value;
            linhas.Add(new(r, material, N(c.Qtd) ?? 1, usd, brl, N(c.UsdTotal), N(c.BrlTotal),
                c.Invoices.Select(x => T(x)).FirstOrDefault(x => x != null), c.Nfs.Select(x => T(x)).FirstOrDefault(x => x != null),
                di, N(c.Ptax, true), frete, seguro));
        }

        // Grupo = DI; sem DI: pela NF (compra nacional, A075), pela invoice (importação, com US$ ou PTAX) ou compra
        // nacional. Frete/seguro sem DI fica com o grupo da linha de cima.
        var ordem = new List<string>();
        var grupos = new Dictionary<string, List<LinhaItem>>();
        string? anterior = null;
        foreach (var l in linhas)
        {
            var chave = l.Di != null ? "DI|" + Chave(l.Di)
                : (l.Frete || l.Seguro) && anterior != null ? anterior
                : l.Nf != null ? "NAC|" + Chave(l.Nf)
                : (l.UsdUnit != null || l.Ptax != null ? "IMP|" : "NAC|") + Chave(l.Invoice);
            if (!grupos.TryGetValue(chave, out var g)) { grupos[chave] = g = new(); ordem.Add(chave); }
            g.Add(l);
            anterior = chave;
        }

        var blocos = new List<NreRemessa>();
        foreach (var chave in ordem)
        {
            var ls = grupos[chave];
            var imp = !chave.StartsWith("NAC|");
            var r = new NreRemessa { Ordem = blocos.Count, Tipo = imp ? TipoRemessa.Importacao : TipoRemessa.Nacional };
            string? Juntar(Func<LinhaItem, string?> campo) => ls.Where(x => !x.Frete && !x.Seguro).Select(campo).OfType<string>()
                .Select(s => s.ToUpperInvariant()).Distinct().ToList() is { Count: > 0 } v ? string.Join(", ", v) : null;
            var invoice = Juntar(x => x.Invoice);
            if (imp) { r.Di = ls.Select(x => x.Di).FirstOrDefault(x => x != null)?.ToUpperInvariant(); r.Invoice = invoice; }
            else r.NotaFiscal = Juntar(x => x.Nf) ?? invoice;
            var ptaxes = ls.Select(x => x.Ptax).OfType<decimal>().Where(p => p > 0).Distinct().ToList();
            r.Ptax = ptaxes.Count > 0 ? ptaxes[0] : null;
            if (ptaxes.Count > 1)
                lida.Avisos.Add($"{r.Rotulo()} tem PTAX diferentes na planilha ({string.Join(" e ", ptaxes.Select(p => p.Ptax()))}); ficou {ptaxes[0].Ptax()}.");
            foreach (var l in ls)
            {
                if (imp && (l.Frete || l.Seguro))
                {
                    var v = Nre.Centavos(l.UsdTotal ?? l.Qtd * (l.UsdUnit ?? 0));
                    if (l.Frete) r.Frete += v; else r.Seguro += v;
                    continue;
                }
                var (codigo, descricao) = Separar(l.Material);
                var item = new NreItem { Codigo = codigo?.ToUpperInvariant(), Descricao = descricao, Quantidade = l.Qtd };
                // Importação: US$ (Unit Price $). Nacional: R$ (R$ Unit Price; sem ele, R$ total ÷ Qtde ou US$ × PTAX).
                if (imp && l.UsdUnit != null) item.ValorUnitarioUsd = Unitario(l.UsdUnit.Value);
                else item.ValorUnitario = Unitario(l.BrlUnit ?? (l.BrlTotal is decimal bt && l.Qtd != 0 ? bt / l.Qtd : (l.UsdUnit ?? 0) * (r.Ptax ?? 0)));
                // O sistema guarda o preço com até 4 casas: se a planilha tem mais, o total da linha pode mudar.
                if (item.ValorUnitarioUsd != null && l.UsdTotal is decimal t && Math.Abs(item.TotalUsd() - t) > 0.01m)
                    lida.Avisos.Add($"linha {l.Linha} ({descricao}): $ Total Price {t.Dolares()} na planilha e {item.TotalUsd().Dolares()} no sistema.");
                else if (item.ValorUnitarioUsd == null && l.BrlTotal is decimal tb && Math.Abs(item.Total() - tb) > 0.01m)
                    lida.Avisos.Add($"linha {l.Linha} ({descricao}): R$ Total Price {tb.Reais()} na planilha e {item.Total().Reais()} no sistema.");
                r.Itens.Add(item);
            }
            blocos.Add(r);
        }
        lida.CustoPlanilha = linhas.Count > 0 && linhas.All(l => l.BrlTotal != null) ? linhas.Sum(l => l.BrlTotal!.Value) : null;
        if (temDesconto) lida.Avisos.Add("a planilha tem Desconto ou Acréscimo: o sistema não tem esses campos, confira o total.");
        return blocos;
    }

    // ---------- planilha AZUL (Summary / controle) ----------

    class ColunasAzul
    {
        public int Aprovado, Item;
        public bool AprovadoEmReais;                      // "Approved Value (BRL)" (A075) em vez de "(USD)"
        public int? Projeto, Frete, Ptax, Final, Taxas, TotalTaxas, Data, Aprovador, Ordem, Status;
        // Planilha de controle (uma NRE por linha): sem Freight + Insurance (o Summary tem) e com data, NRE Order ou
        // status. Approver sozinho não conta (no Summary da A076 ele está escondido e vazio).
        public bool Controle => Frete == null && (Data != null || Ordem != null || Status != null);
    }

    // Linha de cabeçalho da azul: tem Item/Project e Approved Value.
    static ColunasAzul? CabecalhoAzul(IXLRow row)
    {
        int? aprovado = null, item = null;
        var c = new ColunasAzul();
        foreach (var cel in row.CellsUsed())
        {
            var t = Norm(Texto(cel));
            var col = cel.Address.ColumnNumber;
            if (t == "") continue;
            var junto = t.Replace(" ", "");
            if ((t.StartsWith("approved value") || t.StartsWith("valor aprovado")) && aprovado == null)
            {
                aprovado = col;
                c.AprovadoEmReais = (t.Contains("brl") || t.Contains("r$")) && !t.Contains("usd") && !t.Contains("us$");
            }
            else if (junto is "item/project" or "item/projeto") item ??= col;
            else if (t is "project" or "projeto" or "model" or "modelo") c.Projeto ??= col;
            else if (t.StartsWith("freight") || t.StartsWith("frete")) c.Frete ??= col;
            else if (t.StartsWith("ptax")) c.Ptax ??= col;
            else if (t.StartsWith("final amount") && t.Contains("tax")) c.TotalTaxas ??= col;
            else if (t.StartsWith("final amount")) c.Final ??= col;
            else if (t.StartsWith("pis")) c.Taxas ??= col;
            else if (t.Contains("approval date")) c.Data ??= col;
            else if (t.StartsWith("approver")) c.Aprovador ??= col;
            else if (t.StartsWith("nre order")) c.Ordem ??= col;
            else if (t == "status") c.Status ??= col;
        }
        if (aprovado == null || item == null) return null;
        c.Aprovado = aprovado.Value;
        c.Item = item.Value;
        return c;
    }

    record LinhaAzul(string Item, string? Projeto, decimal? Aprovado, decimal? Ptax, decimal? Final, decimal? Taxas, decimal? Total,
        DateTime? Data, string? Aprovador, string? Ordem);

    static List<Lida> LerAzul(IXLWorksheet ws)
    {
        var lidas = new List<Lida>();
        var ultima = ws.LastRowUsed()?.RowNumber() ?? 0;
        foreach (var cab in ws.RowsUsed().ToList())
        {
            if (CabecalhoAzul(cab) is not { } c) continue;
            var linhas = new List<LinhaAzul>();
            var vazias = 0;
            // Até a linha "Total" (ou outro cabeçalho, ou 5 linhas vazias seguidas).
            for (var r = cab.RowNumber() + 1; r <= ultima && vazias < 5; r++)
            {
                var row = ws.Row(r);
                var textos = row.CellsUsed().Select(Texto).Where(x => x != "").ToList();
                if (textos.Count == 0) { vazias++; continue; }
                vazias = 0;
                if (textos.Any(x => Norm(x) == "total") || CabecalhoAzul(row) != null || CabecalhoAmarela(row) != null) break;
                decimal? N(int? col, bool ptax = false) => col is int x ? Numero(row.Cell(x), ptax) : null;
                string? T(int? col) => col is int x && Texto(row.Cell(x)) is { Length: > 0 } s ? s : null;
                var item = T(c.Item) is string it ? SemNumero(it) : null;   // "1. NFs SEDA" → "NFs SEDA" (como o título da amarela)
                var aprovado = N(c.Aprovado);
                if (item == null && aprovado == null) continue;
                linhas.Add(new(item ?? "", T(c.Projeto), aprovado, N(c.Ptax, true), N(c.Final), N(c.Taxas), N(c.TotalTaxas),
                    c.Data is int dc ? Data(row.Cell(dc)) : null, T(c.Aprovador), T(c.Ordem)));
            }
            if (linhas.Count == 0) continue;
            var origem = $"aba \"{ws.Name}\"";
            if (c.Controle)
            {
                // Planilha de controle: uma NRE por linha.
                foreach (var l in linhas.Where(l => l.Item != ""))
                    lidas.Add(new Lida
                    {
                        Titulo = l.Item, Modelo = l.Projeto, Ptax = l.Ptax,
                        AprovadoUsd = !c.AprovadoEmReais && l.Aprovado is decimal a ? Nre.Centavos(a) : null,
                        AprovadoBrl = c.AprovadoEmReais && l.Aprovado is decimal ab ? Nre.Centavos(ab) : null,
                        AprovadaEm = l.Data, Aprovador = l.Aprovador, CodigoNre = l.Ordem, DeControle = true, Origem = origem,
                        Taxa = TaxaDa(l.Taxas, l.Final), TotalPlanilha = l.Total,
                    });
                continue;
            }
            // Summary: as linhas são as DIs de uma NRE só.
            var lida = new Lida { Titulo = JuntarTitulos(linhas.Select(l => l.Item)), Origem = origem };
            var projetos = linhas.Select(l => l.Projeto).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            lida.Modelo = projetos.FirstOrDefault();
            if (projetos.Count > 1) lida.Avisos.Add($"a planilha azul tem mais de um Project ({string.Join(", ", projetos)}); ficou {projetos[0]}.");
            lida.Aprovador = linhas.Select(l => l.Aprovador).FirstOrDefault(a => a != null);
            var aprovadoTotal = linhas.Any(l => l.Aprovado != null) ? Nre.Centavos(linhas.Sum(l => l.Aprovado ?? 0)) : (decimal?)null;
            if (c.AprovadoEmReais) lida.AprovadoBrl = aprovadoTotal; else lida.AprovadoUsd = aprovadoTotal;
            // A NRE guarda uma Ptax de aprovação só (Final Amount = aprovado × Ptax): com uma por DI, não muda.
            lida.PtaxesAzul = linhas.Select(l => l.Ptax).OfType<decimal>().Where(p => p > 0).Distinct().ToList();
            lida.Ptax = lida.PtaxesAzul.Count == 1 ? lida.PtaxesAzul[0] : null;
            lida.Taxa = TaxaDa(linhas.Sum(l => l.Taxas ?? 0), linhas.Sum(l => l.Final ?? 0));
            lida.TotalPlanilha = linhas.Any(l => l.Total != null) ? linhas.Sum(l => l.Total ?? 0) : null;
            lidas.Add(lida);
        }
        return lidas;
    }

    // % das taxas da azul: Pis/Cofins ÷ Final Amount (A076: 26.163,54 ÷ 245.203,10 = 10,67015204%), 8 casas.
    static decimal? TaxaDa(decimal? taxas, decimal? final) =>
        final > 0 && taxas > 0 && Math.Round(taxas.Value / final.Value * 100, 8) is var t && t < 100 ? t : null;

    // "Project A076 - Toolings SMD" + "Project A076 - Toolings IM" → "Project A076 - Toolings SMD, IM".
    static string JuntarTitulos(IEnumerable<string> titulos)
    {
        var t = titulos.Select(x => x.Trim()).Where(x => x != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (t.Count <= 1) return t.FirstOrDefault() ?? "";
        var n = 0;
        while (t.All(x => x.Length > n && char.ToUpperInvariant(x[n]) == char.ToUpperInvariant(t[0][n]))) n++;
        var corte = t[0][..n].LastIndexOf(' ') + 1;
        return corte < 4 ? string.Join(" / ", t) : t[0][..corte] + string.Join(", ", t.Select(x => x[corte..].Trim()));
    }

    // ---------- células ----------

    public static string Norm(string s) => Regex.Replace(Excel.SemAcento(s).ToLowerInvariant(), @"\s+", " ").Trim();

    // Valor da célula; com fórmula, o que o Excel guardou (o que aparece na tela). Arquivo sem o valor guardado (feito
    // por outro programa): o ClosedXML calcula a fórmula.
    static XLCellValue Valor(IXLCell c)
    {
        try
        {
            if (!c.HasFormula) return c.Value;
            var v = c.CachedValue;
            if (!v.IsBlank) return v;
            c.InvalidateFormula();
            return c.Value;
        }
        catch (Exception) { return Blank.Value; }
    }

    static string Texto(IXLCell c)
    {
        var v = Valor(c);
        if (v.IsText) return v.GetText().Trim();
        if (v.IsNumber)
        {
            var d = v.GetNumber();
            return d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString(CultureInfo.InvariantCulture);
        }
        if (v.IsDateTime) return v.GetDateTime().ToString("dd/MM/yyyy");
        return "";
    }

    static decimal? Numero(IXLCell c, bool ptax = false)
    {
        var v = Valor(c);
        try
        {
            if (v.IsNumber) return (decimal)v.GetNumber();
        }
        catch (OverflowException) { return null; }
        return v.IsText ? NumeroDoTexto(v.GetText(), ptax) : null;
    }

    // Número escrito como texto: "$ 1,978.31", "R$ 49.182,13", "5,3795". Valor: o último ponto ou vírgula separa os
    // centavos quando tem 1 ou 2 dígitos depois dele. PTAX: a vírgula ou o ponto é o decimal.
    static decimal? NumeroDoTexto(string s, bool ptax)
    {
        var t = new string(s.Where(ch => char.IsAsciiDigit(ch) || ch is ',' or '.').ToArray());
        if (!t.Any(char.IsAsciiDigit)) return null;
        var negativo = s.TrimStart().StartsWith('-') || s.Contains('(');
        if (ptax && t.Count(ch => ch is ',' or '.') <= 1) t = t.Replace(',', '.');
        else
        {
            var ultimo = t.LastIndexOfAny([',', '.']);
            var casas = ultimo < 0 ? 0 : t.Length - ultimo - 1;
            t = casas is 1 or 2 ? t[..ultimo].Replace(",", "").Replace(".", "") + "." + t[(ultimo + 1)..] : t.Replace(",", "").Replace(".", "");
        }
        return decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? (negativo ? -v : v) : null;
    }

    static DateTime? Data(IXLCell c)
    {
        var v = Valor(c);
        if (v.IsDateTime) return v.GetDateTime().Date;
        if (v.IsNumber && v.GetNumber() is > 30000 and < 80000) return DateTime.FromOADate(v.GetNumber()).Date;
        if (v.IsText && DateTime.TryParse(v.GetText(), new CultureInfo("pt-BR"), DateTimeStyles.None, out var d)) return d.Date;
        return null;
    }
}
