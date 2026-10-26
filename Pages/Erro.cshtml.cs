// ============================================================================
// TELA DE ERRO (lógica)  ·  visual em Pages/Erro.cshtml
// Guarda o código do erro (para o administrador achar o problema no registro do servidor).
// ============================================================================
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MS.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErroModel : PageModel
{
    public string? CodigoErro { get; set; }
    // 404 = página não encontrada (endereço errado ou registro apagado); vazio = falha inesperada.
    [BindProperty(SupportsGet = true)] public int? Codigo { get; set; }
    public bool NaoEncontrada => Codigo == 404;

    public bool MostrarCodigo => !NaoEncontrada && !string.IsNullOrEmpty(CodigoErro);

    public void OnGet() => CodigoErro = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    public void OnPost() => OnGet(); // erro num envio de formulário mostra a mesma tela
}
