// ============================================================================
// TELA: NRE — lista (lógica)  ·  endereço /Nre  ·  visual em Index.cshtml  ·  permissão "nre" (aprovar/remover: "nre_aprovacao")
// NRE = gasto não recorrente de uma máquina (peça, serviço, ferramental...) cobrado do cliente (a célula da máquina).
// Fluxo: Aguardando aprovação → Aprovada → Aguardando chegada → Cobrada do cliente → Concluída (ou Recusada).
// A NRE em si: Editar.cshtml.
// ONDE MEXER:
//   - Filtros e ordem da lista ............ Filtrar (aguardando aprovação primeiro, depois as mais novas)
//   - Colunas da lista .................... Index.cshtml (Modelo · NRE · Título · NF · Invoice · Valor · DI · Status; NF,
//                                           invoice e DI vêm do Detalhado; Valor = Total a cobrar (Final Amount + taxas); Status: aprovada aparece
//                                           como Concluída — Textos.NaLista)
//   - Sem botão Extração desde 24/10/2026 (pedido do usuário): o Excel sai de cada NRE (Editar: botão Extração)
//   - Remover selecionados ................ OnPostRemoverAsync (só quem aprova NRE)
//   - Importar Excel (planilhas Samsung) .. OnPostImportarAsync (cria ou atualiza, nunca duplica) · leitura das
//                                           planilhas amarela e azul: Servicos/ImportarNre.cs
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Nre;

// Planilha da Samsung com e-mails colados dentro pode passar dos 30 MB do padrão.
[RequestSizeLimit(100_000_000)]
[RequestFormLimits(MultipartBodyLengthLimit = 100_000_000)]
public class IndexModel(AppDb db, Auditoria auditoria, Anexos anexos, Regras regras) : MsPage, IFiltroArea
{
    protected override string Modulo => "nre";
    const int PorPagina = 50;

    [BindProperty(SupportsGet = true)] public StatusNre? Status { get; set; }
    [BindProperty(SupportsGet = true)] public TipoNre? Tipo { get; set; }
    [BindProperty(SupportsGet = true)] public int? CelulaId { get; set; }
    [BindProperty(SupportsGet = true)] public int? Ano { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

    public List<Celula> Celulas { get; set; } = new();
    public List<int> Anos { get; set; } = new();
    public List<Modelos.Nre> Lista { get; set; } = new();
    public int Total { get; set; }
    public int Paginas => Math.Max(1, (int)Math.Ceiling(Total / (double)PorPagina));
    public bool Aprova => Pode("nre_aprovacao");

    IQueryable<Modelos.Nre> Filtrar()
    {
        var q = db.Nres.Include(n => n.Equipamento).Include(n => n.Celula)
            .Include(n => n.OrdemServico).Include(n => n.Solicitante).Include(n => n.AnalisadaPor).Include(n => n.Itens)
            .Include(n => n.Remessas).ThenInclude(r => r.Itens).Include(n => n.Anexos).AsSplitQuery().AsQueryable();
        // "Concluída" pega também as aprovadas (a coluna Status mostra Concluída para elas: Textos.NaLista).
        if (Status == StatusNre.Concluida)
            q = q.Where(n => n.Status == StatusNre.Concluida || n.Status == StatusNre.Aprovada || n.Status == StatusNre.EmAndamento || n.Status == StatusNre.Cobrada);
        else if (Status != null) q = q.Where(n => n.Status == Status);
        if (Tipo != null) q = q.Where(n => n.Tipo == Tipo);
        if (CelulaId != null) q = q.Where(n => n.CelulaId == CelulaId);
        if (Ano != null) q = q.Where(n => n.CriadaEm.Year == Ano);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var t = Q.Trim().ToUpper();
            q = q.Where(n => n.Numero.Contains(t) || (n.Maquina != null && n.Maquina.Contains(t))
                || (n.Equipamento != null && n.Equipamento.Codigo.Contains(t))
                || n.Descricao.Contains(t) || (n.OrdemServico != null && n.OrdemServico.Numero.Contains(t))
                || (n.Titulo != null && n.Titulo.Contains(t)) || (n.Modelo != null && n.Modelo.Contains(t))
                || (n.CodigoNre != null && n.CodigoNre.Contains(t)) || (n.PedidoCompra != null && n.PedidoCompra.Contains(t))
                || n.Remessas.Any(r => (r.NotaFiscal != null && r.NotaFiscal.Contains(t)) || (r.Invoice != null && r.Invoice.Contains(t))
                    || (r.Di != null && r.Di.Contains(t))));
        }
        return q.OrderBy(n => n.Status == StatusNre.Aberta ? 0 : 1).ThenByDescending(n => n.CriadaEm);
    }

