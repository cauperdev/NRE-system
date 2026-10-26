// ============================================================================
// OFFICE → PDF — transforma Word, Excel, PowerPoint, texto e e-mail do Outlook em PDF pelo Office deste computador
// (pedido do usuário em 24/10/2026: todo arquivo anexado à NRE vira PDF; imagem e print já viravam: Anexos.ImagemEmPdf).
// Usado em: Anexos.SalvarAsync (ao anexar) e ExcelArquivos.Embutir (anexos antigos que não são PDF, só para o Excel).
// Sem o programa do Office, arquivo com senha ou com erro: devolve false e o arquivo fica como veio.
// ONDE MEXER:
//   - Quais tipos viram PDF ...... Word / Excel / PowerPoint / Email (abaixo)
//   - Tempo máximo por arquivo ... Limite
// ============================================================================
using System.Runtime.InteropServices;

namespace MS.Servicos;

public static class PdfOffice
{
    static readonly string[] Word = [".docx", ".doc", ".rtf", ".odt", ".txt"];
    static readonly string[] Excel = [".xlsx", ".xls", ".xlsm", ".csv", ".ods"];
    static readonly string[] PowerPoint = [".pptx", ".ppt"];
    static readonly string[] Email = [".msg"];   // Outlook: salva como página (MHT) e o Word faz o PDF
    static readonly TimeSpan Limite = TimeSpan.FromMinutes(3);
    const string SenhaFalsa = "ms-sem-senha";    // arquivo com senha dá erro em vez de abrir a janela pedindo a senha
    static readonly SemaphoreSlim umPorVez = new(1, 1);

    public static bool Converte(string extensao) =>
        new[] { Word, Excel, PowerPoint, Email }.Any(l => l.Contains(extensao.ToLowerInvariant()));

    // entrada → PDF em saida. true = o PDF foi criado.
    public static async Task<bool> ConverterAsync(string entrada, string saida)
    {
        var ext = Path.GetExtension(entrada).ToLowerInvariant();
        if (!Converte(ext) || !File.Exists(entrada)) return false;
        await umPorVez.WaitAsync();
        try
        {
            var ok = false;
            var t = new Thread(() => { try { ok = Converter(entrada, saida, ext); } catch { ok = false; } });
            t.SetApartmentState(ApartmentState.STA);   // o Office pede thread STA
            t.Start();
            return t.Join(Limite) && ok && File.Exists(saida) && new FileInfo(saida).Length > 0;
        }
        finally { umPorVez.Release(); }
    }

    static bool Converter(string entrada, string saida, string ext)
    {
        if (Word.Contains(ext)) return PeloWord(entrada, saida);
        if (Excel.Contains(ext)) return PeloExcel(entrada, saida);
        if (PowerPoint.Contains(ext)) return PeloPowerPoint(entrada, saida);
        var mht = Path.ChangeExtension(saida, ".mht");
        try { return OutlookEmMht(entrada, mht) && PeloWord(mht, saida); }
        finally { try { File.Delete(mht); } catch { } }
    }

    static dynamic? Abrir(string progId) => Type.GetTypeFromProgID(progId) is { } t ? Activator.CreateInstance(t) : null;

    static void Liberar(object? com)
    {
        try { if (com != null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com); } catch { }
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    static bool PeloWord(string entrada, string saida)
    {
        dynamic? app = null, doc = null;
        try
        {
            app = Abrir("Word.Application");
            if (app == null) return false;
            app.Visible = false;
            app.DisplayAlerts = 0;   // wdAlertsNone
            // Open(arquivo, ConfirmConversions, ReadOnly, AddToRecentFiles, PasswordDocument)
            doc = app.Documents.Open(entrada, false, true, false, SenhaFalsa);
            doc.ExportAsFixedFormat(saida, 17);   // wdExportFormatPDF
            return true;
        }
        catch { return false; }
        finally
        {
            try { doc?.Close(false); } catch { }
            try { app?.Quit(false); } catch { }
            Liberar(doc); Liberar(app);
        }
    }

    static bool PeloExcel(string entrada, string saida)
    {
        dynamic? app = null, livro = null;
        try
        {
            app = Abrir("Excel.Application");
            if (app == null) return false;
            app.Visible = false;
            app.DisplayAlerts = false;
            // Open(arquivo, UpdateLinks, ReadOnly, Format, Password)
            livro = app.Workbooks.Open(entrada, 0, true, Type.Missing, SenhaFalsa);
            livro.ExportAsFixedFormat(0, saida);   // xlTypePDF: todas as abas
            return true;
        }
        catch { return false; }
        finally
        {
            try { livro?.Close(false); } catch { }
            try { app?.Quit(); } catch { }
            Liberar(livro); Liberar(app);
        }
    }

    static bool PeloPowerPoint(string entrada, string saida)
    {
        dynamic? app = null, apresentacao = null;
        try
        {
            app = Abrir("PowerPoint.Application");
            if (app == null) return false;
            // Open(arquivo, ReadOnly, Untitled, WithWindow) — sem janela
            apresentacao = app.Presentations.Open(entrada, -1, 0, 0);
            apresentacao.SaveAs(saida, 32);   // ppSaveAsPDF
            return true;
        }
        catch { return false; }
        finally
        {
            try { apresentacao?.Close(); } catch { }
            try { if (app != null && (int)app.Presentations.Count == 0) app.Quit(); } catch { }
            Liberar(apresentacao); Liberar(app);
        }
    }

    // E-mail do Outlook (.msg) → página MHT (com o texto e as imagens do e-mail). Não fecha o Outlook de quem usa.
    static bool OutlookEmMht(string entrada, string mht)
    {
        dynamic? app = null, item = null;
        try
        {
            app = Abrir("Outlook.Application");
            if (app == null) return false;
            item = app.Session.OpenSharedItem(entrada);
            item.SaveAs(mht, 10);   // olMHTML
            return File.Exists(mht);
        }
        catch { return false; }
        finally
        {
            try { item?.Close(1); } catch { }   // olDiscard
            Liberar(item); Liberar(app);
        }
    }
}
