// ============================================================================
// TELA: NRE — abrir / acompanhar uma NRE (lógica)  ·  endereço /Nre/Editar/{id}  ·  visual em Editar.cshtml
// Abas, uma por planilha de NRE da Samsung: Resumo = planilha azul (no alto o que se digita dela: aprovação, modelo,
// item/projeto, PO, evidência, NRE Order; embaixo os números e a tabela por DI, calculados) · Detalhado = planilha
// amarela (um bloco por DI ou compra nacional, com itens, frete e seguro, e o fechamento) · POs (código de cada compra e
// o link do site de compras: SiteCompras / LinkPo) · Máquina e descrição (só no sistema; id aba-dados) · Arquivos
// (invoices, DIs, NF e outros). As contas do Resumo saem do Detalhado.
// Ligação com as máquinas: o campo Equipamento é o TIPO de máquina (nome, ex.: ADESIVADORA — lista sem repetir, pode
// digitar outro ou deixar em branco). A célula (cliente que paga) é escolhida na própria NRE. A máquina exata
// (EquipamentoId) só fica gravada quando a NRE vem de uma OS ou do cadastro da máquina e o tipo não é trocado:
//   /Nre/Editar?os=123          → abre com o tipo e a célula da máquina da OS e as peças lançadas nela (compra nacional)
//   /Nre/Editar?equipamento=45  → abre com o tipo e a célula da máquina (o botão "Abrir NRE" do cadastro do equipamento saiu em 19/10/2026)
// Fluxo: Aguardando aprovação → Concluída. Sem botões Aprovar, Recusar, Marcar como comprada e Marcar como cobrada do
// cliente na tela (pedidos do usuário; os dois últimos saíram em 08/10/2026): a aprovação do cliente é só a data e o
// aprovador do Resumo, e concluir não depende dela. Os handlers Aprovar/Recusar/Comprar/Cobrar e os status Aprovada,
// Recusada, Aguardando chegada e Cobrada ficaram para as NRE antigas. Tudo (máquina, área,
// tipo, descrição, Detalhado, aprovação, título...) muda até concluir — as DIs e NFs chegam depois da compra.
// ONDE MEXER:
//   - Abrir já preenchida (OS / máquina) ... OnGetAsync
//   - O que cada botão grava ............... Aplicar (campos e Detalhado) + OnPostAsync / Aprovar / Recusar / Comprar /
//                                            Cobrar / Concluir
//   - Contas do Resumo ..................... Modelos/Entidades.cs (Nre e NreRemessa) · linhas da aba Summary: Servicos/ResumoNre.cs
//   - Números digitados (pt-BR) ............ Dinheiro (R$ e US$: 198.327,26 ou 198,327.26) · Decimal4 (PTAX e %)
//   - Planilha do botão Extração ........... OnGetExtracaoAsync (abas Resumo e Detalhado: Servicos/ResumoNre.cs)
//   - Quem aprova, recusa e remove ......... permissão "nre_aprovacao" (tela Grupos)
//   - Arquivos (invoice, DI, NF...) ........ GuardarAnexos (vão junto com qualquer botão) · OnPostAnexarAsync ·
//                                            OnPostRemoverAnexoAsync · OnGetAnexoAsync (abrir) · guardados: Servicos/Anexos.cs
// ============================================================================
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Nre;

// Vários arquivos num Salvar só (PDF de invoice, DI...): o padrão de 30 MB por envio é pouco.
[RequestSizeLimit(200_000_000)]
[RequestFormLimits(MultipartBodyLengthLimit = 200_000_000, ValueCountLimit = 100_000)]
public class EditarModel(AppDb db, Regras regras, Auditoria auditoria, Anexos anexos, IConfiguration config) : MsPage
{
    protected override string Modulo => "nre";
    [BindProperty] public List<IFormFile> NovosAnexos { get; set; } = new();
    [BindProperty] public TipoAnexoNre TipoAnexo { get; set; }   // tipo dos arquivos escolhidos (aba Arquivos)

    // Linha do Detalhado como foi digitada (números em texto: a cultura pt-BR leria "1.5" como 15).
    public class ItemInput
    {
        public string? Codigo { get; set; }
        public string? Descricao { get; set; }
        public string? Quantidade { get; set; }
        public string? ValorUnitario { get; set; }      // R$ (compra nacional; nos itens antigos de DI vai escondido)
        public string? ValorUnitarioUsd { get; set; }   // US$ (importação)

        public static decimal Numero(string? s, decimal padrao) =>
            decimal.TryParse(s?.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : padrao;
    }

    // Bloco do Detalhado (uma DI ou uma compra nacional) como foi digitado.
    public class RemessaInput
    {
        public TipoRemessa Tipo { get; set; } = TipoRemessa.Importacao;
        public string? Di { get; set; }
        public string? Invoice { get; set; }
        public string? NotaFiscal { get; set; }
        public string? Fornecedor { get; set; }
        public string? Ptax { get; set; }
        public string? Frete { get; set; }
        public string? Seguro { get; set; }
        public List<ItemInput> Itens { get; set; } = new();
    }

