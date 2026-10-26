// ============================================================================
// DADOS INICIAIS — só roda quando o banco está VAZIO (a primeira vez que o sistema liga).
// Cria usuários de demonstração, células, equipamentos, planos, OS e NRE de exemplo (NresDeExemplo, valores fictícios). Não mexe em bancos que já têm dados.
// ONDE MEXER:
//   - Usuários e senhas iniciais ............... linhas U("login", "senha", "Nome", "Sobrenome", Grupo, célula)
//     (admin / admin123 · coordenador, analista, analista2, visitante / 123456 — iguais ao LEIA-ME.txt)
// ============================================================================
using Microsoft.AspNetCore.Identity;
using MS.Modelos;
using MS.Servicos;

namespace MS.BancoDeDados;

// Dados iniciais de demonstração (só roda com o banco vazio).
public static class Seed
{
    public static void Run(AppDb db)
    {
        if (db.Usuarios.Any()) return;

        var samsung = new Celula { Nome = "Samsung" };
        var avell = new Celula { Nome = "Avell" };
        db.Celulas.AddRange(samsung, avell);
        db.SaveChanges();

        var hasher = new PasswordHasher<Usuario>();
        Usuario U(string login, string senha, string nome, string sobrenome, Grupo g, Celula c)
        {
            // Contas de demonstração e o admin são locais (senha do sistema), não da rede (AD).
            var u = new Usuario { Login = login, Email = $"{login}@ms.local", PrimeiroNome = nome, UltimoNome = sobrenome, Grupo = g, CelulaId = c.Id, ContaLocal = true };
            u.SenhaHash = hasher.HashPassword(u, senha);
            return u;
        }
        var admin = U("admin", "admin123", "Christhian", "Cauper", Grupo.Administrador, samsung);
        var coord = U("coordenador", "123456", "Coordenador", "Demo", Grupo.Coordenador, samsung);
        var tec1 = U("analista", "123456", "Analista", "Demo", Grupo.Tecnico, avell);    // grupo Técnico = "Analista" no NRE
        var tec2 = U("analista2", "123456", "Segundo", "Analista", Grupo.Tecnico, samsung);
        db.Usuarios.AddRange(admin, coord, tec1, tec2,
            U("visitante", "123456", "Visitante", "Demo", Grupo.Visitante, samsung));
        db.SaveChanges();

        var equipamentos = new[]
        {
            ("PRDSHU00112", "SHUTTLER", "LINHA 03", avell),
            ("PRDADS00101", "ADESIVADORA", "SMT LINE 02", samsung),
            ("PRDADS00102", "ADESIVADORA", "SMT LINE 02", samsung),
            ("PRDAOI00104", "AOI", "SMT LINE 01", samsung),
            ("PRDAOI00103", "AOI", "SMT LINE 01", samsung),
            ("PRDARC00111", "AR CONDICIONADO DA PRINTER", "SMT LINE 01", samsung),
            ("PRDCHA00106", "CHAMBER", "SMT LINE 02", samsung),
            ("PRDCONV00107", "CONVEYOR", "LINHA 03", avell),
            ("PRDCONV00108", "CONVEYOR", "LINHA 03", avell),
            ("PRDPNT00109", "PARAFUSADEIRA", "LINHA 03", avell),
            ("PRDPTD00110", "PRINTER", "SMT LINE 01", samsung),
            ("PRDCHA00105", "CHAMBER", "SMT LINE 01", samsung),
        }.Select(e => new Equipamento { Codigo = e.Item1, Nome = e.Item2, Predio = "PRÉDIO 1", Localizacao = e.Item3, CelulaId = e.Item4.Id }).ToList();
        equipamentos[6].Status = StatusEquipamento.EmManutencao;
        equipamentos[10].Status = StatusEquipamento.Inativo;
        db.Equipamentos.AddRange(equipamentos);
        db.SaveChanges();

        var checklists = new Dictionary<string, string[]>
        {
            ["SHUTTLER"] = ["LIMPEZA DA ESTRUTURA", "LIMPEZA DE SENSORES", "VERIFICAÇÃO DE POLIAS", "VERIFICAÇÃO DE BELT", "VERIFICAÇÃO DA PNEUMÁTICA", "VERIFICAR BOTÕES DE FUNCIONAMENTO", "VERIFICAR E LUBRIFICAR O EIXO DE ABERTURA"],
            ["CONVEYOR"] = ["Conveyor: Verificar o desgaste e conexão da Belt", "Conveyor: Verificar a condição de rotação do rolo"],
            ["AOI"] = ["Limpeza das lentes da câmera", "Verificar iluminação", "Calibração com placa padrão"],
            ["ADESIVADORA"] = ["Limpeza do bico dosador", "Verificar pressão de ar", "Verificar nível do adesivo"],
            ["CHAMBER"] = ["Verificar vedação da porta", "Conferir temperatura com termômetro padrão"],
        };
        string[] Tarefas(string nome) => checklists.GetValueOrDefault(nome, ["Inspeção visual geral", "Limpeza externa", "Verificar ruídos anormais"]);

        var rnd = new Random(42);
        var agora = DateTime.Now;
        var osSeq = 0;
        var tipos = new[] { Periodicidade.Semanal, Periodicidade.Mensal, Periodicidade.Trimestral };
        foreach (var eq in equipamentos)
        {
            var numero = $"OS-{agora.Year}-{++osSeq:0000}"; // Ordem de trabalho = número da OS, o mesmo nas periodicidades
            foreach (var tipo in tipos.Take(eq.Nome == "SHUTTLER" ? 3 : 2))
            {
                var plano = new PlanoManutencao
                {
                    EquipamentoId = eq.Id,
                    Nome = eq.Nome,
                    OrdemTrabalho = numero,
                    Descricao = $"Preventiva {tipo.Nome().ToLower()} — {eq.Nome}",
                    Periodicidade = tipo,
                    Tarefas = Tarefas(eq.Nome).Select((t, i) => new TarefaPlano { Descricao = t, Ordem = i }).ToList(),
                };
                db.Planos.Add(plano);
                db.SaveChanges();

                var abertura = agora.AddDays(-rnd.Next(0, (int)tipo + 20)).Date.AddHours(10).AddMinutes(39);
                var os = new OrdemServico
                {
                    Numero = numero,
                    EquipamentoId = eq.Id,
                    PlanoId = plano.Id,
                    Tipo = tipo,
                    Prioridade = (Prioridade)rnd.Next(1, 5),
                    DataAbertura = abertura,
                    DataConclusao = Regras.Prazo(abertura, tipo),
                    Descricao = plano.Descricao,
                    Tarefas = Regras.CopiarTarefas(plano),
                };
                if (rnd.Next(4) == 0)
                {
                    os.Status = StatusOS.Aberta;
                    os.TecnicoId = plano.TecnicoId = tec1.Id;   // os responsáveis são os do plano (Cronograma)
                    os.CoordenadorId = plano.CoordenadorId = coord.Id;
                    os.Tarefas[0].Concluida = true;
                }
                db.Ordens.Add(os);
                db.SaveChanges();

                // Alguns ciclos já concluídos aguardando / aprovados.
                if (rnd.Next(3) == 0)
                {
                    var ini = abertura.AddDays(-(int)tipo);
                    var aprovada = rnd.Next(2) == 0;
                    db.Finalizacoes.Add(new Finalizacao
                    {
                        OrdemServicoId = os.Id,
                        NumeroOS = os.Numero,
                        EquipamentoId = eq.Id,
                        Tipo = tipo,
                        Descricao = plano.Descricao,
                        TecnicoId = tec2.Id,
                        CoordenadorId = coord.Id,
                        DataAbertura = ini,
                        DataConclusao = Regras.Prazo(ini, tipo),
                        ConcluidaEm = abertura,
                        Checklist = string.Join("\n", plano.Tarefas.Select(t => $"[x] {t.Descricao}")),
                        StatusRevisao = aprovada ? StatusRevisao.Aprovado : StatusRevisao.Aguardando,
                        AprovadaEm = aprovada ? abertura.AddHours(3) : null,
                        AprovadaPorId = aprovada ? coord.Id : null,
                    });
                    db.SaveChanges();
                }
            }
        }

        NresDeExemplo(db, samsung, avell, admin, coord, tec1, tec2);
    }

