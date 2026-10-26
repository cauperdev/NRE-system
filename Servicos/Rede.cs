// ============================================================================
// LOGIN PELA REDE (AD da empresa) — confere a matrícula e a senha de rede (a mesma do Windows) no Active Directory.
// O sistema NÃO guarda a senha de rede: só pergunta ao AD se está certa. O grupo (Técnico, Coordenador...)
// continua sendo o da tela Usuários.
// Configuração (appsettings.json):
//   "AD": { "Ativo": true, "Dominio": "EMPRESA.LOCAL", "Servidor": "", "CadastrarNoPrimeiroAcesso": true }
//   - Servidor ........................ em branco = o próprio domínio escolhe o servidor (ou o que a TI indicar)
//   - CadastrarNoPrimeiroAcesso ....... matrícula da rede ainda sem cadastro entra como Visitante
// ONDE MEXER:
//   - Conferir matrícula e senha ...... Rede.ConferirAsync
//   - Senha errada várias vezes ....... TentativasLogin (pausa antes de o AD bloquear a conta de rede da pessoa)
// Quem usa: Pages/Conta/Login.cshtml.cs. Conta local (Usuario.ContaLocal, ex.: admin) não passa por aqui.
// ============================================================================
using System.Collections.Concurrent;
using System.DirectoryServices.AccountManagement;

namespace MS.Servicos;

public enum ResultadoRede { Ok, SenhaErrada, SemConexao }

// Nome e e-mail que o AD tem da pessoa (usados no primeiro acesso).
public record PessoaDaRede(string PrimeiroNome, string UltimoNome, string Email);

public class Rede(IConfiguration config, ILogger<Rede> log)
{
    public bool Ativo => config.GetValue("AD:Ativo", false) && OperatingSystem.IsWindows();
    public bool CadastrarNoPrimeiroAcesso => config.GetValue("AD:CadastrarNoPrimeiroAcesso", true);

    // Sem resposta do AD em 20 s (fora da rede da empresa, por exemplo), desiste.
    public async Task<(ResultadoRede Resultado, PessoaDaRede? Pessoa)> ConferirAsync(string matricula, string senha)
    {
        // Senha vazia o AD aceita como "anônimo": nunca deixa passar.
        if (string.IsNullOrEmpty(matricula) || string.IsNullOrEmpty(senha)) return (ResultadoRede.SenhaErrada, null);
        try
        {
            return await Task.Run(() => Conferir(matricula, senha)).WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException)
        {
            log.LogWarning("AD não respondeu em 20 s");
            return (ResultadoRede.SemConexao, null);
        }
    }

    (ResultadoRede, PessoaDaRede?) Conferir(string matricula, string senha)
    {
        if (!OperatingSystem.IsWindows()) return (ResultadoRede.SemConexao, null);
        try
        {
            var servidor = config["AD:Servidor"];
            using var ctx = new PrincipalContext(ContextType.Domain, string.IsNullOrWhiteSpace(servidor) ? config["AD:Dominio"] : servidor);
            // Kerberos/NTLM com a conversa criptografada: a senha não passa aberta pela rede até o AD.
            if (!ctx.ValidateCredentials(matricula, senha, ContextOptions.Negotiate | ContextOptions.Signing | ContextOptions.Sealing))
                return (ResultadoRede.SenhaErrada, null);
            using var p = UserPrincipal.FindByIdentity(ctx, IdentityType.SamAccountName, matricula);
            if (p == null || p.Enabled == false) return (ResultadoRede.SenhaErrada, null);
            return (ResultadoRede.Ok, new PessoaDaRede(p.GivenName ?? matricula, p.Surname ?? "", p.EmailAddress ?? ""));
        }
        catch (Exception ex) when (ex is PrincipalServerDownException or PrincipalOperationException
                                       or System.DirectoryServices.DirectoryServicesCOMException or System.Runtime.InteropServices.COMException)
        {
            log.LogWarning(ex, "Falha ao falar com o AD");
            return (ResultadoRede.SemConexao, null);
        }
    }
}

// Senha errada várias vezes seguidas na mesma matrícula: pausa de 15 minutos.
// Fica abaixo do limite do AD da empresa, para ninguém conseguir bloquear a conta de rede de outra pessoa pelo sistema.
public class TentativasLogin
{
    public const int Maximo = 4;
    public static readonly TimeSpan Pausa = TimeSpan.FromMinutes(15);
    readonly ConcurrentDictionary<string, (int Erros, DateTime Desde)> erros = new(StringComparer.OrdinalIgnoreCase);

    public bool Bloqueado(string login) =>
        erros.TryGetValue(login, out var e) && e.Erros >= Maximo && DateTime.Now - e.Desde < Pausa;

    public void Errou(string login) =>
        erros.AddOrUpdate(login, _ => (1, DateTime.Now),
            (_, e) => DateTime.Now - e.Desde >= Pausa ? (1, DateTime.Now) : (e.Erros + 1, e.Desde));

    public void Acertou(string login) => erros.TryRemove(login, out _);
}