    // O que as partes _BlocoNre / _ItemNre desenham (Indice -1: modelo vazio para o botão "+ DI" / "+ Item").
    public record BlocoVisao(int Indice, RemessaInput Entrada, NreRemessa Calculo, bool Editando, bool PodeEditar);
    public record ItemVisao(int Bloco, int Indice, ItemInput Entrada, NreItem Calculo, NreRemessa CalculoBloco);

    [BindProperty] public Modelos.Nre Registro { get; set; } = new();
    [BindProperty] public List<RemessaInput> Remessas { get; set; } = new();
    // Números da aprovação e do fechamento, como foram digitados.
    [BindProperty] public string? AprovadoUsd { get; set; }
    [BindProperty] public string? PtaxAprovacao { get; set; }
    [BindProperty] public string? TaxaPercentual { get; set; }
    [BindProperty] public string? CustoAbsorvido { get; set; }
    [BindProperty] public string? Motivo { get; set; }        // motivo da recusa (janela do botão Recusar)
    [BindProperty(SupportsGet = true)] public string? Aba { get; set; }   // aba aberta (#aba-resumo...): volta para ela ao salvar
    [BindProperty] public List<string?> Pos { get; set; } = new();       // aba POs: um código por linha

    // Site de compras da empresa (appsettings.json "SiteComprasPo"). Com {po} no endereço, o código da PO entra no lugar
    // (ex.: https://compras.empresa.com/po/{po}); sem {po}, o botão só abre o site. Vazio = sem botão.
    public string? SiteCompras => config["SiteComprasPo"] is { Length: > 0 } s ? s.Trim() : null;
    public string? LinkPo(string? po) =>
        SiteCompras is not string site || string.IsNullOrWhiteSpace(po) ? null
        : site.Contains("{po}") ? site.Replace("{po}", Uri.EscapeDataString(po.Trim())) : site;

    // POs gravadas em Nre.PedidoCompra separadas por vírgula (aceita também ; e quebra de linha, das NRE antigas).
    static List<string?> SepararPos(string? s) =>
        (s ?? "").Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList<string?>();
    string? JuntarPos() =>
        Pos.Select(Limpo).OfType<string>().Select(p => p.ToUpper()).Distinct().ToList() is { Count: > 0 } l ? string.Join(", ", l) : null;

    public List<string> TiposDeMaquina { get; set; } = new();   // nomes das máquinas cadastradas, sem repetir
    public List<Celula> Celulas { get; set; } = new();
    // O que a tela mostra no Resumo e no Detalhado: a NRE com os números e blocos digitados (ao abrir, os salvos).
    public Modelos.Nre Visao { get; private set; } = new();
    public bool Novo => Registro.Id == 0;
    public bool Aprova => Pode("nre_aprovacao");

    Task<Modelos.Nre?> Buscar(int id) => db.Nres
        .Include(n => n.Equipamento).Include(n => n.Celula).Include(n => n.OrdemServico)
        .Include(n => n.Solicitante).Include(n => n.AnalisadaPor).Include(n => n.Itens).Include(n => n.Anexos)
        .Include(n => n.Remessas).ThenInclude(r => r.Itens)
        .AsSplitQuery().FirstOrDefaultAsync(n => n.Id == id);

