// ============================================================================
// INÍCIO DO SISTEMA NRE — primeiro arquivo que roda quando o NRE liga.
// O NRE é a parte de NRE do MS (Gestão de Manutenção) separada num programa próprio (24/10/2026):
// Login → cartões das áreas (Conta/Area) → NRE. Mesmo visual, mesmas regras de acesso por grupo.
// ONDE MEXER:
//   1. Idioma e formato de datas/números ...... cultura "pt-BR" (logo abaixo)
//   2. Quem precisa de login .................. AddRazorPages (sem login só as telas Conta/Login e Erro)
//   3. Onde fica o banco de dados ............. "dados/nre.db" (ou ConnectionStrings:Nre no appsettings.json)
//   4. Tempo que o login dura (12 h) .......... AddCookie → ExpireTimeSpan
//   5. Serviços do sistema .................... AddScoped/AddSingleton (Regras, Auditoria, Anexos, Rede...)
//      Login pela rede (AD) ................... "AD" no appsettings.json (Servicos/Rede.cs)
//      HTTPS (cadeado) ........................ "Https" no appsettings.json (Servicos/Https.cs)
//   6. Atualização automática do banco ........ bloco "using (var scope ...)". Criou um campo novo em
//      Modelos/Entidades.cs? Acrescente aqui o ALTER TABLE dele (o banco já existe; o EnsureCreated não muda tabela).
//   7. Página de erro ......................... UseExceptionHandler("/Erro")
// Porta/endereço: iniciar.bat usa 5110 (o MS usa 5100: os dois podem ficar ligados juntos).
// Cookies com nome próprio ("nre-..."): o navegador divide os cookies do localhost entre as portas, então com os
// nomes padrão entrar no NRE derrubaria o login do MS (e vice-versa).
// ============================================================================
using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Servicos;

// ---------- 1. IDIOMA E FORMATO (pt-BR: datas dd/mm/aaaa, vírgula nos decimais) ----------
var cultura = new CultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = cultura;
CultureInfo.DefaultThreadCurrentUICulture = cultura;

var builder = WebApplication.CreateBuilder(args);

// ---------- 2. TELAS E LOGIN (todas as telas pedem login, menos Login e Erro) ----------
builder.Services.AddRazorPages(o =>
    {
        o.Conventions.AuthorizeFolder("/");
        o.Conventions.AllowAnonymousToPage("/Conta/Login");
        o.Conventions.AllowAnonymousToPage("/Erro");
    })
    .AddMvcOptions(o =>
    {
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
        // Voltar de um registro para a lista com o mesmo filtro (Servicos/VoltaParaLista.cs).
        o.Filters.Add<MS.Servicos.VoltaParaLista>();
        // Mensagens automáticas de campo inválido em português (o padrão do ASP.NET é em inglês).
        var m = o.ModelBindingMessageProvider;
        m.SetValueMustNotBeNullAccessor(_ => "Preencha este campo.");
        m.SetMissingBindRequiredValueAccessor(campo => $"Preencha o campo {campo}.");
        m.SetMissingKeyOrValueAccessor(() => "Preencha este campo.");
        m.SetMissingRequestBodyRequiredValueAccessor(() => "Preencha os campos do formulário.");
        m.SetAttemptedValueIsInvalidAccessor((valor, campo) => $"O valor \"{valor}\" não é válido para {campo}.");
        m.SetNonPropertyAttemptedValueIsInvalidAccessor(valor => $"O valor \"{valor}\" não é válido.");
        m.SetUnknownValueIsInvalidAccessor(campo => $"Valor inválido em {campo}.");
        m.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Valor inválido.");
        m.SetValueIsInvalidAccessor(valor => $"O valor \"{valor}\" não é válido.");
        m.SetValueMustBeANumberAccessor(campo => $"{campo} precisa ser um número.");
        m.SetNonPropertyValueMustBeANumberAccessor(() => "Use um número.");
    });
builder.Services.Configure<Microsoft.AspNetCore.Mvc.CookieTempDataProviderOptions>(o => o.Cookie.Name = "nre-mensagens");
builder.Services.AddAntiforgery(o => o.Cookie.Name = "nre-formulario");

// ---------- 3. BANCO DE DADOS ----------
builder.Services.AddDbContext<AppDb>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Nre") ?? "Data Source=dados/nre.db"));

