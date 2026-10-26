// ============================================================================
// REGRA 06 — FOTOS (registro com prova): salva as fotos da OS reduzidas, na pasta dados/fotos.
// Só vale foto tirada no dia (a data e a hora aparecem em cima da foto).
// As fotos ficam guardadas para sempre: a limpeza de 90 dias foi tirada a pedido do usuário em 20/10/2026.
// ONDE MEXER:
//   - Tamanho máximo da foto (1600 px) ...... LadoMaximo
//   - Quando a foto foi tirada .............. HoraDaFotoAsync (EXIF da foto → data do arquivo → agora)
//                                             (a mesma regra no navegador: interacoes.js seção 11)
// Quem pode ver cada foto: Pages/Foto.cshtml.cs.
// ============================================================================
using System.Globalization;
using System.Text;
using SkiaSharp;

namespace MS.Servicos;

// Regra 06 — fotos reduzidas a até 1600px ao salvar.
public class Fotos(IWebHostEnvironment env)
{
    public const int LadoMaximo = 1600;

    public string Pasta
    {
        get
        {
            var p = Path.Combine(env.ContentRootPath, "dados", "fotos");
            Directory.CreateDirectory(p);
            return p;
        }
    }

    public string Caminho(string arquivo) => Path.Combine(Pasta, Path.GetFileName(arquivo));

    // prefixo: "equip-" na foto da máquina (cadastro do equipamento), para saber de onde é o arquivo na pasta.
    public async Task<string?> SalvarAsync(IFormFile arquivo, string prefixo = "")
    {
        if (arquivo.Length == 0) return null;
        using var entrada = new MemoryStream();
        await arquivo.CopyToAsync(entrada);
        entrada.Position = 0;

        using var codec = SKCodec.Create(entrada);
        if (codec == null) return null;
        using var original = SKBitmap.Decode(codec);
        if (original == null) return null;
        using var orientada = Orientar(original, codec.EncodedOrigin);

        var escala = Math.Min(1.0, (double)LadoMaximo / Math.Max(orientada.Width, orientada.Height));
        var w = Math.Max(1, (int)(orientada.Width * escala));
        var h = Math.Max(1, (int)(orientada.Height * escala));
        using var reduzida = escala < 1
            ? orientada.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            : orientada.Copy();

        var nome = $"{prefixo}{Guid.NewGuid():N}.jpg";
        using var img = SKImage.FromBitmap(reduzida);
        using var dados = img.Encode(SKEncodedImageFormat.Jpeg, 85);
        await using var fs = File.Create(Caminho(nome));
        dados.SaveTo(fs);
        return nome;
    }

