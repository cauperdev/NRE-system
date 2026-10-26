// ============================================================================
// REGRAS DO SISTEMA (as regras dos slides) — como a OS nasce, vence, é concluída e volta.
// ONDE MEXER:
//   Regra 01 · Plano gera a OS ............... CriarOsDoPlanoAsync, OrdemTrabalhoAsync, NumeroDaOsDoPlanoAsync,
//                                              ProximoNumeroOsAsync (formato OS-ano-número)
//   Regra 02 · Checklist vem do plano ........ CopiarTarefas, SincronizarTarefasAsync, SincronizarNrAsync (tarefas de NR)
//              Técnico e coordenador do plano . ResponsaveisAsync (a OS do plano só mostra os do Cronograma)
//   Regra 03 · Prazo pela periodicidade ...... Prazo, DefinirDataManutencao, ProximaManutencao
//              1ª manutenção do plano novo .... PrimeiraManutencao (hoje + período, mínimo 7 dias) · nunca no domingo: SemDomingo
//   Regra 04 · Concluiu, reiniciou ........... Concluir (o ciclo vai para a Finalização e a OS pula para a próxima data)
//              Peças gravadas na Finalização .. LinhaPeca, LerLinhaPeca ("2 x [código] peça")
//   Regra 05 · Coordenador revisa ............ Aprovar, RetornarAsync (Retornar devolve a OS para a data do ciclo;
//                                              qualquer conclusão pode voltar, não só a mais recente)
//   Lembrete do sino ......................... AtualizarLembrete
//   Código do equipamento (prédio + sigla + nº) ProximoCodigoEquipamentoAsync, PrefixoDoPredio, SiglaDoNome
//                                              (prefixo de cada prédio: "PrefixosPredio" no appsettings.json)
//   Remover OS (com fotos e histórico) ....... RemoverOrdensAsync, RemoverImagens
//   Número da NRE (NRE-ano-nº) ............... ProximoNumeroNreAsync (telas da NRE: Pages/Nre)
// Regra 06 (fotos) está em Servicos/Fotos.cs e Regra 07 (acesso) em Servicos/Acesso.cs.
// ============================================================================
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;

namespace MS.Servicos;

// As 7 regras do sistema MS, num só lugar.
public class Regras(AppDb db, Auditoria auditoria, Fotos fotos, Estoque estoque, IConfiguration config)
{
    public static DateTime Prazo(DateTime abertura, Periodicidade p) => SemDomingo(abertura.AddDays((int)p));

    // Manutenção não cai no domingo: domingo passa para a segunda-feira (a hora é mantida).
    public static DateTime SemDomingo(DateTime d) => d.DayOfWeek == DayOfWeek.Sunday ? d.AddDays(1) : d;

    // Plano novo: a 1ª manutenção começa depois de um prazo para se organizar — hoje + periodicidade,
    // com no mínimo DiasParaComecar (a diária também começa em 7 dias). Ex.: semanal criada em 30/09 → 07/10; mensal → 30/10.
    public const int DiasParaComecar = 7;
    public static DateTime PrimeiraManutencao(Periodicidade p, DateTime hoje) =>
        SemDomingo(hoje.Date.AddDays(Math.Max((int)p, DiasParaComecar)));

    // Data da manutenção escolhida pelo usuário = prazo do ciclo. A abertura fica "prazo − período"
    // (mantém a Regra 03) e a próxima manutenção é "data + período". A hora do prazo atual é mantida.
    // "proxima" (opcional) é a próxima manutenção editada pelo usuário; se for igual ao cálculo, não é guardada.
    // Domingo escolhido (na data ou na próxima) passa para a segunda-feira.
    public static void DefinirDataManutencao(OrdemServico os, DateTime data, DateTime? proxima = null)
    {
        var hora = os.DataConclusao == default ? DateTime.Now.TimeOfDay : os.DataConclusao.TimeOfDay;
        os.DataConclusao = SemDomingo(data.Date) + hora;
        os.DataAbertura = os.DataConclusao.AddDays(-(int)os.Tipo);
        var automatica = SemDomingo(os.DataConclusao.Date.AddDays((int)os.Tipo));
        var escolhida = proxima?.Date is DateTime p ? SemDomingo(p) : (DateTime?)null;
        os.DataProxima = escolhida is DateTime e && e != automatica && e > os.DataConclusao.Date ? e : null;
        AtualizarLembrete(os);
    }