    // ---------- números digitados ----------
    static string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // Valor em R$ ou US$. Aceita 198.327,26 · 198327,26 · 198,327.26 (Excel) · 198327.26 · R$ 10: o último ponto ou
    // vírgula separa os centavos quando tem 1 ou 2 dígitos depois dele; os outros são de milhar.
    // Preço com 4 casas (planilha da A075, 23/10/2026: R$ 345,5738): 4 ou mais dígitos depois do último separador, ou
    // ponto e vírgula juntos (4.149,185), também é decimal. 3 dígitos com um tipo só de separador (1.978) = milhar.
    // Vazio = sem valor. Devolve false se não tem número.
    public static bool Dinheiro(string? s, out decimal? valor)
    {
        valor = null;
        if (string.IsNullOrWhiteSpace(s)) return true;
        var t = new string(s.Where(c => char.IsAsciiDigit(c) || c is ',' or '.').ToArray());
        var ultimo = t.LastIndexOfAny([',', '.']);
        var casas = ultimo < 0 ? 0 : t.Length - ultimo - 1;
        var misturado = ultimo >= 0 && t[..ultimo].Contains(t[ultimo] == ',' ? '.' : ',') && !t[..ultimo].Contains(t[ultimo]);
        t = casas is 1 or 2 || (ultimo >= 0 && casas >= 4) || (casas == 3 && misturado)
            ? t[..ultimo].Replace(",", "").Replace(".", "") + "." + t[(ultimo + 1)..]
            : t.Replace(",", "").Replace(".", "");
        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)) return false;
        valor = v;
        return true;
    }

    // PTAX e percentual (5,3498 · 5.3498 · 9,3871): a vírgula ou o ponto é o decimal, sem milhar.
    public static bool Decimal4(string? s, out decimal? valor)
    {
        valor = null;
        if (string.IsNullOrWhiteSpace(s)) return true;
        var t = new string(s.Where(c => char.IsAsciiDigit(c) || c is ',' or '.').ToArray()).Replace(',', '.');
        if (t.Count(c => c == '.') > 1 || !decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)) return false;
        valor = v;
        return true;
    }

    // ---------- Detalhado: entidade ↔ o que a tela mostra ----------
    static ItemInput ParaInput(NreItem i) => new()
    {
        Codigo = i.Codigo, Descricao = i.Descricao, Quantidade = i.Quantidade.ToString("0.####"),
        ValorUnitario = i.ValorUnitario == 0 ? null : Unitario(i.ValorUnitario), ValorUnitarioUsd = i.ValorUnitarioUsd is decimal u ? Unitario(u) : null,
    };

    // Preço unitário na tela: 2 casas ou, se tiver mais (até 4, ex.: 345,5738 da A075), 4 — sempre 4 para o Dinheiro
    // não confundir "185,125" com milhar. Assim salvar de novo não arredonda o preço que veio da planilha.
    public static string Unitario(decimal v) => v == Math.Round(v, 2) ? v.ToString("N2") : v.ToString("N4");

    static RemessaInput ParaInput(NreRemessa r) => new()
    {
        Tipo = r.Tipo, Di = r.Di, Invoice = r.Invoice, NotaFiscal = r.NotaFiscal, Fornecedor = r.Fornecedor, Ptax = r.Ptax?.Ptax(),
        Frete = r.Frete == 0 ? null : r.Frete.ToString("N2"), Seguro = r.Seguro == 0 ? null : r.Seguro.ToString("N2"),
        Itens = r.Itens.Select(ParaInput).ToList(),
    };

    // Bloco para as contas da tela (o que não for número vale vazio). Mantém todas as linhas, na mesma ordem.
    public static NreRemessa BlocoDe(RemessaInput r)
    {
        Decimal4(r.Ptax, out var ptax);
        Dinheiro(r.Frete, out var frete);
        Dinheiro(r.Seguro, out var seguro);
        var bloco = new NreRemessa { Tipo = r.Tipo, Di = Limpo(r.Di), Invoice = Limpo(r.Invoice), NotaFiscal = Limpo(r.NotaFiscal),
            Fornecedor = Limpo(r.Fornecedor), Ptax = ptax, Frete = frete ?? 0, Seguro = seguro ?? 0 };
        foreach (var i in r.Itens) bloco.Itens.Add(ItemDe(i, bloco.Importacao));
        return bloco;
    }

    public static NreItem ItemDe(ItemInput i, bool importacao)
    {
        Dinheiro(i.ValorUnitario, out var brl);
        Dinheiro(i.ValorUnitarioUsd, out var usd);
        return new NreItem { Codigo = Limpo(i.Codigo), Descricao = (i.Descricao ?? "").Trim(), Quantidade = ItemInput.Numero(i.Quantidade, 1),
            ValorUnitario = brl ?? 0, ValorUnitarioUsd = importacao ? usd : null };
    }

    public BlocoVisao BlocoVazio(TipoRemessa tipo) =>
        new(-1, new RemessaInput { Tipo = tipo, Itens = [new ItemInput { Quantidade = "1" }] }, new NreRemessa { Tipo = tipo, Itens = [new NreItem()] }, true, true);
    public ItemVisao ItemVazio(TipoRemessa tipo) =>
        new(-1, -1, new ItemInput { Quantidade = "1" }, new NreItem(), new NreRemessa { Tipo = tipo });

    // Lê o Detalhado digitado para gravar: linhas sem código e sem descrição e blocos vazios ficam de fora.
    (List<NreRemessa> Blocos, string? Erro) LerDetalhado()
    {
        var blocos = new List<NreRemessa>();
        foreach (var r in Remessas)
        {
            var nome = r.Tipo == TipoRemessa.Importacao ? $"DI {Limpo(r.Di) ?? "(a definir)"}" : $"NF {Limpo(r.NotaFiscal) ?? "(a definir)"}";
            if (!Decimal4(r.Ptax, out var ptax) || ptax is <= 0 or > 100) return (blocos, $"{nome}: PTAX inválida (ex.: 5,3498).");
            if (!Dinheiro(r.Frete, out var frete) || !Dinheiro(r.Seguro, out var seguro))
                return (blocos, $"{nome}: frete e seguro só com o valor em US$ (ex.: 1.657,55).");
            var bloco = new NreRemessa
            {
                Ordem = blocos.Count, Tipo = r.Tipo, Di = Limpo(r.Di)?.ToUpper(), Invoice = Limpo(r.Invoice)?.ToUpper(),
                NotaFiscal = Limpo(r.NotaFiscal)?.ToUpper(), Fornecedor = Limpo(r.Fornecedor), Ptax = ptax,
                Frete = r.Tipo == TipoRemessa.Importacao ? frete ?? 0 : 0, Seguro = r.Tipo == TipoRemessa.Importacao ? seguro ?? 0 : 0,
            };
            foreach (var i in r.Itens.Where(i => !string.IsNullOrWhiteSpace(i.Codigo) || !string.IsNullOrWhiteSpace(i.Descricao)))
            {
                if (!Dinheiro(i.ValorUnitario, out _) || !Dinheiro(i.ValorUnitarioUsd, out _))
                    return (blocos, $"{nome}: valor unitário inválido em \"{Limpo(i.Descricao) ?? i.Codigo}\" (ex.: 1.350,00).");
                var item = ItemDe(i, bloco.Importacao);
                item.Codigo = item.Codigo?.ToUpper();
                bloco.Itens.Add(item);
            }
            var vazio = bloco.Itens.Count == 0 && bloco.Di == null && bloco.Invoice == null && bloco.NotaFiscal == null && bloco.Frete + bloco.Seguro == 0;
            if (!vazio) blocos.Add(bloco);
        }
        return (blocos, null);
    }

    // Campos da tela a partir da NRE salva (ao abrir).
    void PreencherTela(Modelos.Nre n)
    {
        AprovadoUsd = n.AprovadoUsd?.ToString("N2");
        PtaxAprovacao = n.PtaxAprovacao?.Ptax();
        TaxaPercentual = n.TaxaPercentual?.ToString("0.#############");   // todas as casas (a padrão é 9,3871094945943)
        CustoAbsorvido = n.CustoAbsorvido?.ToString("N2");
        Remessas = n.RemessasEmOrdem.Select(ParaInput).ToList();
        Pos = SepararPos(n.PedidoCompra);
    }

    // Final Amount (R$) da planilha azul: sempre valor aprovado (US$) × Ptax — calculado, não se digita.
    static decimal? FinalAmountAprovacao(decimal? usd, decimal? ptax) => usd * ptax is decimal p ? Modelos.Nre.Centavos(p) : null;

    void MontarVisao()
    {
        Dinheiro(AprovadoUsd, out var aprovado);
        Dinheiro(CustoAbsorvido, out var absorvido);
        Decimal4(PtaxAprovacao, out var ptax);
        Decimal4(TaxaPercentual, out var taxa);
        Visao = new Modelos.Nre
        {
            AprovadoUsd = aprovado, PtaxAprovacao = ptax, ValorAprovado = FinalAmountAprovacao(aprovado, ptax), TaxaPercentual = taxa, CustoAbsorvido = absorvido,
            MotivoDiferenca = Registro.MotivoDiferenca, Remessas = Remessas.Select(BlocoDe).Select((b, i) => { b.Ordem = i; return b; }).ToList(),
        };
    }

    // ---------- arquivos ----------
    // Guarda os arquivos escolhidos na NRE, com o tipo escolhido. Os recusados (tipo ou tamanho) viram aviso; os outros entram.
    // Imagem (print, foto) entra já como PDF (Servicos/Anexos.cs).
    async Task<List<string>> GuardarAnexos(Modelos.Nre n)
    {
        var nomes = new List<string>();
        var recusados = new List<string>();
        foreach (var f in NovosAnexos.Where(f => f.Length > 0 || !string.IsNullOrEmpty(f.FileName)))
        {
            var (arquivo, nome, tamanho, erro) = await anexos.SalvarAsync(f);
            if (erro != null) { recusados.Add(erro); continue; }
            n.Anexos.Add(new NreAnexo { Tipo = TipoAnexo, Nome = nome, Arquivo = arquivo!, Tamanho = tamanho, EnviadoEm = DateTime.Now, EnviadoPor = User.Identity?.Name ?? "" });
            nomes.Add(nome);
        }
        if (recusados.Count > 0)
            Erro($"Arquivo(s) não anexado(s): {string.Join("; ", recusados)}. Aceitos: {string.Join(" ", Anexos.Extensoes)} (até {Anexos.TamanhoMaximo / 1024 / 1024} MB cada).");
        return nomes;
    }

    void RegistrarAnexos(Modelos.Nre n, List<string> nomes)
    {
        if (nomes.Count > 0) auditoria.Registrar("Nre", n.Id, "Arquivo(s) anexado(s)", $"{TipoAnexo.Nome()}: " + string.Join("\n", nomes));
    }

    async Task CarregarListas()
    {
        // Tipos de máquina da área da NRE (ou, se ela não tem, da área escolhida nos cartões).
        var area = Registro.CelulaId ?? AreaId;
        TiposDeMaquina = await db.Equipamentos.Where(e => area == null || e.CelulaId == area).Select(e => e.Nome).Distinct().OrderBy(n => n).ToListAsync();
        Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
    }

    // Abriu pela OS ou pela máquina: tipo e célula vêm da máquina, que fica ligada à NRE.
    void DaMaquina(Equipamento e)
    {
        Registro.EquipamentoId = e.Id;
        Registro.Equipamento = e;
        Registro.Maquina = e.Nome;
        Registro.CelulaId = e.CelulaId;
    }

    public async Task<IActionResult> OnGetAsync(int? id, int? os, int? equipamento)
    {
        if (id != null)
        {
            var n = await Buscar(id.Value);
            if (n == null) return NotFound();
            Registro = n;
        }
        else
        {
            Registro.CriadaEm = DateTime.Now;
            var ordem = os == null ? null : await db.Ordens.Include(o => o.Pecas).Include(o => o.Equipamento)
                .FirstOrDefaultAsync(o => o.Id == os);
            if (ordem != null)
            {
                // Veio da OS: máquina da OS, OS vinculada e as peças lançadas nela numa compra nacional (o valor fica para preencher).
                Registro.OrdemServicoId = ordem.Id;
                Registro.OrdemServico = ordem;
                if (ordem.Equipamento != null) DaMaquina(ordem.Equipamento);
                if (ordem.Pecas.Count > 0)
                {
                    Registro.Remessas.Add(new NreRemessa
                    {
                        Tipo = TipoRemessa.Nacional,
                        Itens = ordem.Pecas.Select(p => new NreItem { Codigo = p.Codigo, Descricao = p.Descricao, Quantidade = p.Quantidade }).ToList(),
                    });
                    Registro.Descricao = $"Peças usadas na {ordem.Numero}.";
                }
            }
            else if (equipamento != null && await db.Equipamentos.FindAsync(equipamento) is { } eq)
                DaMaquina(eq);
            Registro.CelulaId ??= AreaId;   // NRE nova sem máquina: já vem na área escolhida nos cartões
        }
        PreencherTela(Registro);
        Aba ??= "#aba-resumo";   // nova também: os campos da planilha azul estão no alto do Resumo
        await CarregarListas();
        MontarVisao();
        return Page();
    }

    // Copia o que foi digitado para a NRE: tudo (máquina, área, tipo, descrição, Detalhado, aprovação, título, modelo,
    // NRE do cliente, fechamento) até concluir. Devolve o problema encontrado
    // (ou null se está tudo certo). Números e Detalhado são conferidos antes de mexer em qualquer coisa.
    async Task<string?> Aplicar(Modelos.Nre n)
    {
        if (!Dinheiro(AprovadoUsd, out var aprovadoUsd) || !Dinheiro(CustoAbsorvido, out var absorvido))
            return "Valores da aprovação e do fechamento: digite só o número (ex.: 5.594,88).";
        if (!Decimal4(PtaxAprovacao, out var ptaxAprovacao) || !Decimal4(TaxaPercentual, out var taxa) || taxa is < 0 or > 100)
            return "Ptax e taxa: digite só o número (ex.: 5,56 e 9,3871).";
        var concluida = n.Status == StatusNre.Concluida;
        var (blocos, erroDetalhado) = concluida ? (new List<NreRemessa>(), null) : LerDetalhado();
        if (erroDetalhado != null) return erroDetalhado;
        if (n.Editavel)
        {
            // Equipamento e Descrição são opcionais (Equipamento deixou de ser obrigatório em 24/10/2026, pedido do usuário).
            // Área obrigatória: NRE sem área não aparece em nenhuma lista (a lista é sempre de uma área).
            if (Registro.CelulaId == null) return "Escolha a Área (aba Máquina e descrição).";
            if (!await db.Celulas.AnyAsync(c => c.Id == Registro.CelulaId)) return "Área não encontrada.";
            n.Maquina = Limpo(Registro.Maquina)?.ToUpper();
            n.CelulaId = Registro.CelulaId;
            // Máquina exata (veio da OS ou do cadastro): continua ligada enquanto o tipo for o dela; trocou o tipo, solta.
            var exata = n.Id == 0 ? Registro.EquipamentoId : n.EquipamentoId;
            var nomeExata = exata == null ? null : await db.Equipamentos.Where(e => e.Id == exata).Select(e => e.Nome).FirstOrDefaultAsync();
            n.EquipamentoId = nomeExata != null && nomeExata == n.Maquina ? exata : null;
            n.Tipo = Registro.Tipo;
            n.Descricao = (Registro.Descricao ?? "").Trim();
        }
        if (!concluida)
        {
            n.Titulo = Limpo(Registro.Titulo);
            n.Modelo = Limpo(Registro.Modelo)?.ToUpper();
            n.CodigoNre = Limpo(Registro.CodigoNre)?.ToUpper();
            n.AprovadaClienteEm = Registro.AprovadaClienteEm;
            n.Aprovador = Limpo(Registro.Aprovador);
            n.AprovadoUsd = aprovadoUsd;
            n.PtaxAprovacao = ptaxAprovacao;
            n.ValorAprovado = FinalAmountAprovacao(aprovadoUsd, ptaxAprovacao);   // calculado: valor aprovado × Ptax
            // POs: aba POs (voltou em 24/10/2026). Evidência saiu da tela (pedido do usuário): não mexe no que já estava gravado.
            n.PedidoCompra = JuntarPos();
            n.TaxaPercentual = taxa;
            n.CustoAbsorvido = absorvido;
            n.MotivoDiferenca = Limpo(Registro.MotivoDiferenca);
            // Detalhado: troca os blocos e itens pelos digitados (o item fica na NRE e no bloco).
            db.NreItens.RemoveRange(n.Itens);
            db.NreRemessas.RemoveRange(n.Remessas);
            n.Itens = new();
            n.Remessas = new();
            foreach (var b in blocos)
            {
                n.Remessas.Add(b);
                n.Itens.AddRange(b.Itens);
            }
        }
        return null;
    }

    // Deu problema: mostra a tela de novo com o que foi digitado (os campos voltam sozinhos; o Detalhado vem de Remessas).
    async Task<IActionResult> MostrarDeNovo(int? id, string erro)
    {
        ModelState.AddModelError("", erro);
        if (id is int nid && await Buscar(nid) is { } salvo)
            Registro = salvo;
        else
            Registro = new Modelos.Nre
            {
                CriadaEm = DateTime.Now, OrdemServicoId = Registro.OrdemServicoId, EquipamentoId = Registro.EquipamentoId,
                OrdemServico = Registro.OrdemServicoId == null ? null : await db.Ordens.FindAsync(Registro.OrdemServicoId),
            };
        await CarregarListas();
        MontarVisao();
        return Page();
    }

    // Como a NRE estava antes de gravar, para o Histórico mostrar só o que mudou (campo: antes → depois).
    // Cada campo da tela é uma linha do Histórico, com o mesmo nome da tela; do Detalhado sai só a diferença, DI por DI
    // e item por item (Servicos/HistoricoNre.cs) — nunca a lista inteira antes e depois.
    record Foto(string? Titulo, string? Modelo, string? CodigoNre, DateTime? AprovadaEm, string? Aprovador, string? AprovadoUsd,
        string? PtaxAprovacao, string? Previsto, string? Po, string? Evidencia, string? Maquina, int? Celula, TipoNre Tipo,
        string Descricao, List<HistoricoNre.Bloco> Detalhado, string Taxa, string? CustoAbsorvido, string? MotivoDiferenca, string Total);

    static Foto Retrato(Modelos.Nre n) => new(n.Titulo, n.Modelo, n.CodigoNre, n.AprovadaClienteEm?.Date, n.Aprovador,
        n.AprovadoUsd?.Dolares(), n.PtaxAprovacao?.Ptax(), n.ValorAprovado?.Reais(), n.PedidoCompra, n.Evidencia, n.Maquina,
        n.CelulaId, n.Tipo, n.Descricao, HistoricoNre.Fotografar(n), $"{n.Taxa:0.####}%", n.CustoAbsorvido?.Reais(),
        n.MotivoDiferenca, n.TotalCobrar().Reais());

    async Task<string> Mudancas(Foto a, Modelos.Nre n)
    {
        var celulas = await db.Celulas.ToDictionaryAsync(c => c.Id, c => c.Nome);
        string? Celula(int? id) => id is int c ? celulas.GetValueOrDefault(c) : null;
        var d = Retrato(n);
        var partes = new[]
        {
            Auditoria.Mudancas(
                ("Item / Projeto", a.Titulo, d.Titulo),
                ("Modelo", a.Modelo, d.Modelo),
                ("NRE Order", a.CodigoNre, d.CodigoNre),
                ("Data da aprovação", a.AprovadaEm, d.AprovadaEm),
                ("Aprovador", a.Aprovador, d.Aprovador),
                ("Valor aprovado (US$)", a.AprovadoUsd, d.AprovadoUsd),
                ("Ptax da aprovação", a.PtaxAprovacao, d.PtaxAprovacao),
                ("Final Amount (R$)", a.Previsto, d.Previsto),
                ("PO", a.Po, d.Po),
                ("Evidência", a.Evidencia, d.Evidencia),
                ("Equipamento", a.Maquina, d.Maquina),
                ("Área", Celula(a.Celula), Celula(d.Celula)),
                ("Tipo", a.Tipo.Nome(), d.Tipo.Nome()),
                ("Descrição", a.Descricao, d.Descricao)),
            string.Join("\n", HistoricoNre.Diferencas(a.Detalhado, d.Detalhado)),
            Auditoria.Mudancas(
                ("Taxa (Pis/Cofins, CSLL, R&D)", a.Taxa, d.Taxa),
                ("Custo absorvido pela empresa", a.CustoAbsorvido, d.CustoAbsorvido),
                ("Motivo da diferença", a.MotivoDiferenca, d.MotivoDiferenca),
                ("Total a cobrar", a.Total, d.Total)),
        };
        return string.Join("\n", partes.Where(p => p != ""));
    }

    static string Resumo(Modelos.Nre n) =>
        $"{n.Tipo.Nome()} · {n.Remessas.Count} DI/NF · {n.Itens.Count} item(ns) · total a cobrar {n.TotalCobrar().Reais()}";

    object Volta(int id) => new { id, aba = Aba };

    // Salvar (nova ou alterada). NRE recusada que é corrigida e salva volta para aprovação.
    public async Task<IActionResult> OnPostAsync(int? id, string acao)
    {
        var agora = DateTime.Now;
        Modelos.Nre n;
        if (id is int nid)
        {
            var salva = await Buscar(nid);
            if (salva == null) { Erro("Esta NRE não existe mais (foi removida)."); return RedirectToPage("Index"); }
            n = salva;
        }
        else
        {
            n = new Modelos.Nre
            {
                CriadaEm = agora, Status = StatusNre.Aberta, SolicitanteId = UsuarioId == 0 ? null : UsuarioId,
                OrdemServicoId = Registro.OrdemServicoId != null && await db.Ordens.AnyAsync(o => o.Id == Registro.OrdemServicoId) ? Registro.OrdemServicoId : null,
            };
        }
        var antes = Retrato(n);
        if (await Aplicar(n) is string erro) return await MostrarDeNovo(id, erro);

        var reenviada = n.Status == StatusNre.Recusada;
        if (reenviada)
        {
            n.Status = StatusNre.Aberta;
            n.AnalisadaPorId = null;
            n.AnalisadaEm = null;
        }
        if (id == null)
        {
            n.Numero = await regras.ProximoNumeroNreAsync(agora);
            db.Nres.Add(n);
        }
        var anexados = await GuardarAnexos(n);
        await db.SaveChangesAsync();
        var mudou = id == null ? "" : await Mudancas(antes, n);
        // "Alterada" traz só o que mudou (o total a cobrar entra quando muda); criada ou reenviada: o resumo da NRE.
        if (id == null || reenviada || mudou != "")
            auditoria.Registrar("Nre", n.Id, id == null ? "Criada" : reenviada ? "Reenviada para aprovação" : "Alterada",
                id == null || reenviada ? Resumo(n) + (mudou == "" ? "" : "\n" + mudou) : mudou);
        RegistrarAnexos(n, anexados);
        await db.SaveChangesAsync();
        Ok(id == null ? $"{n.Numero} aberta: aguardando aprovação." : reenviada ? $"{n.Numero} corrigida e enviada de novo para aprovação." : $"{n.Numero} salva.");

        return acao switch
        {
            "continuar" => RedirectToPage("Editar", Volta(n.Id)),
            _ => RedirectToPage("Index"),
        };
    }

    // Muda o status (os campos digitados são gravados junto). "pode" diz se o status atual permite a mudança.
    async Task<IActionResult> Mudar(int id, Func<Modelos.Nre, bool> pode, string recusa, Action<Modelos.Nre> mudar, string acao, string msg)
    {
        var n = await Buscar(id);
        if (n == null) return RedirectToPage("Index");
        if (!pode(n)) { Erro($"{n.Numero}: {recusa}"); return RedirectToPage("Editar", Volta(id)); }
        var antes = Retrato(n);
        if (await Aplicar(n) is string erro) return await MostrarDeNovo(id, erro);
        var anexados = await GuardarAnexos(n);
        mudar(n);
        await db.SaveChangesAsync();
        var mudou = await Mudancas(antes, n);
        auditoria.Registrar("Nre", n.Id, acao, Resumo(n) + (n.Status == StatusNre.Recusada ? $" · motivo: {n.MotivoRecusa}" : "")
            + (mudou == "" ? "" : "\n" + mudou));
        RegistrarAnexos(n, anexados);
        await db.SaveChangesAsync();
        Ok($"{n.Numero}: {msg}");
        return RedirectToPage("Editar", Volta(id));
    }

    public Task<IActionResult> OnPostAprovarAsync(int id)
    {
        if (!Aprova) return Task.FromResult<IActionResult>(RedirectToPage("/Conta/Negado"));
        return Mudar(id, n => n.Status == StatusNre.Aberta, "só NRE aguardando aprovação pode ser aprovada.", n =>
        {
            n.Status = StatusNre.Aprovada;
            n.AnalisadaPorId = UsuarioId;
            n.AnalisadaEm = DateTime.Now;
            n.MotivoRecusa = null;
        }, "Aprovada", "aprovada. Próximo passo: comprar e, quando chegar, registrar as DIs ou NFs no Detalhado.");
    }

    public async Task<IActionResult> OnPostRecusarAsync(int id)
    {
        if (!Aprova) return RedirectToPage("/Conta/Negado");
        if (string.IsNullOrWhiteSpace(Motivo))
        {
            Erro("Para recusar, escreva o motivo da recusa.");
            return RedirectToPage("Editar", Volta(id));
        }
        return await Mudar(id, n => n.Status == StatusNre.Aberta, "só NRE aguardando aprovação pode ser recusada.", n =>
        {
            n.Status = StatusNre.Recusada;
            n.AnalisadaPorId = UsuarioId;
            n.AnalisadaEm = DateTime.Now;
            n.MotivoRecusa = Motivo.Trim();
        }, "Recusada", "recusada. Quem abriu pode corrigir e salvar para enviar de novo.");
    }

    // Sem etapa de aprovação na tela (pedido do usuário): comprar, cobrar e concluir já valem com a NRE aguardando aprovação.
    public Task<IActionResult> OnPostComprarAsync(int id) =>
        Mudar(id, n => n.Status is StatusNre.Aberta or StatusNre.Aprovada, "só NRE ainda não comprada pode ser marcada como comprada.",
            n => n.Status = StatusNre.EmAndamento, "Comprada (aguardando chegada)", "marcada como comprada: aguardando chegada.");

    public Task<IActionResult> OnPostCobrarAsync(int id) =>
        Mudar(id, n => n.Status is StatusNre.Aberta or StatusNre.Aprovada or StatusNre.EmAndamento, "só NRE ainda não cobrada pode ser marcada como cobrada.", n =>
        {
            n.Status = StatusNre.Cobrada;
            n.CobradaEm = DateTime.Now;
        }, "Cobrada do cliente", "marcada como cobrada do cliente.");

    public Task<IActionResult> OnPostConcluirAsync(int id) =>
        Mudar(id, n => n.Status is StatusNre.Aberta or StatusNre.Aprovada or StatusNre.EmAndamento or StatusNre.Cobrada, "NRE recusada ou já concluída não pode ser concluída.", n =>
        {
            n.Status = StatusNre.Concluida;
            n.ConcluidaEm = DateTime.Now;
        }, "Concluída", "concluída.");

    // Botão Extração (24/10/2026, no lugar de "Baixar resumo" e "Baixar detalhado"): um Excel com as abas Resumo (planilha
    // azul) e Detalhado (planilha amarela, com os arquivos da NRE na coluna Invoice & DI) — Servicos/ResumoNre.cs.
    public async Task<IActionResult> OnGetExtracaoAsync(int id)
    {
        var n = await Buscar(id);
        if (n == null) return NotFound();
        // Anexos antigos que não são PDF viram PDF de vez (só a primeira Extração demora mais; Servicos/Anexos.cs).
        var convertidos = new List<string>();
        foreach (var a in n.Anexos)
        {
            var antes = a.Nome;
            if (await anexos.ConverterParaPdfAsync(a)) convertidos.Add($"{antes} → {a.Nome}");
        }
        if (convertidos.Count > 0)
        {
            auditoria.Registrar("Nre", n.Id, "Arquivo(s) convertido(s) em PDF", string.Join("\n", convertidos));
            await db.SaveChangesAsync();
        }
        var planilha = await ExcelArquivos.Embutir(ResumoNre.Planilha(n, Endereco),
            ResumoNre.LinhaEvidenciasDetalhado, ResumoNre.ColunaEvidenciasDetalhado, Evidencias(n), ResumoNre.AbaDetalhado);
        return File(planilha, Excel.Mime, $"{n.Numero}.xlsx");
    }

    // Links dos arquivos no Detalhado (coluna N "Invoice & DI"): "EnderecoSistema" do appsettings.json ou,
    // se vazio, o endereço de agora. Valem só se o Excel do computador não conseguir pôr os arquivos dentro da planilha.
    string Endereco => config["EnderecoSistema"] is { Length: > 0 } e ? e.TrimEnd('/') : $"{Request.Scheme}://{Request.Host}";

    // Os arquivos da aba Arquivos (todos os tipos) que vão dentro das planilhas: (onde estão guardados, nome original, tipo).
    List<(string, string, string)> Evidencias(Modelos.Nre n) =>
        ResumoNre.ArquivosDaPlanilha(n).Select(x => (anexos.Caminho(x.Anexo.Arquivo), x.Anexo.Nome, x.Grupo)).ToList();

    // Anexar arquivo(s) sem mexer em mais nada (vale em qualquer status, inclusive concluída).
    public async Task<IActionResult> OnPostAnexarAsync(int id)
    {
        var n = await Buscar(id);
        if (n == null) return RedirectToPage("Index");
        var anexados = await GuardarAnexos(n);
        if (anexados.Count > 0)
        {
            await db.SaveChangesAsync();
            RegistrarAnexos(n, anexados);
            await db.SaveChangesAsync();
            Ok($"{n.Numero}: {anexados.Count} arquivo(s) anexado(s).");
        }
        else if (TempData.Peek("erro") == null) Erro("Escolha o arquivo para anexar.");
        return RedirectToPage("Editar", new { id, aba = "#aba-arquivos" });
    }

    // Remover um arquivo (pede confirmação na tela; fica registrado no histórico).
    public async Task<IActionResult> OnPostRemoverAnexoAsync(int id, int anexo)
    {
        var a = await db.NreAnexos.FirstOrDefaultAsync(x => x.Id == anexo && x.NreId == id);
        if (a != null)
        {
            anexos.Apagar(a.Arquivo);
            db.NreAnexos.Remove(a);
            auditoria.Registrar("Nre", id, "Arquivo removido", a.Nome);
            await db.SaveChangesAsync();
            Ok($"Arquivo {a.Nome} removido.");
        }
        return RedirectToPage("Editar", new { id, aba = "#aba-arquivos" });
    }

    // Abrir / baixar um arquivo: PDF e imagem abrem no navegador, o resto baixa com o nome original.
    public async Task<IActionResult> OnGetAnexoAsync(int id, int anexo)
    {
        var a = await db.NreAnexos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == anexo && x.NreId == id);
        var caminho = a == null ? null : anexos.Caminho(a.Arquivo);
        if (a == null || !System.IO.File.Exists(caminho)) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var (tipo, abrir) = Anexos.TipoDoArquivo(a.Nome);
        if (!abrir) return PhysicalFile(caminho!, tipo, a.Nome);
        Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(a.Nome)}";
        return PhysicalFile(caminho!, tipo);
    }

    public async Task<IActionResult> OnPostRemoverAsync(int id)
    {
        if (!Aprova) return RedirectToPage("/Conta/Negado");
        var n = await Buscar(id);
        if (n != null)
        {
            auditoria.Registrar("Nre", n.Id, "Removida", $"{n.Numero} · {Resumo(n)}");
            foreach (var a in n.Anexos) anexos.Apagar(a.Arquivo);
            db.Nres.Remove(n);
            await db.SaveChangesAsync();
            Ok($"{n.Numero} removida.");
        }
        return RedirectToPage("Index");
    }
}
