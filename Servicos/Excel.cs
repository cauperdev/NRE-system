// ============================================================================
// PLANILHAS EXCEL — tudo que baixa ou lê .xlsx: Extração, Baixar modelo, Importar Excel e QR codes.
// (A leitura das colunas do JMS antigo — nomes em inglês ou português, datas americanas — fica em Pages/HistoricoJms.)
// ONDE MEXER:
//   - Cor/estilo do cabeçalho das planilhas .......... Montar
//   - Planilha de QR codes (etiquetas) ............... QrCodes (tamanho do QR, etiquetas por linha e por folha)
//   - Colunas dos modelos (Equipamentos / Cronograma)  ColunasEquipamento / ColunasPlano
//   - Aba TASKS (atividades por máquina) ............. LerTasks, TituloTask, TarefasDe
//   - Palavras aceitas em Periodicidade e Status ..... LerTipo, LerPeriodicidades, LerStatusEquipamento
// Na importação as colunas são achadas pelo NOME do cabeçalho (sem acento e sem maiúscula): a ordem não importa.
// ============================================================================
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MS.Modelos;

namespace MS.Servicos;

public static class Excel
{
    public const string Mime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";


    public const string AbaTasks = "TASKS";

    public static byte[] Planilha(params (string Aba, string[] Cabecalho, IEnumerable<object?[]> Linhas)[] abas) => Montar(abas, null);

    // Cronograma: aba dos planos + aba TASKS, com um bloco "MÁQUINA - PERIODICIDADE" e as atividades embaixo.
    public static byte[] PlanilhaCronograma((string Aba, string[] Cabecalho, IEnumerable<object?[]> Linhas) aba,
        IEnumerable<(string Titulo, IEnumerable<string> Tarefas)> blocos) => Montar([aba], blocos);

