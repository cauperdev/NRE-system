// ============================================================================
// RESUMO DA NRE — as linhas da aba "Summary" da planilha de NRE do cliente: uma por DI / compra nacional do Detalhado
// (sem Detalhado ainda, uma linha com a previsão da aprovação) e o Excel do botão Extração da NRE.
// Usado em: aba Resumo da NRE (Pages/Nre/Editar). As contas ficam em Modelos/Entidades.cs (Nre e NreRemessa).
// Botão "Extração" da NRE → Planilha: um Excel com duas abas, no MODELO DAS PLANILHAS DO CLIENTE (pedidos do usuário em
// 24/10/2026, com os prints dos modelos; antes eram os botões "Baixar resumo" e "Baixar detalhado"):
//   aba Resumo    → Resumo    (planilha azul: "Resumo de Custos + Impostos", a linha da NRE + Total)
//   aba Detalhado → Detalhado (planilha amarela: Item · Material · Qtde · ... · R$ Total NF · PTAX · Invoice No. ·
//                   DI No. · Invoice & DI com os arquivos, frete/seguro, custo absorvido e o quadro do total da NF)
// A Extração da lista de NRE (várias NRE numa planilha) saiu em 24/10/2026, a pedido do usuário.
// ============================================================================
using System.Globalization;
using ClosedXML.Excel;
using MS.Modelos;

namespace MS.Servicos;

public static class ResumoNre
{
    // Uma linha do resumo. Valores em reais vazios = DI ainda sem PTAX (não dá para converter).
    public record Linha(Nre Nre, string Rotulo, string? Invoice, decimal? AprovadoUsd, decimal FreteSeguroUsd, decimal? Ptax,
        decimal? FinalReais, decimal? Taxas, bool Previsao, bool PtaxPendente)
    {
        public decimal? Total => FinalReais + Taxas;
    }

    public static List<Linha> Linhas(Nre n)
    {
        if (n.TemDetalhe)
            return n.RemessasEmOrdem.Select(r => new Linha(n, r.Rotulo(), r.Invoice, r.ItensUsd(), r.FreteSeguroUsd(), r.Ptax,
                r.PtaxPendente ? null : r.Reais(), r.PtaxPendente ? null : r.Taxas(n.Taxa), false, r.PtaxPendente)).ToList();
        if (n.Previsto() is decimal previsto)
            return [new Linha(n, "Previsão (aprovação)", null, n.AprovadoUsd, 0, n.PtaxAprovacao, previsto, n.TaxasReais(), true, false)];
        return n.AprovadoUsd != null ? [new Linha(n, "Previsão (aprovação)", null, n.AprovadoUsd, 0, n.PtaxAprovacao, null, null, true, false)] : [];
    }

    // ---------- Formatos e cores dos modelos do cliente ----------
    // Contábil, como nos modelos: "R$ 160.525,18", "$ 16.783,01" e zero como "R$ -".
    const string Reais = "_-\"R$\"* #,##0.00_-;-\"R$\"* #,##0.00_-;_-\"R$\"* \"-\"??_-;_-@_-";
    const string Dolar = "_-\"$\"* #,##0.00_-;-\"$\"* #,##0.00_-;_-\"$\"* \"-\"??_-;_-@_-";
    const string FormatoPtax = "0.0000";
    static readonly XLColor Verde = XLColor.FromHtml("#E2EFDA");      // título do resumo, linha do grupo, custo absorvido
    static readonly XLColor AzulCab = XLColor.FromHtml("#D9E1F2");    // cabeçalhos
    static readonly XLColor AzulItem = XLColor.FromHtml("#DDEBF7");   // Item e Material das linhas de itens
    static readonly XLColor Pessego = XLColor.FromHtml("#FCE4D6");    // coluna das taxas, diferença, R$ Total NF
    static readonly XLColor Creme = XLColor.FromHtml("#FFF2CC");      // frete, seguro, outras despesas
    static readonly XLColor AmareloForte = XLColor.FromHtml("#FFFF00");
    static readonly XLColor Indigo = XLColor.FromHtml("#1A1740"), Azul = XLColor.FromHtml("#6D28D9"), Cinza = XLColor.FromHtml("#52525B");