    // Próxima manutenção: a escolhida pelo usuário ou, se não houver, data da manutenção + período (sem domingo).
    public static DateTime ProximaManutencao(OrdemServico os) => os.DataProxima ?? SemDomingo(os.DataConclusao.AddDays((int)os.Tipo));

    // Lembrete = prazo − N dias. Chamado sempre que o prazo ou os dias mudam.
    public static void AtualizarLembrete(OrdemServico os) =>
        os.DataLembrete = os.LembreteDias is int dias ? os.DataConclusao.Date.AddDays(-dias) : null;

    // Regra 01 — Ordem de trabalho: um número (OS-ano-nº) por equipamento + nome do plano, gerado no Cronograma e
    // compartilhado pelas periodicidades (a semanal e a mensal da mesma adesivadora têm o mesmo número).
    public async Task<string> OrdemTrabalhoAsync(PlanoManutencao plano)
    {
        var nome = plano.Nome.Trim().ToUpper();
        var doGrupo = db.Planos.Local
            .Where(p => p != plano && p.EquipamentoId == plano.EquipamentoId && p.Nome.ToUpper() == nome && p.OrdemTrabalho.StartsWith("OS-"))
            .Select(p => p.OrdemTrabalho).FirstOrDefault()
            ?? await db.Planos
            .Where(p => p.Id != plano.Id && p.EquipamentoId == plano.EquipamentoId && p.Nome.ToUpper() == nome && p.OrdemTrabalho.StartsWith("OS-"))
            .Select(p => p.OrdemTrabalho).FirstOrDefaultAsync();
        if (doGrupo != null) return doGrupo;
        // Sem outro plano no grupo: mantém o número atual se nenhum outro plano o usa; senão, gera um novo.
        if (plano.OrdemTrabalho.StartsWith("OS-") &&
            !await db.Planos.AnyAsync(p => p.Id != plano.Id && p.OrdemTrabalho == plano.OrdemTrabalho))
            return plano.OrdemTrabalho;
        return await ProximoNumeroOsAsync(DateTime.Now);
    }

    // Número da OS do plano: a Ordem de trabalho dele. Só ganha outro se o próprio plano já tiver uma OS com esse
    // número (ex.: OS extra lançada à mão para o mesmo plano).
    public async Task<string> NumeroDaOsDoPlanoAsync(PlanoManutencao plano, DateTime data)
    {
        var numero = plano.OrdemTrabalho;
        var livre = numero.StartsWith("OS-") && !db.Ordens.Local.Any(o => o.PlanoId == plano.Id && o.Numero == numero)
            && !await db.Ordens.AnyAsync(o => o.PlanoId == plano.Id && o.Numero == numero);
        return livre ? numero : await ProximoNumeroOsAsync(data);
    }

    // Código do equipamento: prefixo do prédio + sigla do tipo + número sequencial de 5 dígitos, único entre todos
    // (ex.: PRÉDIO 1 + ADESIVADORA → PRDADS00113).
    // A sigla é a que os equipamentos com o mesmo nome já usam; para um nome novo, sai do próprio nome.
    public async Task<string> ProximoCodigoEquipamentoAsync(string nome, string predio) =>
        ProximoCodigoEquipamento(nome, predio,
            (await db.Equipamentos.Select(e => new { e.Codigo, e.Nome, e.Predio }).ToListAsync()).Select(e => (e.Codigo, e.Nome, e.Predio)));

    // Mesma regra com a lista em mãos — a importação do Excel passa também os equipamentos que acabou de criar.
    public string ProximoCodigoEquipamento(string nome, string predio, IEnumerable<(string Codigo, string Nome, string Predio)> cadastrados)
    {
        nome = nome.Trim().ToUpper();
        var numero = cadastrados.Select(e => Regex.Match(e.Codigo, @"(\d{5})$")).Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max() + 1;
        // A sigla de cada código é o que sobra entre o prefixo do prédio dele e o número (PRDADS00101 → ADS).
        var sigla = cadastrados.Where(e => e.Nome.ToUpper() == nome)
            .Select(e => Regex.Match(e.Codigo, $@"^{Regex.Escape(PrefixoDoPredio(e.Predio))}([A-Z]+)\d{{5}}$")).Where(m => m.Success)
            .GroupBy(m => m.Groups[1].Value).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault()
            ?? SiglaDoNome(nome);
        return $"{PrefixoDoPredio(predio)}{sigla}{numero:00000}";
    }

