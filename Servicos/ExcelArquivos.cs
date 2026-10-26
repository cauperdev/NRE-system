// ============================================================================
// ARQUIVOS DENTRO DO EXCEL — embute arquivos numa planilha já pronta (ícone que abre o arquivo com dois cliques, mesmo
// sem acesso ao sistema), pelo Excel deste computador: o ClosedXML não faz isso. Usado no Excel do botão Extração da
// NRE: os arquivos vão na aba Detalhado, coluna N ("Invoice & DI"); a aba Resumo não leva arquivos (24/10/2026).
// Sem Excel ou com erro, a planilha volta como veio (com os links das evidências, feitos em ResumoNre).
// ============================================================================
using System.Runtime.InteropServices;

namespace MS.Servicos;

public static class ExcelArquivos
{
    static readonly SemaphoreSlim umPorVez = new(1, 1);

    // arquivos: (caminho guardado, nome original, grupo — o tipo: Invoices, DIs...). Abaixo da célula (linha, coluna) — o
    // título "Evidências" — as células com os links são limpas e cada grupo ganha o nome numa célula e, embaixo, os
    // ícones, 2 por linha. Só PDF vira ícone (o resto é convertido em PDF antes; o que não der fica com o nome escrito).
    // aba: nome da aba onde os arquivos entram (vazio = a primeira).
    public static async Task<byte[]> Embutir(byte[] planilha, int linha, int coluna, IReadOnlyList<(string Caminho, string Nome, string Grupo)> arquivos,
        string? aba = null)
    {
        var existentes = arquivos.Where(a => File.Exists(a.Caminho)).ToList();
        if (existentes.Count == 0) return planilha;
        await umPorVez.WaitAsync();
        var pasta = Path.Combine(Path.GetTempPath(), "ms-excel-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pasta);
            var xlsx = Path.Combine(pasta, "planilha.xlsx");
            await File.WriteAllBytesAsync(xlsx, planilha);
            // Cópia com o nome original (é o nome do ícone e o nome com que o arquivo abre); uma pasta para cada, por causa
            // de nomes repetidos.
            var copias = new List<(string Caminho, string Nome, string Grupo)>();
            for (var i = 0; i < existentes.Count; i++)
            {
                var nome = NomeSeguro(existentes[i].Nome);
                var subpasta = Directory.CreateDirectory(Path.Combine(pasta, i.ToString())).FullName;
                var destino = Path.Combine(subpasta, nome);
                // Anexo antigo que não é PDF (Word, Excel... de antes de 24/10/2026): vai convertido em PDF, só no Excel —
                // o Excel não salva a planilha com um Word dentro ("O documento não foi salvo").
                var guardado = existentes[i].Caminho;
                if (!guardado.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && PdfOffice.Converte(Path.GetExtension(guardado)))
                {
                    var pdf = Path.Combine(subpasta, Path.ChangeExtension(nome, ".pdf"));
                    if (await PdfOffice.ConverterAsync(guardado, pdf)) { copias.Add((pdf, Path.GetFileName(pdf), existentes[i].Grupo)); continue; }
                }
                File.Copy(guardado, destino);
                copias.Add((destino, nome, existentes[i].Grupo));
            }
            // Linhas dos links feitos pelo ResumoNre (um rótulo por grupo + um por arquivo), que os ícones substituem.
            var linhasLinks = arquivos.Count + arquivos.Select(a => a.Grupo).Distinct().Count();
            return NoExcel(xlsx, aba, linha, coluna, linhasLinks, copias) ? await File.ReadAllBytesAsync(xlsx) : planilha;
        }
        catch { return planilha; }
        finally
        {
            try { Directory.Delete(pasta, true); } catch { }
            umPorVez.Release();
        }
    }

