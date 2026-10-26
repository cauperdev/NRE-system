// ============================================================================
// BANCO DE DADOS — lista das tabelas e das regras entre elas. O arquivo do banco é dados/ms.db (SQLite).
// ONDE MEXER:
//   - Tabela nova .............................. acrescente um DbSet<Classe> aqui e a classe em Modelos/Entidades.cs
//   - Campo novo numa tabela ................... propriedade em Modelos/Entidades.cs + ALTER TABLE em Program.cs
//   - Índices e o que é apagado junto .......... OnModelCreating
//   - Texto gravado em MAIÚSCULA ............... Maiusculas (o que fica como foi digitado: ManterCaixa / TabelasComoVeio)
// ============================================================================
using Microsoft.EntityFrameworkCore;
using MS.Modelos;

namespace MS.BancoDeDados;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    // MAIÚSCULA (pedido do usuário, 2026-10-07): todo texto vai para o banco em maiúscula, de qualquer tela ou importação.
    // Fica como veio só o que não pode mudar: login, e-mail, senha e arquivos; e as tabelas do próprio sistema
    // (histórico, permissões por módulo e registro de e-mails — guardam chaves que o código procura pelo texto exato).
    static readonly HashSet<(Type, string)> ManterCaixa =
    [
        (typeof(Usuario), nameof(Usuario.Login)), (typeof(Usuario), nameof(Usuario.Email)), (typeof(Usuario), nameof(Usuario.SenhaHash)),
        (typeof(NreAnexo), nameof(NreAnexo.Nome)), (typeof(NreAnexo), nameof(NreAnexo.Arquivo)), (typeof(Imagem), nameof(Imagem.Arquivo)),
        (typeof(Equipamento), nameof(Equipamento.Foto)), (typeof(Celula), nameof(Celula.Logo)),
        (typeof(Equipamento), nameof(Equipamento.Manual)), (typeof(Equipamento), nameof(Equipamento.ManualNome)),
    ];
    static readonly HashSet<Type> TabelasComoVeio = [typeof(Historico), typeof(PermissaoGrupo), typeof(EmailEnviado)];

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Maiusculas();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        Maiusculas();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }


    void Maiusculas()
    {
        foreach (var e in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
            Maiuscula(e);
    }

    static void Maiuscula(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry e)
    {
        var tipo = e.Metadata.ClrType;
        if (TabelasComoVeio.Contains(tipo)) return;
        foreach (var p in e.Properties)
            if (p.CurrentValue is string s && !ManterCaixa.Contains((tipo, p.Metadata.Name)) && s.ToUpperInvariant() is var m && m != s)
                p.CurrentValue = m;
    }

    // O que foi gravado antes da regra: carrega todas as tabelas e grava de novo em maiúscula. Roda uma vez (Program.cs).
    public int PassarTudoParaMaiuscula()
    {
        Celulas.Load(); Usuarios.Load(); Equipamentos.Load(); Planos.Load(); TarefasPlano.Load(); Ordens.Load(); TarefasOS.Load();
        Pecas.Load(); Imagens.Load(); Finalizacoes.Load(); Nres.Load(); NreRemessas.Load(); NreItens.Load(); NreAnexos.Load();
        SpareParts.Load(); MovimentosPeca.Load(); WorkOrdersJms.Load();
        foreach (var e in ChangeTracker.Entries().ToList()) Maiuscula(e);
        return SaveChanges();
    }

    // Usuário removido: os planos em que ele era técnico ou coordenador ficam sem responsável. (Nas OS e finalizações o
    // próprio banco faz isso; em bancos antigos a tabela Planos não tem essa ligação, então é feito aqui.)
    public async Task SoltarDosPlanosAsync(IReadOnlyCollection<int> usuarios)
    {
        await Planos.Where(p => p.TecnicoId != null && usuarios.Contains(p.TecnicoId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.TecnicoId, (int?)null));
        await Planos.Where(p => p.CoordenadorId != null && usuarios.Contains(p.CoordenadorId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CoordenadorId, (int?)null));
    }

    public DbSet<Celula> Celulas => Set<Celula>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Equipamento> Equipamentos => Set<Equipamento>();
    public DbSet<PlanoManutencao> Planos => Set<PlanoManutencao>();
    public DbSet<TarefaPlano> TarefasPlano => Set<TarefaPlano>();
    public DbSet<OrdemServico> Ordens => Set<OrdemServico>();
    public DbSet<TarefaOS> TarefasOS => Set<TarefaOS>();
    public DbSet<PecaOS> Pecas => Set<PecaOS>();
    public DbSet<Imagem> Imagens => Set<Imagem>();
    public DbSet<Finalizacao> Finalizacoes => Set<Finalizacao>();
    public DbSet<Historico> Historicos => Set<Historico>();
    public DbSet<PermissaoGrupo> Permissoes => Set<PermissaoGrupo>();
    public DbSet<Nre> Nres => Set<Nre>();
    public DbSet<NreRemessa> NreRemessas => Set<NreRemessa>();
    public DbSet<NreItem> NreItens => Set<NreItem>();
    public DbSet<NreAnexo> NreAnexos => Set<NreAnexo>();
    public DbSet<SparePart> SpareParts => Set<SparePart>();
    public DbSet<MovimentoPeca> MovimentosPeca => Set<MovimentoPeca>();
    public DbSet<WorkOrderJms> WorkOrdersJms => Set<WorkOrderJms>();
    public DbSet<EmailEnviado> EmailsEnviados => Set<EmailEnviado>();   // avisos por e-mail (Servicos/Avisos.cs)

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Usuario>().HasIndex(u => u.Login).IsUnique();
        b.Entity<Equipamento>().HasIndex(e => e.Codigo).IsUnique();
        b.Entity<Equipamento>().HasIndex(e => e.Irf).IsUnique().HasFilter("\"Irf\" IS NOT NULL");   // IRF da Samsung: um por máquina (vazio pode repetir)
        b.Entity<Equipamento>().HasIndex(e => e.Etiqueta).IsUnique().HasFilter("\"Etiqueta\" IS NOT NULL");   // etiqueta eletrônica: uma por máquina
        // Não é único: as periodicidades do mesmo equipamento + plano dividem o número (Ordem de trabalho).
        b.Entity<OrdemServico>().HasIndex(o => o.Numero);
        b.Entity<Historico>().HasIndex(h => new { h.Entidade, h.EntidadeId });
        b.Entity<PermissaoGrupo>().HasIndex(p => new { p.Grupo, p.Modulo }).IsUnique();

        b.Entity<PlanoManutencao>().HasOne(p => p.Equipamento).WithMany().HasForeignKey(p => p.EquipamentoId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<OrdemServico>().HasOne(o => o.Equipamento).WithMany().HasForeignKey(o => o.EquipamentoId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PlanoManutencao>().HasMany(p => p.Tarefas).WithOne().HasForeignKey(t => t.PlanoId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PlanoManutencao>().HasOne(p => p.Tecnico).WithMany().HasForeignKey(p => p.TecnicoId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<PlanoManutencao>().HasOne(p => p.Coordenador).WithMany().HasForeignKey(p => p.CoordenadorId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<OrdemServico>().HasMany(o => o.Tarefas).WithOne().HasForeignKey(t => t.OrdemServicoId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OrdemServico>().HasMany(o => o.Pecas).WithOne().HasForeignKey(t => t.OrdemServicoId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OrdemServico>().HasMany(o => o.Imagens).WithOne().HasForeignKey(i => i.OrdemServicoId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OrdemServico>().HasOne(o => o.Plano).WithMany().HasForeignKey(o => o.PlanoId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<OrdemServico>().HasOne(o => o.Tecnico).WithMany().HasForeignKey(o => o.TecnicoId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<OrdemServico>().HasOne(o => o.Coordenador).WithMany().HasForeignKey(o => o.CoordenadorId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<Finalizacao>().HasMany(f => f.Imagens).WithOne().HasForeignKey(i => i.FinalizacaoId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Finalizacao>().HasOne(f => f.OrdemServico).WithMany().HasForeignKey(f => f.OrdemServicoId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Finalizacao>().HasOne(f => f.Tecnico).WithMany().HasForeignKey(f => f.TecnicoId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Finalizacao>().HasOne(f => f.Coordenador).WithMany().HasForeignKey(f => f.CoordenadorId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Finalizacao>().HasOne(f => f.AprovadaPor).WithMany().HasForeignKey(f => f.AprovadaPorId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Finalizacao>().HasOne(f => f.Equipamento).WithMany().HasForeignKey(f => f.EquipamentoId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<PecaOS>().Property(p => p.Quantidade).HasConversion<double>();

        // NRE: a máquina com NRE não pode ser apagada; OS apagada só solta o vínculo. (Tabelas criadas em Program.cs.)
        b.Entity<Nre>().HasOne(n => n.Equipamento).WithMany().HasForeignKey(n => n.EquipamentoId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Nre>().HasOne(n => n.Celula).WithMany().HasForeignKey(n => n.CelulaId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Nre>().HasOne(n => n.OrdemServico).WithMany().HasForeignKey(n => n.OrdemServicoId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Nre>().HasOne(n => n.Solicitante).WithMany().HasForeignKey(n => n.SolicitanteId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Nre>().HasOne(n => n.AnalisadaPor).WithMany().HasForeignKey(n => n.AnalisadaPorId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Nre>().HasMany(n => n.Itens).WithOne().HasForeignKey(i => i.NreId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Nre>().HasMany(n => n.Anexos).WithOne().HasForeignKey(a => a.NreId).OnDelete(DeleteBehavior.Cascade);
        // Detalhado: cada DI / compra nacional com os seus itens (o item fica ligado à NRE e ao bloco).
        b.Entity<Nre>().HasMany(n => n.Remessas).WithOne().HasForeignKey(r => r.NreId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<NreRemessa>().HasMany(r => r.Itens).WithOne().HasForeignKey(i => i.RemessaId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Nre>().Ignore(n => n.RemessasEmOrdem);   // só a lista em ordem (calculada), não é outra ligação
        b.Entity<Nre>().Property(n => n.AprovadoUsd).HasConversion<double?>();
        b.Entity<Nre>().Property(n => n.PtaxAprovacao).HasConversion<double?>();
        b.Entity<Nre>().Property(n => n.ValorAprovado).HasConversion<double?>();
        b.Entity<Nre>().Property(n => n.TaxaPercentual).HasConversion<double?>();
        b.Entity<Nre>().Property(n => n.CustoAbsorvido).HasConversion<double?>();
        b.Entity<NreRemessa>().Property(r => r.Ptax).HasConversion<double?>();
        b.Entity<NreRemessa>().Property(r => r.Frete).HasConversion<double>();
        b.Entity<NreRemessa>().Property(r => r.Seguro).HasConversion<double>();
        b.Entity<NreItem>().Property(i => i.Quantidade).HasConversion<double>();
        b.Entity<NreItem>().Property(i => i.ValorUnitario).HasConversion<double>();
        b.Entity<NreItem>().Property(i => i.ValorUnitarioUsd).HasConversion<double?>();

        // Spare Parts: código único dentro da área (cada área tem as suas peças); o histórico (movimentos) vai junto se a
        // peça for apagada; OS/finalização/máquina apagadas só soltam o vínculo (o movimento continua no histórico).
        // (Tabelas criadas em Program.cs.)
        b.Entity<SparePart>().HasIndex(p => new { p.CelulaId, p.Codigo }).IsUnique();
        b.Entity<SparePart>().Property(p => p.Estoque).HasConversion<double>();
        b.Entity<SparePart>().Property(p => p.EstoqueMinimo).HasConversion<double?>();
        b.Entity<SparePart>().HasMany(p => p.Movimentos).WithOne(m => m.SparePart).HasForeignKey(m => m.SparePartId).OnDelete(DeleteBehavior.Cascade);        b.Entity<MovimentoPeca>().Property(m => m.Quantidade).HasConversion<double>();
        b.Entity<MovimentoPeca>().Property(m => m.Saldo).HasConversion<double>();
        b.Entity<MovimentoPeca>().HasOne(m => m.OrdemServico).WithMany().HasForeignKey(m => m.OrdemServicoId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<MovimentoPeca>().HasOne(m => m.Finalizacao).WithMany().HasForeignKey(m => m.FinalizacaoId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<MovimentoPeca>().HasOne(m => m.Equipamento).WithMany().HasForeignKey(m => m.EquipamentoId).OnDelete(DeleteBehavior.SetNull);

        // Histórico do JMS antigo: uma linha por Work Order (a importação atualiza a que já existe). (Tabela criada em Program.cs.)
        b.Entity<WorkOrderJms>().ToTable("WorkOrdersJms");
        b.Entity<WorkOrderJms>().HasIndex(w => w.WorkOrder).IsUnique();
        b.Entity<WorkOrderJms>().HasIndex(w => w.Codigo);
        b.Entity<WorkOrderJms>().Property(w => w.TempoSugerido).HasConversion<double?>();
    }
}