    // Prefixo do código pelo prédio: o de "PrefixosPredio" no appsettings.json (PRÉDIO 1 → PRD) ou, se o prédio
    // não estiver lá, sai do próprio nome pela mesma regra da sigla (CENTRAL → CNT). Acento não conta (PREDIO = PRÉDIO).
    public string PrefixoDoPredio(string predio)
    {
        predio = Excel.SemAcento(predio.Trim().ToUpper());
        if (predio == "") return "";
        var doConfig = config.GetSection("PrefixosPredio").GetChildren()
            .FirstOrDefault(p => Excel.SemAcento(p.Key.Trim().ToUpper()) == predio)?.Value?.Trim().ToUpper();
        return string.IsNullOrEmpty(doConfig) ? SiglaDoNome(predio) : doConfig;
    }

    // Nome ainda sem equipamento: iniciais das palavras (MIXADOR DE PASTA DE SOLDA → MPS) ou, se for
    // uma palavra só, a primeira letra + as consoantes seguintes (BUFFER → BFF, LOADER → LDR).
    static string SiglaDoNome(string nome)
    {
        var palavras = Regex.Replace(Excel.SemAcento(nome), "[^A-Z ]", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p is not ("DE" or "DA" or "DO" or "DAS" or "DOS" or "E")).ToList();
        if (palavras.Count == 0) return "EQP";
        if (palavras.Count > 1) return string.Concat(palavras.Take(4).Select(p => p[0]));
        var palavra = palavras[0];
        var sigla = palavra[0] + string.Concat(palavra.Skip(1).Where(c => !"AEIOU".Contains(c)));
        if (sigla.Length < 3) sigla = palavra;
        return sigla[..Math.Min(3, sigla.Length)];
    }

    public async Task<string> ProximoNumeroOsAsync(DateTime data)
    {
        var prefixo = $"OS-{data.Year}-";
        // Maior número salvo (o mais comprido e, no mesmo tamanho, o maior) + os ainda não salvos.
        var numeros = await db.Ordens.Where(o => o.Numero.StartsWith(prefixo))
            .OrderByDescending(o => o.Numero.Length).ThenByDescending(o => o.Numero)
            .Select(o => o.Numero).Take(1).ToListAsync();
        numeros.AddRange(db.Ordens.Local.Where(o => o.Numero.StartsWith(prefixo)).Select(o => o.Numero));
        // Números já reservados como Ordem de trabalho de plano (mesmo que a OS tenha sido apagada) não se repetem.
        numeros.AddRange(await db.Planos.Where(p => p.OrdemTrabalho.StartsWith(prefixo))
            .OrderByDescending(p => p.OrdemTrabalho.Length).ThenByDescending(p => p.OrdemTrabalho)
            .Select(p => p.OrdemTrabalho).Take(1).ToListAsync());
        numeros.AddRange(db.Planos.Local.Where(p => p.OrdemTrabalho.StartsWith(prefixo)).Select(p => p.OrdemTrabalho));
        var max = numeros.Select(n => int.TryParse(n[prefixo.Length..], out var x) ? x : 0).DefaultIfEmpty(0).Max();
        return $"{prefixo}{max + 1:0000}";
    }

    // Regra 01 + 02 — o plano gera a OS e a OS nasce com as tarefas do plano.
    // Sem "abertura" (importação do Excel): a 1ª manutenção segue PrimeiraManutencao (hoje + período, mín. 7 dias, sem domingo).
    public async Task<OrdemServico> CriarOsDoPlanoAsync(PlanoManutencao plano, DateTime? abertura = null)
    {
        var agora = DateTime.Now;
        var inicio = abertura ?? PrimeiraManutencao(plano.Periodicidade, agora) + agora.TimeOfDay - TimeSpan.FromDays((int)plano.Periodicidade);
        var os = new OrdemServico
        {
            Numero = await NumeroDaOsDoPlanoAsync(plano, abertura ?? agora), // o número vem do Cronograma (Ordem de trabalho)
            EquipamentoId = plano.EquipamentoId,
            PlanoId = plano.Id,
            Tipo = plano.Periodicidade,
            Prioridade = Prioridade.Media,
            Status = StatusOS.Aberta,
            DataAbertura = inicio,
            DataConclusao = Prazo(inicio, plano.Periodicidade),
            Descricao = plano.Descricao,
            LembreteDias = plano.LembreteDias,
            TecnicoId = plano.TecnicoId,
            CoordenadorId = plano.CoordenadorId,
            Tarefas = CopiarTarefas(plano),
        };
        AtualizarLembrete(os);
        db.Ordens.Add(os);
        if (!plano.OrdemTrabalho.StartsWith("OS-")) plano.OrdemTrabalho = os.Numero; // plano ainda sem número
        return os;
    }