    static void Valor(IXLWorksheet ws, int linha, int coluna, decimal? v, string formato)
    {
        var c = ws.Cell(linha, coluna);
        c.Style.NumberFormat.Format = formato;   // formato mesmo vazio: a célula fica pronta como no modelo
        if (v is decimal d) c.Value = d;
    }

    static void Grade(IXLRange r)
    {
        r.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        r.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }

    // Evidências (pedido do usuário em 15/10/2026): o título na célula (linha, coluna) e embaixo TODOS os arquivos da aba
    // Arquivos, agrupados por tipo na ordem da tela, um por linha, com link que abre o arquivo no sistema (endereco =
    // "EnderecoSistema" do appsettings.json; pede o login se ainda não entrou). Depois a tela troca os links pelos próprios
    // arquivos dentro do Excel (ExcelArquivos.Embutir); os links ficam só se o Excel do computador falhar.
    // Só no Detalhado, na coluna "Invoice & DI" do modelo (N); o Resumo não leva arquivos (pedido do usuário em 24/10/2026).
    public const int LinhaEvidenciasDetalhado = 4, ColunaEvidenciasDetalhado = 14;
    static readonly (TipoAnexoNre Tipo, string Nome)[] GruposArquivos =
        [(TipoAnexoNre.Invoice, "Invoices"), (TipoAnexoNre.Di, "DIs"), (TipoAnexoNre.NotaFiscal, "NF"),
         (TipoAnexoNre.Evidencia, "Evidências"), (TipoAnexoNre.Outro, "Outros")];
    public static List<(NreAnexo Anexo, string Grupo)> ArquivosDaPlanilha(Nre n) =>
        GruposArquivos.SelectMany(g => n.Anexos.Where(a => a.Tipo == g.Tipo).OrderBy(a => a.EnviadoEm).ThenBy(a => a.Id)
            .Select(a => (a, g.Nome))).ToList();

    static void Evidencias(IXLWorksheet ws, Nre n, string endereco, int linha, int coluna, string titulo, double largura)
    {
        var t = ws.Cell(linha, coluna);
        t.Value = titulo;
        t.Style.Fill.BackgroundColor = AzulCab;
        t.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        t.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        t.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        var arquivos = ArquivosDaPlanilha(n);
        if (arquivos.Count == 0)
        {
            var c = ws.Cell(linha + 1, coluna);
            c.Value = "Nenhum arquivo — anexe na aba Arquivos da NRE";
            c.Style.Font.FontColor = Cinza;
            c.Style.Font.FontSize = 9;
        }
        string? grupo = null;
        foreach (var (a, g) in arquivos)
        {
            if (g != grupo)
            {
                grupo = g;
                var rotulo = ws.Cell(++linha, coluna);
                rotulo.Value = g;
                rotulo.Style.Font.Bold = true;
                rotulo.Style.Font.FontColor = Indigo;
            }
            var c = ws.Cell(++linha, coluna);
            c.Value = a.Nome;
            c.SetHyperlink(new XLHyperlink(new Uri($"{endereco}/Nre/Editar/{n.Id}?handler=Anexo&anexo={a.Id}")));
            c.Style.Font.FontColor = Azul;
            c.Style.Font.Underline = XLFontUnderlineValues.Single;
        }
        ws.Column(coluna).Width = largura;
    }

