// ============================================================================
// SPARE PARTS — USO DAS PEÇAS NAS OS: registra onde cada peça foi usada (é o histórico do código: em qual OS e máquina,
// quanto, quem e quando). Cada registro é um movimento da peça.
// NÃO EXISTE CONTROLE DE ESTOQUE: as peças lançadas nas OS são só registro e histórico do código (o usuário confirmou
// em 05/10/2026; em 08/10/2026 a quantidade saiu da tela Spare Parts, que ficou só código, descrição e equipamento).
//   - Uso em OS ................................... RegistrarUso: quando o técnico conclui a OS (Regras.Concluir), cada
//                                                   peça lançada em "Peças utilizadas" com código fica registrada.
//                                                   Código que ainda não está no Spare Parts é cadastrado sozinho.
//                                                   Vale a área da máquina da OS: cada área tem as suas peças.
//   - Uso desfeito ................................ DesfazerUso: o coordenador retornou a conclusão (Regras.RetornarAsync);
//                                                   a OS concluída de novo registra outra vez.
//   - Movimentar (mudar a quantidade) ............. sem uso desde 08/10/2026 (a tela Spare Parts não tem mais quantidade)
// ============================================================================
using MS.BancoDeDados;
using MS.Modelos;

namespace MS.Servicos;

public class Estoque(AppDb db, IHttpContextAccessor http)
{
    string Quem => http.HttpContext?.User.Identity?.Name ?? "sistema";

    // Muda a quantidade da peça e grava o movimento (quantidade com sinal: + entra, − sai). Só a tela Spare Parts usa.
    public MovimentoPeca Movimentar(SparePart p, TipoMovimento tipo, decimal quantidade, string? obs = null)
    {
        p.Estoque += quantidade;
        return Registrar(p, tipo, quantidade, obs);
    }

    // Grava o movimento no histórico da peça, sem mexer na quantidade.
    MovimentoPeca Registrar(SparePart p, TipoMovimento tipo, decimal quantidade, string? obs = null,
        OrdemServico? os = null, Finalizacao? fin = null, int? equipamentoId = null)
    {
        var m = new MovimentoPeca
        {
            SparePart = p, Quando = DateTime.Now, Tipo = tipo, Quantidade = quantidade, Saldo = p.Estoque,
            OrdemServico = os, Finalizacao = fin, EquipamentoId = equipamentoId ?? os?.EquipamentoId,
            Usuario = Quem, Observacao = string.IsNullOrWhiteSpace(obs) ? null : obs.Trim(),
        };
        db.MovimentosPeca.Add(m);
        return m;
    }

    // Conclusão da OS: cada peça com código entra no histórico (ligada à finalização, para poder desfazer).
    public void RegistrarUso(OrdemServico os, Finalizacao fin)
    {
        var usadas = os.Pecas.Where(p => !string.IsNullOrWhiteSpace(p.Codigo) && p.Quantidade > 0).ToList();
        if (usadas.Count == 0) return;
        var codigos = usadas.Select(p => p.Codigo!.Trim().ToUpper()).Distinct().ToList();
        // A peça é da área da máquina da OS: o mesmo código em outra área é outra peça, com outro histórico.
        var area = db.Equipamentos.Where(e => e.Id == os.EquipamentoId).Select(e => e.CelulaId).FirstOrDefault();
        var cadastro = db.SpareParts.Where(s => s.CelulaId == area && codigos.Contains(s.Codigo)).ToList()
            .GroupBy(s => s.Codigo).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Ativa).First());
        foreach (var p in usadas)
        {
            var codigo = p.Codigo!.Trim().ToUpper();
            if (!cadastro.TryGetValue(codigo, out var peca))
            {
                // Peça nova lançada pelo técnico: entra no Spare Parts com a descrição da OS.
                peca = new SparePart { CelulaId = area, Codigo = codigo, Descricao = p.Descricao.Trim() == "" ? codigo : p.Descricao.Trim().ToUpper(), CriadaEm = DateTime.Now };
                db.SpareParts.Add(peca);
                cadastro[codigo] = peca;
            }
            Registrar(peca, TipoMovimento.UsoOs, -p.Quantidade, $"Usada na {os.Numero}", os, fin);
        }
    }

    // Conclusão retornada pelo coordenador: o uso que ela registrou é desfeito.
    public void DesfazerUso(Finalizacao fin)
    {
        var usos = db.MovimentosPeca.Where(m => m.FinalizacaoId == fin.Id && m.Tipo == TipoMovimento.UsoOs).ToList();
        foreach (var u in usos)
        {
            var peca = db.SpareParts.Find(u.SparePartId);
            if (peca != null)
                Registrar(peca, TipoMovimento.Estorno, -u.Quantidade, $"Uso desfeito: conclusão da {fin.NumeroOS} retornada", equipamentoId: u.EquipamentoId)
                    .OrdemServicoId = u.OrdemServicoId;
        }
    }
}