    // Técnico e coordenador responsáveis são escolhidos no plano (Cronograma): as OS dele ainda não concluídas acompanham.
    public async Task ResponsaveisAsync(PlanoManutencao plano)
    {
        foreach (var os in await db.Ordens.Where(o => o.PlanoId == plano.Id && o.Status != StatusOS.Concluida).ToListAsync())
        {
            os.TecnicoId = plano.TecnicoId;
            os.CoordenadorId = plano.CoordenadorId;
        }
    }

    // Tarefas de NR (EHS) também vão para a OS, sempre no fim do checklist (Servicos/Ehs.cs).
    public static List<TarefaOS> CopiarTarefas(PlanoManutencao plano) =>
        plano.Tarefas.OrderBy(t => Ehs.EhNr(t.Descricao)).ThenBy(t => t.Ordem)
            .Select((t, i) => new TarefaOS { Descricao = t.Descricao, Ordem = i }).ToList();

    // Tarefas de NR: as OS abertas do plano acompanham o que o EHS cadastrou, mesmo as que já têm tarefa marcada.
    // Só as NR mudam: as tarefas comuns e as marcações ficam como estão (NR com o mesmo texto continua marcada).
    public async Task<int> SincronizarNrAsync(PlanoManutencao plano)
    {
        var ordens = await db.Ordens.Include(o => o.Tarefas)
            .Where(o => o.PlanoId == plano.Id && o.Status != StatusOS.Concluida).ToListAsync();
        return ordens.Count(os => AcertarNr(os, plano));
    }

    // Acerta as NR de uma OS pelas do plano; true se mudou alguma coisa.
    public bool AcertarNr(OrdemServico os, PlanoManutencao plano)
    {
        var doPlano = plano.Tarefas.OrderBy(t => t.Ordem).Select(t => t.Descricao).Where(Ehs.EhNr).ToList();
        var naOs = os.Tarefas.Where(t => Ehs.EhNr(t.Descricao)).OrderBy(t => t.Ordem).ToList();
        if (naOs.Select(t => t.Descricao).SequenceEqual(doPlano)) return false;
        var marcadas = naOs.Where(t => t.Concluida).Select(t => t.Descricao).ToHashSet();
        db.TarefasOS.RemoveRange(naOs);
        foreach (var t in naOs) os.Tarefas.Remove(t);
        var ordem = os.Tarefas.Select(t => t.Ordem).DefaultIfEmpty(-1).Max();
        foreach (var d in doPlano)
            os.Tarefas.Add(new TarefaOS { Descricao = d, Ordem = ++ordem, Concluida = marcadas.Contains(d) });
        return true;
    }

    // Regra 02 — mudou o checklist do plano: OS que ainda não começaram recebem o novo checklist.
    public async Task SincronizarTarefasAsync(PlanoManutencao plano)
    {
        var ordens = await db.Ordens.Include(o => o.Tarefas)
            .Where(o => o.PlanoId == plano.Id && o.Status == StatusOS.Aberta).ToListAsync();
        foreach (var os in ordens.Where(o => o.Tarefas.All(t => !t.Concluida)))
        {
            db.TarefasOS.RemoveRange(os.Tarefas);
            os.Tarefas = CopiarTarefas(plano);
            os.Tipo = plano.Periodicidade;
            os.DataConclusao = Prazo(os.DataAbertura, plano.Periodicidade);
            AtualizarLembrete(os);
        }
    }

