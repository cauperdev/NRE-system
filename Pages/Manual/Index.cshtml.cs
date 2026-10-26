// ============================================================================
// TELA: MANUAL DO NRE (lógica)  ·  endereço /Manual  ·  visual e texto em Index.cshtml  ·  permissão "manual"
// Como usar o sistema NRE, tela por tela (pedido do usuário em 24/10/2026). Só leitura: não há o que editar.
// O que muda com o sistema (permissões, taxa padrão, limites de arquivo e de login) vem do próprio código.
// ============================================================================
using MS.Servicos;

namespace MS.Pages.Manual;

public class IndexModel : MsPage
{
    protected override string Modulo => "manual";

    public void OnGet() { }
}