    // ---------- Botão "Extração" da NRE: um Excel com as abas Resumo e Detalhado ----------
    // (Pedido do usuário em 24/10/2026: no lugar dos botões "Baixar resumo" e "Baixar detalhado".) Os arquivos da NRE
    // entram depois na aba Detalhado (ExcelArquivos.Embutir com aba = AbaDetalhado).
    public const string AbaDetalhado = "Detalhado";
    public static byte[] Planilha(Nre n, string endereco)
    {
        using var wb = new XLWorkbook();
        Resumo(wb.Worksheets.Add("Resumo"), n);
        Detalhado(wb.Worksheets.Add(AbaDetalhado), n, endereco);
        foreach (var ws in wb.Worksheets)
        {
            ws.ShowGridLines = false;   // como os modelos: só as bordas das tabelas
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.FitToPages(1, 0);
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ---------- Aba Resumo: modelo da planilha azul ----------
    // Linha 1 "Resumo de Custos + Impostos" (verde) · linha 2 cabeçalho · linha 3 a NRE · linha 5 Total.
    // Colunas: Project (modelo) · Item/Project (título) · Approved Value (USD) · Ptax · Final Amount (BRL) = aprovado × Ptax ·
    // Pis/Confins, CSSL, R&D Tax(BRL) = Final Amount × taxa · Final Amount (BRL) + Taxes · Approver. Os nomes das colunas
    // são os do modelo do cliente, como estão lá. Sem valor aprovado ainda: os números do Detalhado (custo real).
    // Sem os arquivos (pedido do usuário em 24/10/2026): eles saem só no Detalhado, na coluna Invoice & DI.
    static void Resumo(IXLWorksheet ws, Nre n)
    {

        var titulo = ws.Range(1, 1, 1, 8).Merge();
        ws.Cell(1, 1).Value = "Resumo de Custos + Impostos";
        titulo.Style.Font.Bold = true;
        titulo.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titulo.Style.Fill.BackgroundColor = Verde;
        titulo.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

        string[] cab = ["Project", "Item/Project", "Approved Value (USD)", "Ptax", "Final Amount (BRL)",
                        "Pis/Confins, CSSL, R&D Tax(BRL)", "Final Amount (BRL) + Taxes", "Approver"];
        for (var c = 0; c < cab.Length; c++) ws.Cell(2, c + 1).Value = cab[c];
        var cabecalho = ws.Range(2, 1, 2, 8);
        cabecalho.Style.Font.Bold = true;
        cabecalho.Style.Fill.BackgroundColor = AzulCab;
        cabecalho.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cabecalho.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        cabecalho.Style.Alignment.WrapText = true;
        ws.Cell(2, 6).Style.Fill.BackgroundColor = Pessego;

        // A NRE: a previsão da aprovação (como a planilha azul) ou, sem valor aprovado, o custo real do Detalhado.
        var aprovacao = n.Previsto() != null;
        decimal? final = aprovacao ? n.Previsto() : n.TemDetalhe ? n.FinalAmount() : null;
        decimal? taxas = aprovacao ? n.TaxasPrevistas() : n.TemDetalhe ? n.TaxasReais() : null;
        ws.Cell(3, 1).Value = n.Modelo ?? "";
        ws.Cell(3, 2).Value = n.Titulo ?? n.Numero;
        Valor(ws, 3, 3, n.AprovadoUsd ?? n.AprovadoUsdResumo(), Dolar);
        Valor(ws, 3, 4, n.PtaxAprovacao, Reais);
        Valor(ws, 3, 5, final, Reais);
        Valor(ws, 3, 6, taxas, Reais);
        Valor(ws, 3, 7, final + taxas, Reais);
        ws.Cell(3, 8).Value = n.Aprovador ?? "";
        ws.Range(3, 1, 3, 8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Cell(3, 2).Style.Alignment.WrapText = true;
        Grade(ws.Range(2, 1, 3, 8));

        // Linha 5: Total (como o modelo: Ptax repetida, os valores em reais somados).
        ws.Cell(5, 1).Value = "Total";
        Valor(ws, 5, 4, n.PtaxAprovacao, Reais);
        Valor(ws, 5, 5, final, Reais);
        Valor(ws, 5, 6, taxas, Reais);
        Valor(ws, 5, 7, final + taxas, Reais);
        ws.Range(5, 1, 5, 8).Style.Font.Bold = true;
        Grade(ws.Range(5, 1, 5, 8));

        ws.Row(2).Height = 30;
        ws.Row(3).Height = 30;
        ws.Column(1).Width = 14;
        ws.Column(2).Width = 30;
        ws.Column(3).Width = 24;
        ws.Column(4).Width = 11;
        ws.Columns(5, 7).Width = 16;
        ws.Column(8).Width = 14;
    }

    // ---------- Aba Detalhado: modelo da planilha amarela ----------
    // Linha 2 "1. <título>" (amarelo) · linha 4 cabeçalho · linha 5 o grupo (somas) · itens 1.1, 1.2... de todos os blocos
    // do Detalhado, na ordem (cada um com a PTAX, a invoice — ou a NF, na compra nacional — e a DI do bloco) · 3 linhas
    // vazias · Frete, Seguro, Outras despesas (somas das DIs) · Custo absorvido pela empresa · Diferença para o aprovado ·
    // quadro R$ Total NF (Total, Pis/Cofins + "Tax = taxa", VALOR TOTAL DA NF; sem a frase do frete e seguro, a pedido). Coluna N (Invoice & DI): os arquivos.
    // Os valores são os do sistema (cada linha arredondada no centavo, como as contas da tela), não fórmulas.
    static void Detalhado(IXLWorksheet ws, Nre n, string endereco)
    {
        const int ultima = 14;   // A..N

        var titulo = ws.Range(2, 1, 2, 2).Merge();
        ws.Cell(2, 1).Value = $"1. {n.Titulo ?? n.Numero}";
        titulo.Style.Fill.BackgroundColor = AmareloForte;
        titulo.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

        string[] cab = ["Item", "Material", "Qtde", "Unit Price $", "R$ Unit Price", "$ Total Price", "R$ Total Price",
                        "Desconto", "Acrescimo", "R$ Total NF", "PTAX", "Invoice No.", "DI No.", "Invoice & DI"];
        for (var c = 0; c < cab.Length; c++) ws.Cell(4, c + 1).Value = cab[c];
        var cabecalho = ws.Range(4, 1, 4, ultima);
        cabecalho.Style.Fill.BackgroundColor = AzulCab;
        cabecalho.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(4, 1, 4, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        ws.Cell(4, 10).Style.Fill.BackgroundColor = AmareloForte;
        Grade(cabecalho);

        // Itens de todos os blocos (DI / compra nacional), na ordem da tela.
        var itens = n.RemessasEmOrdem.SelectMany(r => r.Itens.Select(i => (Bloco: r, Item: i))).ToList();
        var l = 6;
        var k = 0;
        foreach (var (r, i) in itens)
        {
            var emDolar = r.Importacao && i.ValorUnitarioUsd != null;
            ws.Cell(l, 1).Value = $"1.{++k}";
            ws.Cell(l, 2).Value = i.Codigo is { Length: > 0 } cod ? $"{cod} - {i.Descricao}" : i.Descricao;
            ws.Cell(l, 3).Value = i.Quantidade;
            if (emDolar)
            {
                Valor(ws, l, 4, i.ValorUnitarioUsd, Dolar);
                Valor(ws, l, 5, r.Ptax is decimal p ? Nre.Centavos(i.ValorUnitarioUsd!.Value * p) : null, Reais);
                Valor(ws, l, 6, i.TotalUsd(), Dolar);
                Valor(ws, l, 7, r.PtaxPendente ? null : r.ReaisItem(i), Reais);
            }
            else
            {
                Valor(ws, l, 4, null, Dolar);
                Valor(ws, l, 5, i.ValorUnitario, Reais);
                Valor(ws, l, 6, null, Dolar);
                Valor(ws, l, 7, r.ReaisItem(i), Reais);
            }
            if (r.Ptax is decimal ptax) Valor(ws, l, 11, ptax, FormatoPtax);
            else if (emDolar) ws.Cell(l, 11).Value = "pendente";
            ws.Cell(l, 12).Value = r.Importacao ? r.Invoice ?? "" : r.NotaFiscal is { Length: > 0 } nf ? $"NF {nf}" : "";
            ws.Cell(l, 13).Value = r.Importacao ? r.Di ?? "" : "";
            l++;
        }
        var fimItens = l + 2;   // + 3 linhas vazias, como o modelo
        var linhasItens = ws.Range(6, 1, fimItens, ultima - 1);
        Grade(linhasItens);
        linhasItens.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Range(6, 1, fimItens, 2).Style.Fill.BackgroundColor = AzulItem;
        ws.Range(6, 2, fimItens, 2).Style.Alignment.WrapText = true;
        ws.Range(6, 1, fimItens, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(6, 3, fimItens, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(6, 11, fimItens, 13).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // Linha 5: o grupo, com as somas dos itens (sem frete e seguro, como o modelo).
        var itensReais = itens.Where(x => !(x.Bloco.PtaxPendente && x.Item.ValorUnitarioUsd != null)).Sum(x => x.Bloco.ReaisItem(x.Item));
        ws.Cell(5, 1).Value = 1;
        ws.Cell(5, 2).Value = n.Tipo == TipoNre.Peca ? "Itens consumíveis" : n.Tipo.Nome();
        ws.Cell(5, 3).Value = itens.Sum(x => x.Item.Quantidade);
        Valor(ws, 5, 6, itens.Where(x => x.Bloco.Importacao).Sum(x => x.Item.TotalUsd()), Dolar);
        Valor(ws, 5, 7, itensReais, Reais);
        Valor(ws, 5, 8, 0, Reais);
        Valor(ws, 5, 9, 0, Reais);
        Valor(ws, 5, 10, itensReais, Reais);
        var grupo = ws.Range(5, 1, 5, 10);
        grupo.Style.Font.Bold = true;
        grupo.Style.Fill.BackgroundColor = Verde;
        ws.Cell(5, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(5, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        Grade(ws.Range(5, 1, 5, ultima - 1));

        // Frete, Seguro e Outras despesas: as somas das DIs (US$ e R$ pela PTAX de cada DI).
        l = fimItens + 1;
        var dis = n.RemessasEmOrdem.Where(r => r.Importacao).ToList();
        var inicioDespesas = l;
        foreach (var (nome, usd, brl) in new (string, decimal, decimal)[]
        {
            ("Frete", dis.Sum(r => r.Frete), dis.Sum(r => r.Reais(r.Frete) ?? 0)),
            ("Seguro", dis.Sum(r => r.Seguro), dis.Sum(r => r.Reais(r.Seguro) ?? 0)),
            ("Outras despesas", 0, 0),
        })
        {
            ws.Cell(l, 2).Value = nome;
            if (usd > 0) { Valor(ws, l, 6, usd, Dolar); Valor(ws, l, 7, brl, Reais); Valor(ws, l, 10, brl, Reais); }
            l++;
        }
        var despesas = ws.Range(inicioDespesas, 1, l - 1, ultima - 1);
        despesas.Style.Fill.BackgroundColor = Creme;
        Grade(despesas);
        // Coluna N (Invoice & DI) sem a faixa creme aqui (pedido do usuário em 24/10/2026): os arquivos podem descer até
        // estas linhas.
        l++;

        // Custo absorvido pela empresa.
        ws.Cell(l, 2).Value = "Custo absorvido pela empresa";
        ws.Cell(l, 2).Style.Font.Bold = true;
        ws.Cell(l, 2).Style.Fill.BackgroundColor = Verde;
        Valor(ws, l, 7, n.CustoAbsorvido ?? 0, Reais);
        Valor(ws, l, 8, 0, Reais);
        Valor(ws, l, 9, 0, Reais);
        ws.Cell(l, 10).Style.Fill.BackgroundColor = AmareloForte;
        Grade(ws.Range(l, 1, l, ultima - 1));   // sem a coluna N: lá ficam os arquivos (pedido do usuário)
        l++;

        // Diferença entre o aprovado e o real (US$ em F, R$ em J) e o motivo, se houver.
        ws.Cell(l, 2).Value = "Diferença entre custo aprovado pela Samsung e o custo real, devido às diferenças nas NFs (descontos, acréscimos, dólar rate, etc.).";
        ws.Cell(l, 2).Style.Fill.BackgroundColor = Pessego;
        ws.Cell(l, 2).Style.Alignment.WrapText = true;
        ws.Cell(l, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        Valor(ws, l, 6, n.DiferencaUsd(), Dolar);
        Valor(ws, l, 10, n.TemDetalhe && n.Previsto() is decimal previsto ? n.Total() - previsto : null, Reais);
        if (n.MotivoDiferenca is { Length: > 0 } motivo) { ws.Cell(l, 12).Value = motivo; ws.Cell(l, 12).Style.Alignment.WrapText = true; }
        var dif = ws.Range(l, 1, l, ultima - 1);
        Grade(dif);
        dif.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(l).Height = 45;
        l += 2;

        // Quadro do total da NF (colunas H:J), a taxa ao lado e a observação do frete e seguro.
        var custo = n.Total();
        var taxas = n.Remessas.Sum(r => r.Taxas(n.Taxa));
        var topoQuadro = l;
        ws.Cell(l, 10).Value = "R$ Total NF";
        ws.Cell(l, 10).Style.Font.Bold = true;
        ws.Cell(l, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(l, 10).Style.Fill.BackgroundColor = Pessego;
        l++;
        foreach (var (rotulo, valor) in new (string, decimal)[]
        {
            ("Total:", custo), ("Pis/Cofins, CSSL, R&D:", taxas), ("VALOR TOTAL DA NF:", custo + taxas),
        })
        {
            var r = ws.Range(l, 8, l, 9).Merge();
            ws.Cell(l, 8).Value = rotulo;
            r.Style.Font.Bold = true;
            r.Style.Fill.BackgroundColor = AzulCab;
            r.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            r.Style.Alignment.WrapText = true;
            Valor(ws, l, 10, valor, Reais);
            ws.Cell(l, 10).Style.Font.Bold = true;
            ws.Cell(l, 10).Style.Fill.BackgroundColor = Pessego;
            l++;
        }
        Grade(ws.Range(topoQuadro + 1, 8, l - 1, 10));
        ws.Cell(topoQuadro, 10).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Cell(topoQuadro + 2, 11).Value = $"Tax = {n.Taxa.ToString("0.########", CultureInfo.InvariantCulture)}";
        if (n.TaxaPercentual != null)
            ws.Cell(topoQuadro + 2, 12).Value = $"Valor do Tax corrigido (padrão: {Nre.TaxaPadrao.ToString("0.########", CultureInfo.InvariantCulture)})";
        ws.Range(topoQuadro + 2, 11, topoQuadro + 2, 12).Style.Font.Bold = true;

        ws.Column(1).Width = 5;
        ws.Column(2).Width = 48;
        ws.Column(3).Width = 6;
        ws.Columns(4, 6).Width = 13;
        ws.Column(7).Width = 15;
        ws.Columns(8, 9).Width = 11;
        ws.Column(10).Width = 16;
        ws.Column(11).Width = 18;   // PTAX e, embaixo, "Tax = 10.67015204"
        ws.Column(12).Width = 26;
        ws.Column(13).Width = 14;
        Evidencias(ws, n, endereco, LinhaEvidenciasDetalhado, ColunaEvidenciasDetalhado, "Invoice & DI", 34);
    }
}
