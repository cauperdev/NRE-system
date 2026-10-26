// ============================================================================
// ANEXOS DA NRE — guarda os arquivos (PO, orçamento, nota fiscal, evidências...) na pasta dados/anexos.
// Diferente das fotos das OS, NÃO são apagados sozinhos: só saem quando alguém remove na tela da NRE
// (ou quando a NRE inteira é removida).
// Imagem (print, foto) vira PDF ao anexar (pedido do usuário em 08/10/2026): guarda só o PDF, com o mesmo nome
// (print.png → print.pdf), uma página A4 com a imagem inteira. Desde 24/10/2026 também Word, Excel, PowerPoint, texto e
// e-mail do Outlook (.msg), pelo Office deste computador (Servicos/PdfOffice.cs). Fica como veio: .zip, .eml e o que o
// Office não conseguir abrir.
// ONDE MEXER:
//   - Tipos de arquivo aceitos ........ Extensoes (as de imagem viram PDF: ExtensoesImagem)
//   - Tamanho máximo de cada arquivo .. TamanhoMaximo (o envio inteiro: [RequestSizeLimit] em Pages/Nre/Editar.cshtml.cs)
//   - Página e qualidade do PDF ....... ImagemEmPdf (A4, margem, até 2400 px, JPEG 92)
// ============================================================================
using SkiaSharp;

namespace MS.Servicos;