    // Regra 04 — concluiu, reiniciou: o ciclo concluído vai para a Finalização e a MESMA OS (mesmo número)
    // continua na lista com o checklist zerado e a data do próximo ciclo (ex.: semanal de 26/09 → 03/10).
    public Finalizacao Concluir(OrdemServico os)
    {
        var agora = DateTime.Now;
        var fin = new Finalizacao
        {
            OrdemServicoId = os.Id,
            NumeroOS = os.Numero,
            EquipamentoId = os.EquipamentoId,
            Tipo = os.Tipo,
            Descricao = os.Descricao,
            TecnicoId = os.TecnicoId,
            CoordenadorId = os.CoordenadorId,
            DataAbertura = os.DataAbertura,
            DataConclusao = os.DataConclusao,
            ConcluidaEm = agora,
            Checklist = string.Join("\n", os.Tarefas.OrderBy(t => Ehs.EhNr(t.Descricao)).ThenBy(t => t.Ordem).Select(t => $"[{(t.Concluida ? "x" : " ")}] {t.Descricao}")),
            Pecas = string.Join("\n", os.Pecas.Select(LinhaPeca)),
            StatusRevisao = StatusRevisao.Aguardando,
        };
        // As fotos passam para a finalização (Regra 06 — registro com prova).
        foreach (var img in os.Imagens.ToList())
        {
            img.OrdemServicoId = null;
            fin.Imagens.Add(img);
        }
        os.Imagens.Clear();
        db.Finalizacoes.Add(fin);
        // Spare Parts: as peças lançadas ficam no histórico do código; não existe estoque (Servicos/Estoque.cs).
        estoque.RegistrarUso(os, fin);

        foreach (var t in os.Tarefas) t.Concluida = false;
        db.Pecas.RemoveRange(os.Pecas);
        os.Pecas.Clear();
        os.Status = StatusOS.Aberta;
        // Refeita e concluída de novo: o motivo do retorno sai da OS (continua no Histórico).
        os.MotivoRetorno = null;
        os.RetornadaEm = null;
        os.RetornadaPor = null;
        // Nova data = a "Próxima manutenção" que a OS mostrava (data da manutenção + período, ou a escolhida);
        // se essa data já passou, hoje + período.
        var proxima = ProximaManutencao(os);
        os.DataConclusao = proxima.Date >= agora.Date ? proxima.Date + os.DataConclusao.TimeOfDay : Prazo(agora, os.Tipo);
        os.DataAbertura = os.DataConclusao.AddDays(-(int)os.Tipo);
        os.DataProxima = null; // a partir daqui volta a ser calculada
        AtualizarLembrete(os); // o lembrete acompanha a nova data
        return fin;
    }

    // Número da NRE: NRE-ano-nº, sequência que recomeça a cada ano (NRE-2026-0001, NRE-2026-0002...).
    public async Task<string> ProximoNumeroNreAsync(DateTime data)
    {
        var prefixo = $"NRE-{data.Year}-";
        var numeros = await db.Nres.Where(n => n.Numero.StartsWith(prefixo)).Select(n => n.Numero).ToListAsync();
        var ultimo = numeros.Select(n => int.TryParse(n[prefixo.Length..], out var x) ? x : 0).DefaultIfEmpty(0).Max();
        return $"{prefixo}{ultimo + 1:0000}";
    }

    // Peças na Finalização ficam como texto, uma por linha: "2 x [COD-123] Correia" ([código] só se tiver;
    // conclusões antigas têm só "2 x Correia").
    public static string LinhaPeca(PecaOS p) =>
        $"{p.Quantidade:0.##} x {(string.IsNullOrEmpty(p.Codigo) ? "" : $"[{p.Codigo}] ")}{p.Descricao}".TrimEnd();

