// ============================================================================
// HTTPS (cadeado) — a senha de rede digitada no celular/outro PC viaja criptografada.
// Configuração (appsettings.json):
//   "Https": { "Ativo": true, "Porta": 5463, "Certificado": "", "SenhaCertificado": "" }
//   - Certificado em branco ... o sistema gera um certificado próprio (dados/https-certificado.pfx) com o nome e os
//                               IPs deste PC; refaz sozinho se o IP mudar. O navegador avisa "não seguro" até a TI
//                               fornecer um certificado da empresa.
//   - Certificado da TI ....... caminho do .pfx + senha: o aviso some.
// Quem abre pelo próprio PC (localhost) continua em http, sem aviso; quem vem pela rede é levado para https
// (Program.cs, bloco "HTTPS"). QR codes antigos com http continuam funcionando (são redirecionados).
// ============================================================================
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MS.Servicos;

public static class Https
{
    const string Arquivo = "https-certificado.pfx";
    // Senha só do arquivo do certificado próprio (fica na pasta dados, neste PC).
    const string SenhaArquivo = "ms-system-maintenance";

    public static X509Certificate2? Certificado(IConfiguration config, string contentRoot)
    {
        if (!config.GetValue("Https:Ativo", false)) return null;
        var daTi = config["Https:Certificado"];
        if (!string.IsNullOrWhiteSpace(daTi))
            return X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(contentRoot, daTi), config["Https:SenhaCertificado"]);

        var caminho = Path.Combine(contentRoot, "dados", Arquivo);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        if (File.Exists(caminho))
        {
            var atual = X509CertificateLoader.LoadPkcs12FromFile(caminho, SenhaArquivo);
            if (Serve(atual)) return atual;
        }
        var novo = Gerar();
        File.WriteAllBytes(caminho, novo.Export(X509ContentType.Pfx, SenhaArquivo));
        return X509CertificateLoader.LoadPkcs12FromFile(caminho, SenhaArquivo);
    }

    // IPv4 deste PC que estão ligados (Wi-Fi, cabo) + localhost.
    static List<IPAddress> Ips() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Append(IPAddress.Loopback).Distinct().ToList();

    // O certificado próprio ainda vale: falta mais de 30 dias para vencer e cobre todos os IPs atuais.
    static bool Serve(X509Certificate2 c)
    {
        if (c.NotAfter < DateTime.Now.AddDays(30)) return false;
        var san = c.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        if (san == null) return false;
        var cobertos = san.EnumerateIPAddresses().ToHashSet();
        return Ips().All(cobertos.Contains);
    }

    static X509Certificate2 Gerar()
    {
        using var rsa = RSA.Create(2048);
        var maquina = Environment.MachineName;
        var req = new CertificateRequest($"CN={maquina}, O=MS-SYSTEM MAINTENANCE", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var nomes = new SubjectAlternativeNameBuilder();
        nomes.AddDnsName(maquina);
        nomes.AddDnsName("localhost");
        var dominio = IPGlobalProperties.GetIPGlobalProperties().DomainName;
        if (!string.IsNullOrWhiteSpace(dominio)) nomes.AddDnsName($"{maquina}.{dominio}");
        foreach (var ip in Ips()) nomes.AddIpAddress(ip);
        req.CertificateExtensions.Add(nomes.Build());
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false)); // servidor web
        return req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
    }
}
