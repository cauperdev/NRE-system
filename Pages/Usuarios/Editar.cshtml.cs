// ============================================================================
// TELA: USUÁRIOS — cadastro (lógica)  ·  endereço /Usuarios/Editar/{id}  ·  visual em Editar.cshtml
// ONDE MEXER:
//   - Carregar na tela ...................... OnGetAsync
//   - Validações (senha mínima de 6 etc.) e salvar  OnPostAsync
//   - Login pela rede (AD) ligado: Usuário = matrícula e a senha é a do Windows; só "conta local" (marcada pelo
//     Administrador, ex.: admin de emergência) tem senha do sistema.
//   - Remover ............................... OnPostRemoverAsync
// Coordenador não mexe em conta de Administrador (Regra 07).
// ============================================================================
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Usuarios;

public class EditarModel(AppDb db, Auditoria auditoria, Rede rede) : MsPage
{
    protected override string Modulo => "usuarios";

    [BindProperty] public Usuario Usu { get; set; } = new();
    public List<Celula> Celulas { get; set; } = new();
    // Login pela rede ligado: a senha só existe para conta local (ex.: admin de emergência).
    public bool PelaRede => rede.Ativo;
    public bool UsaSenhaDoSistema => !PelaRede || Usu.ContaLocal;

    // Coordenador não mexe em conta de Administrador nem cria administradores.
    public bool Bloqueado { get; set; }
    public IEnumerable<Grupo> GruposPermitidos =>
        Acesso.Grupos.Where(g => Grupo == Grupo.Administrador || g != Grupo.Administrador);

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id != null)
        {
            var u = await db.Usuarios.FindAsync(id);
            if (u == null) return NotFound();
            Usu = u;
        }
        Bloqueado = Usu.Grupo == Grupo.Administrador && Grupo != Grupo.Administrador;
        Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, string acao, string? novaSenha)
    {
        Usuario? u = id is int uid ? await db.Usuarios.FindAsync(uid) : null;
        if (id != null && u == null) return NotFound();
        if (Grupo != Grupo.Administrador && (u?.Grupo == Grupo.Administrador || Usu.Grupo == Grupo.Administrador))
            return RedirectToPage("/Conta/Negado");

        Usu.Login = Usu.Login.Trim();
        // Conta local (entra com a senha do sistema) só o Administrador marca ou desmarca.
        var contaLocal = Grupo == Grupo.Administrador ? Usu.ContaLocal : (u?.ContaLocal ?? false);
        var usaSenha = !rede.Ativo || contaLocal;
        if (!Acesso.Grupos.Contains(Usu.Grupo)) ModelState.AddModelError("", "Escolha um grupo da lista.");
        if (Usu.Login == "" || Usu.PrimeiroNome.Trim() == "") ModelState.AddModelError("", "Preencha usuário e primeiro nome.");
        if (await db.Usuarios.AnyAsync(x => x.Login == Usu.Login && x.Id != (id ?? 0)))
            ModelState.AddModelError("", $"O usuário {Usu.Login} já existe.");
        if (usaSenha && string.IsNullOrEmpty(novaSenha) && string.IsNullOrEmpty(u?.SenhaHash)) ModelState.AddModelError("", "Informe a senha.");
        if (usaSenha && !string.IsNullOrEmpty(novaSenha) && novaSenha.Length < 6) ModelState.AddModelError("", "A senha precisa ter ao menos 6 caracteres.");
        if (u != null && u.Id == UsuarioId && (!Usu.Ativo || Usu.Grupo != u.Grupo))
            ModelState.AddModelError("", "Você não pode desativar nem trocar o grupo da sua própria conta.");
        if (u != null && u.Id == UsuarioId && rede.Ativo && u.ContaLocal && !contaLocal)
            ModelState.AddModelError("", "Você não pode tirar a conta local da sua própria conta (é o acesso de emergência).");
        if (!ModelState.IsValid)
        {
            Usu.Id = id ?? 0;
            Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
            return Page();
        }

        var novo = u == null;
        u ??= db.Usuarios.Add(new Usuario()).Entity;
        var antes = new Usuario
        {
            Login = u.Login, Email = u.Email, PrimeiroNome = u.PrimeiroNome, UltimoNome = u.UltimoNome,
            Grupo = u.Grupo, CelulaId = u.CelulaId, Ativo = u.Ativo, MembroEquipe = u.MembroEquipe, ContaLocal = u.ContaLocal,
            RecebeEmail = u.RecebeEmail,
        };
        u.Login = Usu.Login;
        u.Email = (Usu.Email ?? "").Trim();
        u.RecebeEmail = Usu.RecebeEmail;
        u.PrimeiroNome = Usu.PrimeiroNome.Trim();
        u.UltimoNome = (Usu.UltimoNome ?? "").Trim();
        u.Grupo = Usu.Grupo;
        u.CelulaId = Usu.CelulaId;
        u.Ativo = Usu.Ativo;
        u.MembroEquipe = Usu.MembroEquipe;
        u.ContaLocal = contaLocal;
        // Pela rede a senha é a do Windows: senha antiga do sistema é apagada para não servir de porta dos fundos.
        if (!usaSenha) { u.SenhaHash = ""; novaSenha = null; }
        else if (!string.IsNullOrEmpty(novaSenha)) u.SenhaHash = new PasswordHasher<Usuario>().HashPassword(u, novaSenha);
        await db.SaveChangesAsync();

        if (novo)
            auditoria.Registrar("Usuario", u.Id, "Criado", $"{u.Login} · {u.NomeCompleto} · {u.Grupo.Nome()} · {(u.Ativo ? "ativo" : "inativo")}"
                + (rede.Ativo ? $" · {(u.ContaLocal ? "conta local" : "rede (AD)")}" : ""));
        else
        {
            // Só o que mudou, de quanto para quanto (a senha nunca aparece, só o aviso de que foi trocada).
            var celulas = await db.Celulas.ToDictionaryAsync(c => c.Id, c => c.Nome);
            var mudou = Auditoria.Mudancas(
                ("Usuário", antes.Login, u.Login),
                ("Primeiro nome", antes.PrimeiroNome, u.PrimeiroNome),
                ("Último nome", antes.UltimoNome, u.UltimoNome),
                ("E-mail", antes.Email, u.Email),
                ("Recebe os avisos por e-mail", antes.RecebeEmail, u.RecebeEmail),
                ("Grupo", antes.Grupo.Nome(), u.Grupo.Nome()),
                ("Área", antes.CelulaId is int a ? celulas.GetValueOrDefault(a) : null, u.CelulaId is int d ? celulas.GetValueOrDefault(d) : null),
                ("Ativo", antes.Ativo, u.Ativo),
                ("Membro da equipe", antes.MembroEquipe, u.MembroEquipe),
                ("Acesso", antes.ContaLocal ? "Conta local" : "Rede (AD)", u.ContaLocal ? "Conta local" : "Rede (AD)"));
            if (!string.IsNullOrEmpty(novaSenha)) mudou += (mudou == "" ? "" : "\n") + "Senha: alterada";
            if (mudou != "") auditoria.Registrar("Usuario", u.Id, "Alterado", mudou);
        }
        await db.SaveChangesAsync();
        Ok($"Usuário {u.Login} salvo.");

        return acao switch
        {
            "outro" => RedirectToPage("Editar", new { id = (int?)null }),
            "continuar" => RedirectToPage("Editar", new { id = u.Id }),
            _ => RedirectToPage("Index"),
        };
    }

    public async Task<IActionResult> OnPostRemoverAsync(int id)
    {
        var u = await db.Usuarios.FindAsync(id);
        if (u == null || u.Id == UsuarioId) return RedirectToPage("Index");
        if (u.Grupo == Grupo.Administrador && Grupo != Grupo.Administrador) return RedirectToPage("/Conta/Negado");
        await db.SoltarDosPlanosAsync([id]);
        db.Usuarios.Remove(u);
        auditoria.Registrar("Usuario", id, "Removido", u.Login);
        await db.SaveChangesAsync();
        Ok($"Usuário {u.Login} removido.");
        return RedirectToPage("Index");
    }
}