    public static (string Qtd, string Codigo, string Peca) LerLinhaPeca(string linha)
    {
        var m = Regex.Match(linha.Trim(), @"^(\S+) [xX] (?:\[([^\]]*)\] ?)?(.*)$");   // "X" desde que o texto é gravado em maiúscula
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value) : ("", "", linha.Trim());
    }

    // Regra 05 — coordenador aprova.
    public void Aprovar(Finalizacao fin, int usuarioId)
    {
        fin.StatusRevisao = StatusRevisao.Aprovado;
        fin.AprovadaEm = DateTime.Now;
        fin.AprovadaPorId = usuarioId;
        auditoria.Registrar("Finalizacao", fin.Id, "Aprovado", $"Conclusão da {fin.NumeroOS} aprovada");
    }

    // Regra 05 — coordenador devolve para refazer: a conclusão sai da Finalização e a OS (mesmo número) volta
    // para a data daquele ciclo, com o checklist a refazer e as fotos e peças de volta. O motivo (obrigatório nas
    // telas) fica na OS, com quem retornou e quando — aparece na coluna Retorno da lista de Ordens de serviço.
    // Qualquer conclusão pode voltar, não só a mais recente (a pedido do usuário em 05/10/2026). Para não repetir os
    // ciclos concluídos depois dela, a OS guarda a data em que estava (DataProxima) e, refeita, segue para essa data.
    // Duas conclusões da mesma OS retornadas: a OS fica na data mais antiga, com os dois motivos, e é refeita uma vez.
    public async Task RetornarAsync(Finalizacao fin, string motivo, string quem)
    {
        var os = await db.Ordens.Include(o => o.Tarefas).Include(o => o.Pecas).FirstOrDefaultAsync(o => o.Id == fin.OrdemServicoId);
        var fotos = await db.Imagens.Where(i => i.FinalizacaoId == fin.Id).ToListAsync();
        if (os != null)
        {
            // Já retornada e ainda não refeita: a data de depois é a que o primeiro retorno guardou.
            var jaRetornada = os.RetornadaEm != null;
            var depois = jaRetornada ? os.DataProxima : os.DataConclusao.Date;
            if (!jaRetornada || fin.DataConclusao < os.DataConclusao)
            {
                os.DataAbertura = fin.DataAbertura;
                os.DataConclusao = fin.DataConclusao;
            }
            os.DataProxima = depois is DateTime d && d.Date > os.DataConclusao.Date ? d.Date : null;
            foreach (var t in os.Tarefas) t.Concluida = false;
            os.Status = StatusOS.Aberta;
            AtualizarLembrete(os);
            os.MotivoRetorno = jaRetornada && !string.IsNullOrWhiteSpace(os.MotivoRetorno) ? $"{os.MotivoRetorno}\n{motivo}" : motivo;
            os.RetornadaEm = DateTime.Now;
            os.RetornadaPor = quem;
            // As fotos e as peças daquele ciclo voltam para a OS (peças gravadas como "2 x [COD] Correia").
            foreach (var img in fotos) { img.FinalizacaoId = null; img.OrdemServicoId = os.Id; }
            foreach (var linha in fin.Pecas.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var (q, codigo, peca) = LerLinhaPeca(linha);
                if (decimal.TryParse(q, System.Globalization.NumberStyles.Number, new System.Globalization.CultureInfo("pt-BR"), out var qtd))
                    os.Pecas.Add(new PecaOS { Codigo = codigo == "" ? null : codigo, Descricao = peca, Quantidade = qtd });
            }
            auditoria.Registrar("OrdemServico", os.Id, "Retornada",
                $"Conclusão de {fin.DataConclusao:dd/MM/yyyy} devolvida pelo coordenador: a OS voltou para {os.DataConclusao:dd/MM/yyyy}, para refazer" +
                (os.DataProxima is DateTime p ? $"; refeita, segue para {p:dd/MM/yyyy}" : "") + $"\nMotivo: {motivo}");
        }
        else RemoverImagens(fotos);
        // Spare Parts: o uso das peças dessa conclusão é desfeito no histórico (a OS concluída de novo registra outra vez).
        estoque.DesfazerUso(fin);
        db.Finalizacoes.Remove(fin);
        auditoria.Registrar("Finalizacao", fin.Id, "Retornado", $"Conclusão da {fin.NumeroOS} devolvida para refazer (saiu da Finalização)\nMotivo: {motivo}");
    }

    // Apaga OS com as finalizações e as fotos delas (remover OS, ou remover o plano que as gerou).
    public async Task<int> RemoverOrdensAsync(IQueryable<OrdemServico> consulta)
    {
        var ordens = await consulta.Include(o => o.Imagens).ToListAsync();
        var ids = ordens.Select(o => o.Id).ToList();
        var fotosFin = await db.Imagens
            .Where(i => i.FinalizacaoId != null && db.Finalizacoes.Any(f => f.Id == i.FinalizacaoId && ids.Contains(f.OrdemServicoId))).ToListAsync();
        foreach (var img in ordens.SelectMany(o => o.Imagens).Concat(fotosFin)) fotos.Apagar(img.Arquivo);
        foreach (var o in ordens) auditoria.Registrar("OrdemServico", o.Id, "Removida", o.Numero);
        db.Ordens.RemoveRange(ordens);
        return ordens.Count;
    }

    public void RemoverImagens(IEnumerable<Imagem> imagens)
    {
        foreach (var img in imagens) fotos.Apagar(img.Arquivo);
        db.Imagens.RemoveRange(imagens);
    }
}