    public async Task OnGetAsync()
    {
        Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
        Anos = await db.Nres.Select(n => n.CriadaEm.Year).Distinct().OrderByDescending(a => a).ToListAsync();
        var todas = await Filtrar().ToListAsync();
        Total = todas.Count;
        Pagina = Math.Clamp(Pagina, 1, Paginas);
        Lista = todas.Skip((Pagina - 1) * PorPagina).Take(PorPagina).ToList();
    }

    // ---------- Importar Excel ----------
    // Planilhas de NRE da Samsung: amarela (detalhe: itens, invoice, PTAX, DI, frete e seguro) e/ou azul (Summary:
    // Project, Item/Project, Approved Value). Leitura em Servicos/ImportarNre.cs. Nunca duplica: a NRE com a mesma
    // NRE Order ou o mesmo Item/Project é atualizada (pelas DIs não: ver ImportarNre.Achar); senão é criada, aguardando
    // aprovação, na área escolhida nos cartões. Azul + amarela no mesmo arquivo = uma NRE só. NRE concluída não muda.
    // O que a planilha não traz (em branco) não apaga nada. Equipamento: da célula MAQUINA da amarela (o nome ao lado);
    // sem ela, o aviso lista as NRE em que falta.
    public async Task<IActionResult> OnPostImportarAsync(IFormFile? arquivo)
    {
        var volta = new { Status, Tipo, CelulaId, Ano, Q };
        if (arquivo == null || arquivo.Length == 0) { Erro("Escolha a planilha de NRE (.xlsx)."); return RedirectToPage(volta); }
        ImportarNre.Leitura leitura;
        try
        {
            using var s = arquivo.OpenReadStream();
            leitura = ImportarNre.Ler(s);
        }
        catch (Exception)
        {
            Erro("Não foi possível ler o arquivo. Use a planilha em .xlsx (no Excel na web: Arquivo › Salvar como › Baixar uma cópia).");
            return RedirectToPage(volta);
        }
        if (leitura.Nres.Count == 0)
        {
            Erro("Não achei NRE nesta planilha. O sistema procura o cabeçalho da planilha amarela (Material · Qtde · Unit Price $ · PTAX · DI No.) ou da azul (Project · Item/Project · Approved Value (USD) · Ptax).");
            return RedirectToPage(volta);
        }

        var area = AreaId;   // procura e cria só na área escolhida (NRE sem área também vale)
        var existentes = await db.Nres.Include(n => n.Itens).Include(n => n.Remessas).ThenInclude(r => r.Itens)
            .Where(n => area == null || n.CelulaId == area || n.CelulaId == null).AsSplitQuery().ToListAsync();
        var celulas = await db.Celulas.ToDictionaryAsync(c => c.Id, c => c.Nome);
        // Nomes das máquinas da área: o Equipamento da planilha que não estiver aqui entra assim mesmo, mas avisa.
        var maquinas = (await db.Equipamentos.Where(e => area == null || e.CelulaId == area).Select(e => e.Nome).Distinct().ToListAsync())
            .Select(m => m.Trim().ToUpper()).ToHashSet();
        var usadas = new HashSet<Modelos.Nre>();
        List<string> criadas = new(), atualizadas = new(), falta = new(), confira = new();
        foreach (var l in leitura.Nres)
        {
            var nome = l.Titulo == "" ? l.Origem : l.Titulo.ToUpper();
            var n = ImportarNre.Achar(l, existentes);
            if (n != null && !usadas.Add(n)) { confira.Add($"{nome}: aparece mais de uma vez no arquivo; ficou a primeira."); continue; }
            if (n?.Status == StatusNre.Concluida) { confira.Add($"{n.Numero} ({nome}) já está concluída: não mudei nada."); continue; }
            var nova = n == null;
            if (n == null)
            {
                var agora = DateTime.Now;
                n = new Modelos.Nre
                {
                    Numero = await regras.ProximoNumeroNreAsync(agora), CriadaEm = agora, Status = StatusNre.Aberta,
                    SolicitanteId = UsuarioId == 0 ? null : UsuarioId, CelulaId = area, Descricao = "",
                };
                db.Nres.Add(n);
                existentes.Add(n);
                usadas.Add(n);
            }
            var antes = Retrato(n);
            AplicarPlanilha(l, n);
            await db.SaveChangesAsync();   // grava uma por vez: o número da próxima NRE nova depende desta

            var mudou = nova ? "" : Mudancas(antes, Retrato(n));
            if (nova)
            {
                auditoria.Registrar("Nre", n.Id, "Criada pelo Excel",
                    $"{n.Remessas.Count} DI/NF · {n.Itens.Count} item(ns) · total a cobrar {n.TotalCobrar().Reais()}"
                    + (n.Maquina != null ? $" · equipamento {n.Maquina}" : "") + $" · arquivo {arquivo.FileName}");
                criadas.Add($"{n.Numero} · {n.Titulo} · {n.Remessas.Count} DI/NF · {n.Itens.Count} item(ns) · total a cobrar {n.TotalCobrar().Reais()}");
            }
            else
            {
                if (mudou != "") auditoria.Registrar("Nre", n.Id, "Alterada pelo Excel", mudou);
                atualizadas.Add($"{n.Numero} · {n.Titulo}" + (mudou == "" ? " (já estava igual à planilha)" : ""));
            }

            // O que a planilha não traz e a NRE precisa.
            var faltam = new List<string>();
            if (n.CelulaId == null) faltam.Add("Área");
            if (n.PtaxPendente) faltam.Add("PTAX de " + string.Join(", ", n.Remessas.Where(r => r.PtaxPendente).Select(r => r.Rotulo())));
            if (faltam.Count > 0) falta.Add($"{n.Numero}: {string.Join(" · ", faltam)}");

            // Conferência com os números da planilha.
            confira.AddRange(l.Avisos.Select(a => $"{n.Numero}: {a}"));
            if (l.Detalhado != null && l.CustoPlanilha is decimal custo && Math.Abs(custo - n.Total()) > 1)
                confira.Add($"{n.Numero}: R$ Total Price da planilha {custo.Reais()} e no sistema {n.Total().Reais()} — confira os itens.");
            if (l.TotalPlanilha is decimal tp && Math.Abs(tp - n.TotalCobrar()) > 1)
                confira.Add($"{n.Numero}: Final Amount + Taxes da planilha azul {tp.Reais()} e total a cobrar no sistema {n.TotalCobrar().Reais()}.");
            if (l.Detalhado == null && l.PtaxesAzul.Count > 1)
                confira.Add($"{n.Numero}: a planilha azul tem uma Ptax por linha ({string.Join(" e ", l.PtaxesAzul.Select(p => p.Ptax()))}); a Ptax da aprovação não foi mudada. Importe também a amarela para trazer as DIs.");
            if (l.TemCustoAbsorvido)
                confira.Add($"{n.Numero}: a planilha tem Custo absorvido pela empresa — preencha no Fechamento (aba Detalhado).");
            if (l.Maquina != null && !maquinas.Contains(l.Maquina.Trim().ToUpper()))
                confira.Add($"{n.Numero}: o Equipamento {l.Maquina.Trim().ToUpper()} (linha MAQUINA da planilha) não está na lista de equipamentos da área — entrou assim mesmo, confira o nome.");
        }
        await db.SaveChangesAsync();

        var grupos = new List<GrupoAviso>();
        if (criadas.Count > 0) grupos.Add(new("Criadas (aguardando aprovação" + (area is int a && celulas.TryGetValue(a, out var nomeArea) ? $", área {nomeArea}" : "") + "):", criadas));
        if (atualizadas.Count > 0) grupos.Add(new("Atualizadas:", atualizadas));
        if (falta.Count > 0) grupos.Add(new("Falta preencher (abra a NRE e salve):", falta));
        if (confira.Count > 0) grupos.Add(new("Confira:", confira));
        var dica = leitura.ArquivosDentro > 0
            ? $"O Excel tem {leitura.ArquivosDentro} arquivo(s) colado(s) dentro (ex.: os e-mails da coluna email): esses não vêm. Anexe na aba Arquivos da NRE."
            : null;
        Avisar(new($"Importar Excel: {criadas.Count} NRE criada(s) e {atualizadas.Count} atualizada(s).", grupos, dica));
        return RedirectToPage(volta);
    }