    // Logo do cliente da área (cartões do Dashboard): PNG para manter o fundo transparente, até 600px.
    // Não é foto de OS: não tem data na imagem.
    public async Task<string?> SalvarLogoAsync(IFormFile arquivo)
    {
        if (arquivo.Length == 0) return null;
        using var entrada = new MemoryStream();
        await arquivo.CopyToAsync(entrada);
        entrada.Position = 0;
        using var original = SKBitmap.Decode(entrada);
        if (original == null) return null;
        var escala = Math.Min(1.0, 600.0 / Math.Max(original.Width, original.Height));
        using var reduzida = escala < 1
            ? original.Resize(new SKImageInfo(Math.Max(1, (int)(original.Width * escala)), Math.Max(1, (int)(original.Height * escala))),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            : original.Copy();
        var nome = $"logo-{Guid.NewGuid():N}.png";
        using var img = SKImage.FromBitmap(reduzida);
        using var dados = img.Encode(SKEncodedImageFormat.Png, 100);
        await using var fs = File.Create(Caminho(nome));
        dados.SaveTo(fs);
        return nome;
    }

    // Fotos de celular vêm com a orientação no EXIF; grava já na posição certa. (Também usada pelo PDF das evidências da NRE.)
    internal static SKBitmap Orientar(SKBitmap bmp, SKEncodedOrigin origem)
    {
        var graus = origem switch
        {
            SKEncodedOrigin.BottomRight => 180,
            SKEncodedOrigin.RightTop => 90,
            SKEncodedOrigin.LeftBottom => 270,
            _ => 0,
        };
        if (graus == 0) return bmp.Copy();
        var girar90 = graus != 180;
        var destino = new SKBitmap(girar90 ? bmp.Height : bmp.Width, girar90 ? bmp.Width : bmp.Height);
        using var canvas = new SKCanvas(destino);
        canvas.Translate(destino.Width / 2f, destino.Height / 2f);
        canvas.RotateDegrees(graus);
        canvas.Translate(-bmp.Width / 2f, -bmp.Height / 2f);
        using var img = SKImage.FromBitmap(bmp);
        canvas.DrawImage(img, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
        return destino;
    }

    public void Apagar(string arquivo)
    {
        var c = Caminho(arquivo);
        if (File.Exists(c)) File.Delete(c);
    }

    // Quando a foto foi tirada: a data que a câmera grava dentro da foto (EXIF); sem ela (print, foto reenviada
    // pelo WhatsApp), a data do arquivo que o navegador informou ("informada", yyyy-MM-ddTHH:mm:ss); sem nada, agora.
    public static async Task<DateTime> HoraDaFotoAsync(IFormFile arquivo, string? informada, DateTime agora)
    {
        var inicio = new byte[(int)Math.Min(arquivo.Length, 256 * 1024)]; // o EXIF fica no começo do arquivo
        await using var s = arquivo.OpenReadStream();
        int lidos = 0, n;
        while (lidos < inicio.Length && (n = await s.ReadAsync(inicio.AsMemory(lidos))) > 0) lidos += n;
        if (LerExif(inicio, lidos) is DateTime gravada) return gravada;
        return DateTime.TryParseExact(informada, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : agora;
    }

    // JPEG → bloco APP1 "Exif" → DateTimeOriginal (0x9003) ou, se não tiver, DateTime (0x0132): "2026:10:01 08:42:13".
    static DateTime? LerExif(byte[] b, int n)
    {
        try
        {
            if (n < 4 || b[0] != 0xFF || b[1] != 0xD8) return null; // não é JPEG
            for (var p = 2; p + 10 < n && b[p] == 0xFF;)
            {
                int marca = b[p + 1], tam = b[p + 2] << 8 | b[p + 3];
                if (marca == 0xDA) break; // começou a imagem: não tem EXIF
                if (marca == 0xE1 && Encoding.ASCII.GetString(b, p + 4, 4) == "Exif") return LerTiff(b, p + 10);
                p += 2 + tam;
            }
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException) { } // EXIF cortado ou estranho
        return null;
    }

    static DateTime? LerTiff(byte[] b, int t)
    {
        var le = b[t] == 'I'; // "II" = bytes ao contrário (Intel), "MM" = Motorola
        int U16(int o) => le ? b[t + o] | b[t + o + 1] << 8 : b[t + o] << 8 | b[t + o + 1];
        int U32(int o) => le ? U16(o) | U16(o + 2) << 16 : U16(o) << 16 | U16(o + 2);
        int? Campo(int ifd, int tag)
        {
            for (var i = 0; i < U16(ifd); i++)
                if (U16(ifd + 2 + i * 12) == tag) return ifd + 2 + i * 12;
            return null;
        }
        string Texto(int campo) => Encoding.ASCII.GetString(b, t + U32(campo + 8), 19);

        if (U16(2) != 42) return null;
        var ifd0 = U32(4);
        string? data = null;
        if (Campo(ifd0, 0x8769) is int exif && Campo(U32(exif + 8), 0x9003) is int original) data = Texto(original);
        if (data == null && Campo(ifd0, 0x0132) is int alterada) data = Texto(alterada);
        return DateTime.TryParseExact(data, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }
}