public class Anexos(IWebHostEnvironment env)
{
    public static readonly string[] ExtensoesImagem = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];
    public static readonly string[] Extensoes =
        [".pdf", ".xlsx", ".xls", ".xlsm", ".csv", ".docx", ".doc", ".pptx", .. ExtensoesImagem, ".msg", ".eml", ".txt", ".zip"];
    public const long TamanhoMaximo = 25L * 1024 * 1024;   // 25 MB por arquivo
    const int LadoMaximoPdf = 2400;                         // foto grande de celular é reduzida (nítida em A4, PDF leve)

    public string Pasta
    {
        get
        {
            var p = Path.Combine(env.ContentRootPath, "dados", "anexos");
            Directory.CreateDirectory(p);
            return p;
        }
    }

    public string Caminho(string arquivo) => Path.Combine(Pasta, Path.GetFileName(arquivo));

    // Guarda o arquivo com um nome próprio (o original fica no banco). Imagem vira PDF.
    // Devolve o nome guardado, o nome que aparece na tela, o tamanho e, se recusou, o motivo.
    public async Task<(string? Arquivo, string Nome, long Tamanho, string? Erro)> SalvarAsync(IFormFile f)
    {
        var nome = Path.GetFileName(f.FileName);
        var ext = Path.GetExtension(nome).ToLowerInvariant();
        if (f.Length == 0) return (null, nome, 0, $"{nome}: arquivo vazio");
        if (!Extensoes.Contains(ext)) return (null, nome, 0, $"{nome}: tipo de arquivo não aceito");
        if (f.Length > TamanhoMaximo) return (null, nome, 0, $"{nome}: maior que {TamanhoMaximo / 1024 / 1024} MB");
        if (ExtensoesImagem.Contains(ext))
        {
            using var entrada = new MemoryStream();
            await f.CopyToAsync(entrada);
            var nomePdf = Path.ChangeExtension(nome, ".pdf");
            var pdf = ImagemEmPdf(entrada.ToArray(), Path.GetFileNameWithoutExtension(nome));
            if (pdf == null) return (null, nome, 0, $"{nome}: imagem que não deu para abrir");
            var guardadoPdf = $"{Guid.NewGuid():N}.pdf";
            await File.WriteAllBytesAsync(Caminho(guardadoPdf), pdf);
            return (guardadoPdf, nomePdf, pdf.Length, null);
        }
        // Word, Excel, PowerPoint, texto e e-mail do Outlook viram PDF pelo Office (PdfOffice); sem Office ou com erro,
        // o arquivo fica como veio.
        if (PdfOffice.Converte(ext))
        {
            var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ms-pdf-" + Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                var origem = Path.Combine(temp, "arquivo" + ext);
                await using (var copia = File.Create(origem)) await f.CopyToAsync(copia);
                var guardadoPdf = $"{Guid.NewGuid():N}.pdf";
                if (await PdfOffice.ConverterAsync(origem, Caminho(guardadoPdf)))
                    return (guardadoPdf, Path.ChangeExtension(nome, ".pdf"), new FileInfo(Caminho(guardadoPdf)).Length, null);
                Apagar(guardadoPdf);   // pedaço de PDF que tenha ficado
            }
            finally { try { Directory.Delete(temp, true); } catch { } }
        }
        var guardado = $"{Guid.NewGuid():N}{ext}";
        await using var destino = File.Create(Caminho(guardado));
        await f.CopyToAsync(destino);
        return (guardado, nome, f.Length, null);
    }

    // Imagem → PDF de uma página A4 (deitada quando a imagem é mais larga que alta), a imagem inteira centralizada no
    // alto, sem aumentar print pequeno. null = não é uma imagem que dê para ler.
    public static byte[]? ImagemEmPdf(byte[] dados, string titulo)
    {
        using var codec = SKCodec.Create(new MemoryStream(dados));
        if (codec == null) return null;
        using var original = SKBitmap.Decode(codec);
        if (original == null) return null;
        using var orientada = Fotos.Orientar(original, codec.EncodedOrigin);   // foto de celular: na posição certa
        var reduzir = Math.Min(1.0, LadoMaximoPdf / (double)Math.Max(orientada.Width, orientada.Height));
        using var imagem = reduzir < 1
            ? orientada.Resize(new SKImageInfo(Math.Max(1, (int)(orientada.Width * reduzir)), Math.Max(1, (int)(orientada.Height * reduzir))),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            : orientada.Copy();

        var deitada = imagem.Width > imagem.Height;
        float largura = deitada ? 842 : 595, altura = deitada ? 595 : 842, margem = 28;   // A4 em pontos (1/72")
        var escala = Math.Min(1f, Math.Min((largura - 2 * margem) / imagem.Width, (altura - 2 * margem) / imagem.Height));
        float w = imagem.Width * escala, h = imagem.Height * escala;

        var info = SKDocumentPdfMetadata.Default;
        info.Title = titulo;
        info.Creator = info.Producer = "MS Gestão de Manutenção";
        info.Creation = info.Modified = DateTime.Now;
        info.EncodingQuality = 92;   // imagem em JPEG dentro do PDF (101 = sem perda, PDF bem maior)
        using var ms = new MemoryStream();
        using (var doc = SKDocument.CreatePdf(ms, info))
        {
            var canvas = doc.BeginPage(largura, altura);
            canvas.Clear(SKColors.White);
            using var img = SKImage.FromBitmap(imagem);
            canvas.DrawImage(img, SKRect.Create((largura - w) / 2, margem, w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            doc.EndPage();
            doc.Close();
        }
        return ms.ToArray();
    }

    // Anexo antigo que não é PDF (Word, Excel... de antes de 24/10/2026): vira PDF de vez — o arquivo guardado é trocado
    // pelo PDF e o nome passa a terminar em .pdf. Usado na Extração da NRE (assim só a primeira demora). true = converteu.
    public async Task<bool> ConverterParaPdfAsync(Modelos.NreAnexo a)
    {
        var ext = Path.GetExtension(a.Arquivo).ToLowerInvariant();
        if (ext == ".pdf" || !PdfOffice.Converte(ext) || !File.Exists(Caminho(a.Arquivo))) return false;
        var pdf = $"{Guid.NewGuid():N}.pdf";
        if (!await PdfOffice.ConverterAsync(Caminho(a.Arquivo), Caminho(pdf))) { Apagar(pdf); return false; }
        Apagar(a.Arquivo);
        a.Arquivo = pdf;
        a.Nome = Path.ChangeExtension(a.Nome, ".pdf");
        a.Tamanho = new FileInfo(Caminho(pdf)).Length;
        return true;
    }

    public void Apagar(string arquivo)
    {
        var c = Caminho(arquivo);
        if (File.Exists(c)) File.Delete(c);
    }

    // PDF e imagem abrem no navegador; o resto baixa.
    public static (string Tipo, bool Abrir) TipoDoArquivo(string nome) => Path.GetExtension(nome).ToLowerInvariant() switch
    {
        ".pdf" => ("application/pdf", true),
        ".png" => ("image/png", true),
        ".jpg" or ".jpeg" => ("image/jpeg", true),
        ".txt" => ("text/plain; charset=utf-8", true),
        ".xlsx" or ".xlsm" => ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", false),
        ".xls" => ("application/vnd.ms-excel", false),
        ".csv" => ("text/csv", false),
        ".docx" => ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", false),
        ".doc" => ("application/msword", false),
        ".pptx" => ("application/vnd.openxmlformats-officedocument.presentationml.presentation", false),
        ".msg" => ("application/vnd.ms-outlook", false),
        ".eml" => ("message/rfc822", false),
        ".zip" => ("application/zip", false),
        _ => ("application/octet-stream", false),
    };

    public static string Tamanho(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024:0.#} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}