    // Copia para a NRE o que veio da planilha. Campo que a planilha não traz não muda.
    void AplicarPlanilha(ImportarNre.Lida l, Modelos.Nre n)
    {
        if (l.Titulo != "" && !(l.TituloDaAba && n.Titulo != null)) n.Titulo = l.Titulo.Trim().ToUpper();
        if (l.Modelo != null && (!l.ModeloDoTitulo || n.Modelo == null)) n.Modelo = l.Modelo.Trim().ToUpper();
        if (l.CodigoNre != null) n.CodigoNre = l.CodigoNre.Trim().ToUpper();
        // Equipamento = "MAQUINA | ADESIVADORA" da amarela. Trocou o tipo: solta a máquina exata (como no Salvar da NRE).
        if (l.Maquina?.Trim().ToUpper() is { Length: > 0 } maquina && n.Maquina != maquina)
        {
            n.Maquina = maquina;
            n.EquipamentoId = null;
        }
        if (l.AprovadaEm != null) n.AprovadaClienteEm = l.AprovadaEm;
        if (l.Aprovador != null) n.Aprovador = l.Aprovador.Trim().ToUpper();
        if (l.AprovadoUsd != null || l.Ptax != null)
        {
            n.AprovadoUsd = l.AprovadoUsd ?? n.AprovadoUsd;
            n.PtaxAprovacao = l.Ptax ?? n.PtaxAprovacao;
            n.ValorAprovado = n.AprovadoUsd * n.PtaxAprovacao is decimal p ? Modelos.Nre.Centavos(p) : null;   // como na tela: aprovado × Ptax
        }
        // Taxa da planilha (A076: 10,67015204%, "corrigida para os modelos de smartphone"); igual à padrão = vazio.
        if (l.Taxa is decimal taxa) n.TaxaPercentual = Math.Abs(taxa - Modelos.Nre.TaxaPadrao) < 0.0001m ? null : taxa;
        if (l.Detalhado != null)
        {
            // Detalhado: troca os blocos e itens pelos da planilha (o item fica na NRE e no bloco), como o Salvar da NRE.
            db.NreItens.RemoveRange(n.Itens);
            db.NreRemessas.RemoveRange(n.Remessas);
            n.Itens = new();
            n.Remessas = new();
            foreach (var b in l.Detalhado)
            {
                n.Remessas.Add(b);
                n.Itens.AddRange(b.Itens);
            }
        }
    }