    static byte[] Montar((string Aba, string[] Cabecalho, IEnumerable<object?[]> Linhas)[] abas,
        IEnumerable<(string Titulo, IEnumerable<string> Tarefas)>? blocos)
    {
        using var wb = new XLWorkbook();
        foreach (var (aba, cab, linhas) in abas)
        {
            var ws = wb.Worksheets.Add(aba);
            for (var c = 0; c < cab.Length; c++) ws.Cell(1, c + 1).Value = cab[c];
            var cabRange = ws.Range(1, 1, 1, cab.Length);
            cabRange.Style.Font.Bold = true;
            cabRange.Style.Font.FontColor = XLColor.White;
            cabRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1A1740"); // Índigo noite da paleta (mesma cor do topo do sistema)
            var l = 2;
            foreach (var linha in linhas)
            {
                for (var c = 0; c < linha.Length; c++)
                {
                    var cel = ws.Cell(l, c + 1);
                    switch (linha[c])
                    {
                        case DateTime d: cel.Value = d; cel.Style.DateFormat.Format = d.TimeOfDay == TimeSpan.Zero ? "dd/mm/yyyy" : "dd/mm/yyyy hh:mm"; break;
                        case decimal m: cel.Value = m; break;
                        case int i: cel.Value = i; break;
                        case null: break;
                        default: cel.Value = linha[c]!.ToString(); break;
                    }
                }
                l++;
            }
            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(1, Math.Min(l, 300), 8, 60);
        }
        if (blocos != null)
        {
            var ws = wb.Worksheets.Add(AbaTasks);
            var l = 1;
            foreach (var (titulo, tarefas) in blocos)
            {
                var cab = ws.Cell(l++, 1);
                cab.Value = titulo;
                cab.Style.Font.Bold = true;
                cab.Style.Fill.BackgroundColor = XLColor.FromHtml("#d9d9d9");
                foreach (var t in tarefas) ws.Cell(l++, 1).Value = t;
                l++; // linha em branco entre os blocos
            }
            ws.Column(1).Width = 100;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // QR codes das máquinas em etiquetas: nome (negrito), código e o QR que abre as OS da máquina no sistema.
    // 3 etiquetas por linha, com espaço entre elas para recortar; cada folha A4 leva 5 linhas de etiquetas.
    public static byte[] QrCodes(IEnumerable<(string Nome, string Codigo, string Link)> maquinas)
    {
        const int porLinha = 3, linhasPorFolha = 5;
        const int lado = 120;          // QR em pixels (~3 cm impresso)
        const double largura = 30;     // coluna da etiqueta (~215 px)
        const int margemQr = 47;       // (215 − 120) / 2: centraliza o QR na coluna
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("QR codes");
        ws.ShowGridLines = false;
        for (var k = 0; k < porLinha; k++)
        {
            ws.Column(2 * k + 1).Width = largura;
            if (k < porLinha - 1) ws.Column(2 * k + 2).Width = 3; // espaço entre etiquetas
        }

        using var gerador = new QRCoder.QRCodeGenerator();
        var n = 0;
        foreach (var (nome, codigo, link) in maquinas)
        {
            var bloco = n / porLinha;
            var topo = bloco * 4 + 1;   // nome, código, QR e uma linha de espaço
            var col = n % porLinha * 2 + 1;
            if (n % porLinha == 0)
            {
                ws.Row(topo).Height = 20;
                ws.Row(topo + 1).Height = 18;
                ws.Row(topo + 2).Height = 100; // ~133 px: cabe o QR
                ws.Row(topo + 3).Height = 12;
                if (bloco > 0 && bloco % linhasPorFolha == 0) ws.PageSetup.AddHorizontalPageBreak(topo - 1);
            }
            var cNome = ws.Cell(topo, col);
            cNome.Value = nome;
            cNome.Style.Font.Bold = true;
            ws.Cell(topo + 1, col).Value = codigo;

            var etiqueta = ws.Range(topo, col, topo + 2, col);
            etiqueta.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            etiqueta.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            etiqueta.Style.Alignment.ShrinkToFit = true; // nome comprido diminui para caber
            etiqueta.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            var png = new QRCoder.PngByteQRCode(gerador.CreateQrCode(link, QRCoder.QRCodeGenerator.ECCLevel.M)).GetGraphic(8);
            ws.AddPicture(new MemoryStream(png)).MoveTo(ws.Cell(topo + 2, col), margemQr, 6).WithSize(lado, lado);
            n++;
        }

        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.CenterHorizontally = true;
        ws.PageSetup.Margins.Top = ws.PageSetup.Margins.Bottom = 0.4;
        ws.PageSetup.Margins.Left = ws.PageSetup.Margins.Right = 0.3;
        ws.PageSetup.Margins.Header = ws.PageSetup.Margins.Footer = 0.2;
        ws.PageSetup.FitToPages(1, 0);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ---------- Modelos (colunas = campos do formulário de cada tela) ----------

    public static readonly string[] ColunasEquipamento =
        ["IRF (código da Samsung, opcional)", "Etiqueta eletrônica (código de barras da etiqueta, opcional)", "Nome", "Modelo (opcional)", "Número de série (opcional)", "Prédio", "Localização", "Área", "Status (Operando/Em manutenção/Inativo)"];

    public static readonly string[] ColunasPlano =
        ["Código do equipamento", "Nome do plano", "Periodicidade (Diária/Semanal/Quinzenal/Mensal/Trimestral/Semestral/Anual ou D/S/Q/M/TM/SM/A; várias: M/TM)", "Descrição do serviço",
         "Lembrar antes do prazo (dias; em branco = não lembrar)",
         "Técnico responsável (nome completo ou matrícula; opcional)", "Coordenador responsável (nome completo ou matrícula; opcional)"];

    // Spare Parts: as mesmas colunas da aba Peças da Extração (dá para importar uma ou outra). Sem quantidade desde
    // 08/10/2026 (o Spare Parts é só histórico do código).
    public static readonly string[] ColunasSpareParts =
        ["Código", "Descrição", "Equipamento (tipo de máquina, opcional)"];

    public static byte[] ModeloSpareParts() => Planilha(("Spare Parts", ColunasSpareParts,
        [["CR-0889", "CORREIA DENTADA HTD 5M", "ADESIVADORA"]]));

    public static byte[] ModeloEquipamentos() => Planilha(("Equipamentos", ColunasEquipamento,
        [["", "", "ADESIVADORA", "PVA DELTA 8", "", "PRÉDIO 1", "SMT LINE 02", "SAMSUNG", "Operando"]]));

    public static byte[] ModeloPlanos() => PlanilhaCronograma(("Cronograma", ColunasPlano,
        [["PRDADS00101", "ADESIVADORA", "M/TM", "Preventiva — ADESIVADORA", 5, "", ""]]),
        [(TituloTask("ADESIVADORA", Periodicidade.Mensal), ["Limpeza do bico dosador", "Verificar pressão de ar", "Verificar nível do adesivo"]),
         (TituloTask("ADESIVADORA", Periodicidade.Trimestral), ["Trocar o filtro de ar", "Calibrar a pressão de dosagem"])]);

    // ---------- Leitura: acha as colunas pelo nome do cabeçalho (a ordem não importa) ----------

    public class LinhaPlanilha(int numero, Dictionary<string, string> campos)
    {
        public int Numero { get; } = numero;
        public string this[string coluna] => campos.GetValueOrDefault(Chave(coluna), "").Trim();
        public bool Tem(string coluna) => campos.ContainsKey(Chave(coluna)); // a planilha tem essa coluna?
        public bool Vazia => campos.Values.All(string.IsNullOrWhiteSpace);
    }

    // "Status (Operando/…)" → "status"; "Código" → "codigo"
    public static string Chave(string cabecalho)
    {
        var s = cabecalho.Split('(')[0].Trim().ToLowerInvariant();
        return SemAcento(s);
    }

    public static string SemAcento(string s) => new string(s.Normalize(System.Text.NormalizationForm.FormD)
        .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());

    public static List<LinhaPlanilha> LerTabela(Stream arquivo)
    {
        using var wb = new XLWorkbook(arquivo);
        return LerAba(wb.Worksheets.First());
    }

    // Lê a aba com esse nome (sem acento e sem maiúscula) ou, se não houver, a primeira — ex.: aba "JMS" da MANUTENÇÃO PREVENTIVA.
    public static List<LinhaPlanilha> LerTabela(Stream arquivo, string aba)
    {
        using var wb = new XLWorkbook(arquivo);
        return LerAba(wb.Worksheets.FirstOrDefault(w => Normalizar(w.Name) == Normalizar(aba)) ?? wb.Worksheets.First());
    }

    // Cronograma: planos da aba "Cronograma" (ou da primeira) + atividades da aba TASKS (Tasks = null se ela não existir).
    public record Cronograma(List<LinhaPlanilha> Linhas, Dictionary<string, List<string>>? Tasks);

    public static Cronograma LerCronograma(Stream arquivo)
    {
        using var wb = new XLWorkbook(arquivo);
        var tasks = wb.Worksheets.FirstOrDefault(w => w.Name.Trim().Equals(AbaTasks, StringComparison.OrdinalIgnoreCase));
        var planos = wb.Worksheets.FirstOrDefault(w => Normalizar(w.Name) == "cronograma") ?? wb.Worksheets.First(w => w != tasks);
        return new(LerAba(planos), tasks == null ? null : LerTasks(tasks));
    }

    // Aba TASKS: a linha "AOI - MENSAL" abre um bloco; as linhas seguintes, até o próximo título, são as atividades.
    static readonly Regex TituloTaskRegex = new(@"^(.+?)\s*-\s*(diaria|semanal|quinzenal|mensal|bimestral|trimestral|semestral|anual)$");

    static Dictionary<string, List<string>> LerTasks(IXLWorksheet ws)
    {
        var blocos = new Dictionary<string, List<string>>();
        List<string>? atual = null;
        foreach (var row in ws.RowsUsed())
        {
            var texto = row.CellsUsed().FirstOrDefault()?.GetString().Trim() ?? "";
            if (texto == "") continue;
            var m = TituloTaskRegex.Match(Regex.Replace(Normalizar(texto), @"\s+", " "));
            if (m.Success)
            {
                var chave = ChaveTask(m.Groups[1].Value, m.Groups[2].Value);
                if (!blocos.TryGetValue(chave, out atual)) blocos[chave] = atual = new();
            }
            else atual?.Add(texto);
        }
        return blocos;
    }

    static string ChaveTask(string maquina, string periodo) => Regex.Replace(Normalizar(maquina), @"\s+", " ") + " - " + Normalizar(periodo);

    public static string TituloTask(string maquina, Periodicidade p) => $"{maquina.Trim().ToUpper()} - {p.Nome().ToUpper()}";

    // Atividades do bloco "<máquina> - <periodicidade>" (null se não houver bloco ou ele estiver vazio).
    public static List<string>? TarefasDe(Dictionary<string, List<string>> tasks, string maquina, Periodicidade p) =>
        tasks.GetValueOrDefault(ChaveTask(maquina, p.Nome())) is { Count: > 0 } lista ? lista : null;

    static List<LinhaPlanilha> LerAba(IXLWorksheet ws)
    {
        var cab = ws.Row(1).CellsUsed().ToDictionary(c => c.Address.ColumnNumber, c => Chave(c.GetString()));
        var linhas = new List<LinhaPlanilha>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var campos = new Dictionary<string, string>();
            foreach (var (col, chave) in cab)
            {
                var cel = row.Cell(col);
                // Data: "dd/MM/yyyy HH:mm" (com ":ss" só quando a hora tem segundos, ex.: Criada em do Histórico JMS).
                campos[chave] = cel.DataType == XLDataType.DateTime
                    ? cel.GetDateTime().ToString(cel.GetDateTime().Second != 0 ? "dd/MM/yyyy HH:mm:ss" : "dd/MM/yyyy HH:mm")
                    : cel.GetString();
            }
            var linha = new LinhaPlanilha(row.RowNumber(), campos);
            if (!linha.Vazia) linhas.Add(linha);
        }
        return linhas;
    }

    public static string Normalizar(string s) => SemAcento(s.Trim().ToLowerInvariant());

    // Por extenso ou pela sigla do cronograma: D, S, Q, M, TM (trimestral), SM (semestral), A.
    public static Periodicidade? LerTipo(string s) => Normalizar(s) switch
    {
        "diaria" or "d" => Periodicidade.Diaria,
        "semanal" or "s" => Periodicidade.Semanal,
        "quinzenal" or "q" => Periodicidade.Quinzenal,
        "mensal" or "m" => Periodicidade.Mensal,
        "trimestral" or "tm" => Periodicidade.Trimestral,
        "semestral" or "sm" => Periodicidade.Semestral,
        "anual" or "a" => Periodicidade.Anual,
        _ => null,
    };

    // Várias periodicidades numa célula ("S/M/TM/SM/A"): devolve as válidas (sem repetir) e os trechos não reconhecidos.
    public static (List<Periodicidade> Validas, List<string> Invalidas) LerPeriodicidades(string s)
    {
        var validas = new List<Periodicidade>();
        var invalidas = new List<string>();
        foreach (var parte in s.Split(['/', ',', ';', '+'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (LerTipo(parte) is Periodicidade p) { if (!validas.Contains(p)) validas.Add(p); }
            else invalidas.Add(parte);
        }
        return (validas, invalidas);
    }

    public static StatusEquipamento? LerStatusEquipamento(string s) => Normalizar(s) switch
    {
        "operando" or "" => StatusEquipamento.Operando,
        "em manutencao" or "manutencao" => StatusEquipamento.EmManutencao,
        "inativo" => StatusEquipamento.Inativo,
        _ => null,
    };
}