    // NRE de demonstração (valores fictícios): aguardando aprovação, concluídas e uma recusada, com importação (DI,
    // invoice, PTAX, frete e seguro) e compra nacional (NF), para a lista, o Resumo e o Detalhado não começarem vazios.
    static void NresDeExemplo(AppDb db, Celula samsung, Celula avell, Usuario admin, Usuario coord, Usuario tec1, Usuario tec2)
    {
        var hoje = DateTime.Today;
        var ano = hoje.Year;
        var n = 0;
        Nre Nova(Celula c, TipoNre tipo, StatusNre status, string maquina, string titulo, string modelo, string descricao, Usuario quem, int diasAtras)
        {
            n++;
            return new Nre
            {
                Numero = $"NRE-{ano}-{n:0000}", CelulaId = c.Id, Tipo = tipo, Status = status, Maquina = maquina,
                Titulo = titulo, Modelo = modelo, Descricao = descricao, SolicitanteId = quem.Id,
                CriadaEm = hoje.AddDays(-diasAtras).AddHours(9),
            };
        }
        void Aprovar(Nre x, decimal usd, decimal ptax, string po, int diasDepois)
        {
            x.CodigoNre = $"1220010{x.Id + 500:00000}";
            x.AprovadaClienteEm = x.CriadaEm.Date.AddDays(diasDepois);
            x.Aprovador = "Cliente Demo";
            x.AprovadoUsd = usd;
            x.PtaxAprovacao = ptax;
            x.ValorAprovado = Nre.Centavos(usd * ptax);
            x.PedidoCompra = po;
            x.Evidencia = "OK";
            x.AnalisadaPorId = coord.Id;
            x.AnalisadaEm = x.CriadaEm.AddDays(1);
        }
        NreRemessa Remessa(Nre x, TipoRemessa tipo, string? di, string? invoice, string? nf, string fornecedor, decimal? ptax, decimal frete, decimal seguro,
            params (string codigo, string desc, decimal qtd, decimal valor)[] itens)
        {
            var r = new NreRemessa { Ordem = x.Remessas.Count + 1, Tipo = tipo, Di = di, Invoice = invoice, NotaFiscal = nf, Fornecedor = fornecedor, Ptax = ptax, Frete = frete, Seguro = seguro };
            foreach (var (codigo, desc, qtd, valor) in itens)
            {
                var i = new NreItem { Codigo = codigo, Descricao = desc, Quantidade = qtd };
                if (tipo == TipoRemessa.Importacao) i.ValorUnitarioUsd = valor; else i.ValorUnitario = valor;
                r.Itens.Add(i);
                x.Itens.Add(i);
            }
            x.Remessas.Add(r);
            return r;
        }

        var a = Nova(samsung, TipoNre.Peca, StatusNre.Concluida, "ADESIVADORA", "Mobile Samsung - SMD Project A075", "A075",
            "Troca dos bicos dispensadores e da placa controladora da adesivadora da SMT LINE 02.", tec2, 40);
        var b = Nova(samsung, TipoNre.Servico, StatusNre.Concluida, "AOI", "Mobile Samsung - AOI Calibration", "A156",
            "Calibração da câmera e troca do conjunto de iluminação da AOI da SMT LINE 01.", tec2, 28);
        var c = Nova(avell, TipoNre.Ferramental, StatusNre.Aberta, "CONVEYOR", "Notebook Avell - Linha 03", "B11",
            "Guias e esteira nova para o conveyor da Linha 03 (modelo novo de notebook).", tec1, 15);
        var d = Nova(samsung, TipoNre.Melhoria, StatusNre.Aberta, "PRINTER", "Mobile Samsung - Printer Upgrade", "A256",
            "Kit de limpeza automática do stencil para a printer da SMT LINE 01.", tec2, 9);
        var e = Nova(avell, TipoNre.Peca, StatusNre.Aberta, "PARAFUSADEIRA", "Notebook Avell - Parafusadeira", "B11",
            "Duas parafusadeiras elétricas com controle de torque para a montagem final.", tec1, 3);
        var f = Nova(samsung, TipoNre.Outro, StatusNre.Recusada, "CHAMBER", "Mobile Samsung - Chamber", "A075",
            "Troca do sensor de temperatura da chamber da SMT LINE 02.", tec2, 20);
        var g = Nova(samsung, TipoNre.Peca, StatusNre.Aberta, "AOI", "Mobile Samsung - AOI Line 01", "A366",
            "Câmera de inspeção reserva para a AOI da SMT LINE 01.", tec2, 6);
        var h = Nova(avell, TipoNre.Servico, StatusNre.Concluida, "SHUTTLER", "Notebook Avell - Shuttler", "B09",
            "Revisão completa do shuttler da Linha 03 (troca de correias e rolamentos).", tec1, 35);
        db.Nres.AddRange(a, b, c, d, e, f, g, h);
        db.SaveChanges();

        Aprovar(a, 2600m, 5.42m, "4500012345", 3);
        Remessa(a, TipoRemessa.Importacao, "26/0001234-5", "INV-DEMO-0001", null, "Fornecedor Demo Ltd.", 5.3498m, 180m, 25m,
            ("BD-040", "BICO DISPENSADOR 0,4 MM", 2, 320m), ("PC-7710", "PLACA CONTROLADORA", 1, 1890m));
        Remessa(a, TipoRemessa.Nacional, null, null, "000.101", "Distribuidora Demo", null, 0, 0,
            ("CB-12", "CABO DE COMUNICAÇÃO", 2, 145m));
        a.CustoAbsorvido = 0;
        a.CobradaEm = a.CriadaEm.AddDays(25);
        a.ConcluidaEm = a.CriadaEm.AddDays(32);

        Aprovar(b, 1100m, 5.47m, "4500012388", 2);
        Remessa(b, TipoRemessa.Nacional, null, null, "000.214", "Serviços Técnicos Demo", 5.45m, 0, 0,
            ("SV-01", "CALIBRAÇÃO DA CÂMERA", 1, 3800m), ("LZ-220", "CONJUNTO DE ILUMINAÇÃO", 1, 2150m));
        b.CobradaEm = b.CriadaEm.AddDays(20);
        b.ConcluidaEm = b.CriadaEm.AddDays(24);

        Aprovar(c, 1800m, 5.50m, "4500012401", 2);
        Remessa(c, TipoRemessa.Importacao, null, "INV-DEMO-0007", null, "Fornecedor Demo Ltd.", null, 95m, 12m,
            ("GU-300", "GUIA LATERAL DO CONVEYOR", 4, 210m), ("ES-1200", "ESTEIRA 1200 MM", 1, 760m));

        Aprovar(d, 950m, 5.47m, "4500012430", 2);

        Aprovar(g, 3150m, 5.49m, "4500012455", 2);
        Remessa(g, TipoRemessa.Importacao, "26/0004410-2", "INV-DEMO-0012", null, "Fornecedor Demo Ltd.", 5.4870m, 140m, 18m,
            ("CM-5MP", "CÂMERA DE INSPEÇÃO 5 MP", 1, 2780m), ("LT-16", "LENTE TELECÊNTRICA 16 MM", 1, 410m));

        Aprovar(h, 1300m, 5.38m, "4500012360", 2);
        Remessa(h, TipoRemessa.Nacional, null, null, "000.087", "Manutenção Industrial Demo", 5.38m, 0, 0,
            ("SV-02", "REVISÃO DO SHUTTLER", 1, 4200m), ("CR-90", "CORREIA DENTADA", 2, 380m), ("RL-6204", "ROLAMENTO 6204", 4, 62m));
        h.CobradaEm = h.CriadaEm.AddDays(18);
        h.ConcluidaEm = h.CriadaEm.AddDays(21);

        f.AnalisadaPorId = coord.Id;
        f.AnalisadaEm = f.CriadaEm.AddDays(2);
        f.MotivoRecusa = "Coberto pela garantia do fabricante da máquina.";
        db.SaveChanges();
    }
}
