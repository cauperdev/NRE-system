// ============================================================================
// DADOS DOS GRÁFICOS DO DASHBOARD — formato recebido pelos desenhos Pages/Shared/_Rosca.cshtml (rosca)
// e Pages/Shared/_Colunas.cshtml (colunas). Os números são calculados em Pages/Index.cshtml.cs.
// ============================================================================
namespace MS.Modelos;

// Dados do gráfico de rosca do dashboard (partial Shared/_Rosca).
public record Fatia(string Rotulo, int Qtd, string Cor, string? Url = null, bool Ativa = false);

public record Rosca(string Titulo, List<Fatia> Fatias);

// Gráfico de colunas (partial Shared/_Colunas): cada coluna tem um valor por série.
public record Serie(string Nome, string Cor);
public record Coluna(string Rotulo, int[] Valores, string? Url = null, bool Ativa = false);
public record Colunas(string Titulo, List<Serie> Series, List<Coluna> Itens);