    // Nome da cópia = o nome embaixo do ícone (o Excel sempre usa o nome do arquivo, numa linha só): até 16 letras, para
    // as caixas ficarem do tamanho das do modelo do cliente (pedido do usuário em 24/10/2026; ex.: "2606936395 1.pdf",
    // "MODELO PAR….pdf").
    const int NomeMaximo = 16;
    static string NomeSeguro(string nome)
    {
        var limpo = string.Concat(Path.GetFileName(nome).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (limpo.Length == 0) return "EVIDENCIA";
        var ext = Path.GetExtension(limpo);
        var corpo = Path.GetFileNameWithoutExtension(limpo);
        return corpo.Length + ext.Length <= NomeMaximo ? limpo : corpo[..Math.Max(1, NomeMaximo - ext.Length - 1)].TrimEnd() + "…" + ext;
    }

    // Excel deste computador: abre escondido, põe cada arquivo como ícone (OLEObjects.Add, sem vínculo), salva e fecha.
    // Como o modelo do cliente (pedidos do usuário em 24/10/2026): em cada grupo, o nome do grupo (DIs, NF...) e embaixo
    // os arquivos em 3 COLUNAS, descendo para a linha de baixo, cada um numa caixa com borda, todas do MESMO TAMANHO (a do
    // maior ícone), com o ícone e o nome do arquivo centralizados; a coluna fica com a largura de 3 caixas (não alarga
    // com a quantidade). Só PDF vira ícone (com Word
    // dentro o Excel não salva a planilha); o que não for fica só com o nome escrito.
    static bool NoExcel(string xlsx, string? aba, int linha, int coluna, int linhasLinks, List<(string Caminho, string Nome, string Grupo)> arquivos)
    {
        var ok = false;
        const double Escala = 1;   // tamanho dos ícones (1 = o do Excel, como no modelo do cliente)
        var t = new Thread(() =>
        {
            dynamic? app = null, livro = null;
            try
            {
                var tipo = Type.GetTypeFromProgID("Excel.Application");
                if (tipo == null) return;
                app = Activator.CreateInstance(tipo)!;
                app.DisplayAlerts = false;
                livro = app.Workbooks.Open(xlsx);
                dynamic ws = aba == null ? livro.Worksheets.Item(1) : livro.Worksheets.Item(aba);
                ws.Activate();   // Word e outros só entram como ícone na aba aberta (o Detalhado é a 2ª aba)
                ws.Range(ws.Cells.Item(linha + 1, coluna), ws.Cells.Item(linha + linhasLinks, coluna)).Clear();
                double D(object v) => Convert.ToDouble(v);
                double Topo(dynamic c) => D(c.Top);
                double Altura(dynamic c) => D(c.Height);
                var r = linha + 1;
                dynamic Celula(double y) { while (Topo(ws.Cells.Item(r, coluna)) + 0.5 < y) r++; return ws.Cells.Item(r, coluna); }   // 1ª célula a partir da altura y

                // 1. Os ícones (no tamanho natural; arrumados depois). Não-PDF ou erro: fica para escrever o nome.
                var itens = new List<(string Grupo, string Nome, dynamic? Icone)>();
                foreach (var (caminho, nome, g) in arquivos)
                {
                    dynamic? o = null;
                    if (caminho.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        try
                        {
                            o = ws.OLEObjects().Add(Type.Missing, caminho, false, true, Type.Missing, Type.Missing, nome, 0, 0);
                            try { o.Border.LineStyle = -4142; } catch { }   // sem a moldura do ícone: a caixa é desenhada abaixo
                            // Menor (pedido do usuário): ícone e nome reduzidos por igual, sem espremer.
                            double w = D(o.Width), h = D(o.Height);
                            o.Width = w * Escala;
                            o.Height = h * Escala;
                        }
                        catch { try { o?.Delete(); } catch { } o = null; }
                    itens.Add((g, nome, o));
                }

                // 2. Caixas iguais: largura e altura do maior ícone + folga. A coluna fica com a largura de 3 caixas.
                const double folga = 3, espaco = 8;
                const int porLinha = 3;
                var icones = itens.Where(i => i.Icone != null).Select(i => i.Icone!).ToList();
                double caixaL = icones.Count == 0 ? 0 : icones.Max(o => D(o.Width)) + 2 * folga;
                double caixaA = icones.Count == 0 ? 0 : icones.Max(o => D(o.Height)) + 2 * folga;
                dynamic col = ws.Columns.Item(coluna);
                double precisa = espaco + porLinha * (caixaL + espaco);
                if (icones.Count > 0)
                    col.ColumnWidth = Math.Min(255, D(col.ColumnWidth) * precisa / D(col.Width) + 1);   // pontos → largura do Excel

                // 3. Arruma: rótulo do grupo e embaixo as caixas, 3 por linha; os nomes que não viraram ícone, embaixo delas.
                double inicio = D(ws.Cells.Item(linha, coluna).Left) + espaco;
                double proximoTopo = Topo(ws.Cells.Item(linha + 1, coluna));
                foreach (var grupo in itens.GroupBy(i => i.Grupo))
                {
                    dynamic rotulo = Celula(proximoTopo);
                    rotulo.Value2 = grupo.Key;
                    rotulo.Font.Bold = true;
                    rotulo.Font.Color = 0x40171A;   // Índigo noite #1A1740 (o Excel lê BGR)
                    proximoTopo = Topo(rotulo) + Altura(rotulo) + 4;
                    var n = 0;
                    foreach (var (_, _, o) in grupo.Where(i => i.Icone != null))
                    {
                        var x = inicio + n % porLinha * (caixaL + espaco);
                        var y = proximoTopo + n / porLinha * (caixaA + espaco);
                        n++;
                        dynamic caixa = ws.Shapes.AddShape(1, x, y, caixaL, caixaA);   // retângulo
                        caixa.Fill.Visible = 0;
                        caixa.Line.ForeColor.RGB = 0;
                        caixa.Line.Weight = 0.75;
                        o!.Left = x + (caixaL - D(o.Width)) / 2;
                        o.Top = y + (caixaA - D(o.Height)) / 2;
                        o.BringToFront();
                    }
                    if (n > 0) proximoTopo += (n + porLinha - 1) / porLinha * (caixaA + espaco) + 2;
                    foreach (var (_, nome, _) in grupo.Where(i => i.Icone == null))
                    {
                        dynamic c = Celula(proximoTopo);
                        c.Value2 = $"{nome} (abra na aba Arquivos da NRE)";
                        c.Font.Color = 0x5B5252;   // cinza #52525B
                        proximoTopo = Topo(c) + Altura(c) + 2;
                    }
                    proximoTopo += 4;
                }
                ((dynamic)livro.Worksheets.Item(1)).Activate();   // abre na 1ª aba (Resumo)
                livro.Save();
                ok = true;
            }
            catch { }
            finally
            {
                try { livro?.Close(false); } catch { }
                try { app?.Quit(); } catch { }
                try { if (app != null) Marshal.FinalReleaseComObject(app); } catch { }
                livro = null; app = null;
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        });
        t.SetApartmentState(ApartmentState.STA); // o Excel pede thread STA
        t.Start();
        return t.Join(TimeSpan.FromMinutes(2)) && ok;
    }
}
