// ============================================================================
// TELA: LOGIN (lógica)  ·  endereço /Conta/Login  ·  aberta sem login
// Abrir esta tela ou tentar entrar encerra a sessão que estiver aberta no navegador.
// Duas formas de entrar:
//   - Pela rede (AD) ..... matrícula + senha do Windows, conferidas no AD da empresa (Servicos/Rede.cs). O sistema não
//                          guarda a senha de rede. Matrícula que ainda não tem cadastro entra como Visitante
//                          (o administrador muda o grupo depois na tela Usuários).
//   - Conta local ........ usuários marcados como "conta local" (ex.: admin de emergência) usam a senha do sistema.
//                          Com o AD desligado no appsettings.json, todos entram com a senha do sistema (como antes).
// Abre a sessão (dura 12 h — Program.cs) e volta para a página pedida (ex.: a que o QR code abriu) ou vai para a tela
// dos cartões dos clientes (Conta/Area: escolher a área), que depois abre a tela inicial do grupo (Acesso.PaginaInicial).
// ONDE MEXER: regras de entrada e mensagens → OnPostAsync · primeiro acesso pela rede → Cadastrar
// ============================================================================
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Conta;

// A proteção do formulário é conferida à mão em OnPostAsync, depois de encerrar a sessão aberta: se outra aba
// entrou depois que esta tela abriu, a conferência automática recusava o envio e mostrava a tela de erro com o
// menu de quem estava logado (parecia que o login errado tinha entrado).
[IgnoreAntiforgeryToken]
public class LoginModel(AppDb db, IAntiforgery antiforgery, Rede rede, TentativasLogin tentativas, Auditoria auditoria) : PageModel
{
    [BindProperty] public string Login { get; set; } = "";
    [BindProperty] public string Senha { get; set; } = "";
    public string? Erro { get; set; }
    public bool PelaRede => rede.Ativo;

    const string Invalido = "Matrícula ou senha inválidas.";

    // Abrir a tela de login encerra a sessão aberta neste navegador (vale para todas as abas).
    public async Task OnGetAsync() => await EncerrarSessao();

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        // Tentar entrar também encerra a sessão aberta: login errado nunca deixa ninguém dentro do sistema.
        await EncerrarSessao();
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            Erro = "A tela de login estava desatualizada. Digite matrícula e senha de novo.";
            return Page();
        }

        var login = (Login ?? "").Trim();
        if (tentativas.Bloqueado(login))
        {
            Erro = $"Muitas senhas erradas para {login}. Aguarde {TentativasLogin.Pausa.TotalMinutes:0} minutos e tente de novo.";
            return Page();
        }

        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Login == login);
        if (u != null && (u.ContaLocal || !rede.Ativo))
        {
            // Conta local: senha guardada no sistema.
            if (string.IsNullOrEmpty(u.SenhaHash) || string.IsNullOrEmpty(Senha) ||
                new PasswordHasher<Usuario>().VerifyHashedPassword(u, u.SenhaHash, Senha) == PasswordVerificationResult.Failed)
                return Errou(login);
        }
        else if (rede.Ativo)
        {
            // Pela rede: o AD confere a matrícula e a senha do Windows.
            var (resultado, pessoa) = await rede.ConferirAsync(login, Senha ?? "");
            if (resultado == ResultadoRede.SemConexao)
            {
                Erro = "Não foi possível falar com a rede da empresa (AD). Confira se o computador ou celular está na rede da empresa e tente de novo.";
                return Page();
            }
            if (resultado == ResultadoRede.SenhaErrada) return Errou(login);
            if (u == null)
            {
                if (!rede.CadastrarNoPrimeiroAcesso)
                {
                    Erro = "Sua matrícula ainda não tem acesso ao sistema. Peça o cadastro ao administrador.";
                    return Page();
                }
                u = await Cadastrar(login, pessoa!);
            }
        }
        else return Errou(login);

        if (!u.Ativo)
        {
            Erro = "Seu acesso ao sistema está desativado. Fale com o administrador.";
            return Page();
        }
        tentativas.Acertou(login);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.NomeCompleto),
            new(ClaimTypes.Role, u.Grupo.ToString()),
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) && returnUrl != "/") return LocalRedirect(returnUrl);
        // Antes do sistema, a tela dos cartões dos clientes (escolher a área); de lá vai para a tela inicial do grupo.
        return RedirectToPage("/Conta/Area");
    }

    // Mesma mensagem para matrícula que não existe e senha errada (não revela quem tem acesso).
    IActionResult Errou(string login)
    {
        if (login != "") tentativas.Errou(login);
        Erro = Invalido;
        return Page();
    }

    // Primeiro acesso pela rede: cadastra a matrícula como Visitante, com nome e e-mail do AD.
    async Task<Usuario> Cadastrar(string matricula, PessoaDaRede p)
    {
        var u = new Usuario
        {
            Login = matricula, PrimeiroNome = p.PrimeiroNome.ToUpper(), UltimoNome = p.UltimoNome.ToUpper(), Email = p.Email,
            Grupo = Grupo.Visitante, Ativo = true, MembroEquipe = false, ContaLocal = false, SenhaHash = "",
        };
        db.Usuarios.Add(u);
        await db.SaveChangesAsync();
        auditoria.Registrar("Usuario", u.Id, "Criado", $"Primeiro acesso pela rede (AD) · {u.NomeCompleto} · Visitante");
        await db.SaveChangesAsync();
        return u;
    }

    async Task EncerrarSessao()
    {
        if (User.Identity?.IsAuthenticated != true) return;
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity()); // o formulário desta resposta já sai para "ninguém logado"
    }
}