// ---------- 4. SESSÃO DO LOGIN (cookie de 12 h, renovado enquanto a pessoa usa) ----------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "nre-login";
        o.LoginPath = "/Conta/Login";
        o.AccessDeniedPath = "/Conta/Negado";
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
    });

// Listas grandes: "marcar todos" envia um campo por linha, e o padrão do ASP.NET recusa mais de 1.024 campos (erro 400).
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.ValueCountLimit = 100_000);

// ---------- 5. SERVIÇOS DO SISTEMA (pasta Servicos) ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Auditoria>();
builder.Services.AddScoped<Regras>();              // número da próxima NRE
builder.Services.AddScoped<Estoque>();             // usado pelo Regras (veio do MS)
builder.Services.AddSingleton<Fotos>();            // logos das áreas (dados/fotos)
builder.Services.AddSingleton<Anexos>();           // arquivos da NRE (dados/anexos) — Servicos/Anexos.cs
builder.Services.AddSingleton<Rede>();             // login pela rede (AD) — Servicos/Rede.cs
builder.Services.AddSingleton<TentativasLogin>();  // pausa depois de várias senhas erradas

// ---------- HTTPS (cadeado para quem vem pela rede — Servicos/Https.cs; "Https" no appsettings.json) ----------
var certificado = Https.Certificado(builder.Configuration, builder.Environment.ContentRootPath);
var portaHttps = builder.Configuration.GetValue("Https:Porta", 5464);
if (certificado != null)
{
    var url = (builder.Configuration["urls"] ?? "http://localhost:5110").Split(';')[0].Trim();
    var naRede = url.Contains("0.0.0.0") || url.Contains("://*") || url.Contains("://+");
    var portaHttp = new Uri(System.Text.RegularExpressions.Regex.Replace(url, @"://(0\.0\.0\.0|\*|\+)", "://localhost")).Port;
    builder.WebHost.ConfigureKestrel(k =>
    {
        if (naRede) { k.ListenAnyIP(portaHttp); k.ListenAnyIP(portaHttps, l => l.UseHttps(certificado)); }
        else { k.ListenLocalhost(portaHttp); k.ListenLocalhost(portaHttps, l => l.UseHttps(certificado)); }
    });
}

var app = builder.Build();

// ---------- 6. ATUALIZAÇÃO AUTOMÁTICA DO BANCO (roda toda vez que o sistema liga) ----------
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "dados"));
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.EnsureCreated();
    // Modo WAL: ler e gravar ao mesmo tempo não se atrapalham.
    try { db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL"); } catch (Exception) { }
    Seed.Run(db);
    // Grupos Auditor e EHS não existem no NRE (24/10/2026): quem estiver neles passa para Visitante (no NRE os dois já
    // não viam nada, como o Visitante). A pessoa vê o grupo novo na próxima vez que entrar.
    db.Database.ExecuteSqlRaw("UPDATE \"Usuarios\" SET \"Grupo\" = 1 WHERE \"Grupo\" IN (2, 6)");
    Acesso.Carregar(db);
}

// ---------- 7. PÁGINA DE ERRO E ORDEM DE PROCESSAMENTO ----------
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Erro");

// HTTPS: quem vem pela rede (celular, outro PC) em http é levado para https — a senha nunca viaja aberta.
// Quem abre no próprio PC (localhost) continua em http, sem aviso de certificado.
if (certificado != null)
    app.Use(async (ctx, proximo) =>
    {
        var remoto = ctx.Connection.RemoteIpAddress;
        if (remoto is { IsIPv4MappedToIPv6: true }) remoto = remoto.MapToIPv4();
        if (!ctx.Request.IsHttps && remoto != null && !System.Net.IPAddress.IsLoopback(remoto))
        {
            ctx.Response.Redirect($"https://{ctx.Request.Host.Host}:{portaHttps}{ctx.Request.PathBase}{ctx.Request.Path}{ctx.Request.QueryString}",
                permanent: false, preserveMethod: true);
            return;
        }
        await proximo();
    });

// Endereço que não existe (ou registro apagado): mostra a tela de erro do sistema.
app.UseStatusCodePagesWithReExecute("/Erro", "?codigo={0}");

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();