    // Para o Histórico: só o que a planilha mudou (mesmos nomes da tela da NRE; o Detalhado DI por DI e item por item).
    record Foto(string? Titulo, string? Modelo, string? CodigoNre, string? Maquina, DateTime? AprovadaEm, string? Aprovador, string? AprovadoUsd,
        string? Ptax, string? Previsto, List<HistoricoNre.Bloco> Detalhado, string Taxa, string Total);

    static Foto Retrato(Modelos.Nre n) => new(n.Titulo, n.Modelo, n.CodigoNre, n.Maquina, n.AprovadaClienteEm?.Date, n.Aprovador,
        n.AprovadoUsd?.Dolares(), n.PtaxAprovacao?.Ptax(), n.ValorAprovado?.Reais(), HistoricoNre.Fotografar(n),
        $"{n.Taxa:0.########}%", n.TotalCobrar().Reais());

    static string Mudancas(Foto a, Foto d) => string.Join("\n", new[]
    {
        Auditoria.Mudancas(
            ("Item / Projeto", a.Titulo, d.Titulo),
            ("Modelo", a.Modelo, d.Modelo),
            ("NRE Order", a.CodigoNre, d.CodigoNre),
            ("Equipamento", a.Maquina, d.Maquina),
            ("Data da aprovação", a.AprovadaEm, d.AprovadaEm),
            ("Aprovador", a.Aprovador, d.Aprovador),
            ("Valor aprovado (US$)", a.AprovadoUsd, d.AprovadoUsd),
            ("Ptax da aprovação", a.Ptax, d.Ptax),
            ("Final Amount (R$)", a.Previsto, d.Previsto)),
        string.Join("\n", HistoricoNre.Diferencas(a.Detalhado, d.Detalhado)),
        Auditoria.Mudancas(("Taxa (Pis/Cofins, CSLL, R&D)", a.Taxa, d.Taxa), ("Total a cobrar", a.Total, d.Total)),
    }.Where(p => p != ""));

    // Remover selecionados — só quem aprova NRE (padrão: Coordenador e Administrador).
    public async Task<IActionResult> OnPostRemoverAsync(int[] ids)
    {
        if (!Aprova) return RedirectToPage("/Conta/Negado");
        var lista = await db.Nres.Include(n => n.Itens).Include(n => n.Remessas).ThenInclude(r => r.Itens).Include(n => n.Anexos)
            .Where(n => ids.Contains(n.Id)).ToListAsync();
        foreach (var n in lista)
        {
            auditoria.Registrar("Nre", n.Id, "Removida", $"{n.Numero} · {n.Total().Reais()}");
            foreach (var a in n.Anexos) anexos.Apagar(a.Arquivo);   // os arquivos da NRE saem junto
        }
        db.Nres.RemoveRange(lista);
        await db.SaveChangesAsync();
        Ok($"{lista.Count} NRE removida(s).");
        return RedirectToPage(new { Status, Tipo, CelulaId, Ano, Q });
    }
}
