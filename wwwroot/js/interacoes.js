// ============================================================================
// INTERAÇÕES DA TELA — o que acontece ao clicar, marcar, digitar (vale para todas as páginas).
// Ligado em Pages/Shared/_Layout.cshtml. Cada bloco é ativado por um atributo no HTML (data-...),
// então para usar numa tela nova basta colocar o atributo — não precisa mexer aqui.
//
// ÍNDICE — aperte Ctrl+F e procure pelo número (ex.: "== 5."):
//    1. Menu hambúrguer ................... abre/fecha o menu lateral
//    2. Marcar todos ...................... data-marcar-todos=".classe-das-caixinhas"
//    3. Botões com seleção e confirmação .. data-exige-selecao / data-confirmar="Pergunta?"
//                                           (janela do sistema: msConfirmar / msAvisar)
//    4. Listas dinâmicas .................. data-adicionar (tarefas do plano, peças da OS)
//    5. Aba ativa ......................... volta para a mesma aba depois de salvar
//    6. Atualizar sem recarregar .......... data-sem-recarregar (Dashboard e Calendário)
//    7. Próxima manutenção automática ..... data-proxima (data + periodicidade, sem domingo; plano novo: data-inicio-auto)
//    8. Código automático do equipamento .. data-codigo-de (prefixo do prédio + sigla + número)
//    9. Ver mais / Ver menos .............. data-alternar
//   10. Linhas e cartões clicáveis ........ data-href (clicar abre o registro)
//   11. Várias fotos antes de salvar ...... data-fotos (fotos da OS somam em vez de substituir; só foto de hoje, com a hora)
//   12. Caixa de seleção com pesquisa ..... data-pesquisa no <select> (equipamento no Cronograma e na OS nova)
//                                           e no <input list> (texto livre: Equipamento da NRE)
//   13. Dica dos gráficos ................. data-dica (balão com o <span class="dica-conteudo" hidden> de dentro)
//   14. Motivo do retorno ................. data-retorno (coluna Retorno da lista de OS e cartão do celular)
//   15. Voltar para a lista com o filtro .. listas guardam o filtro (cookie) e a rolagem; Servicos/VoltaParaLista.cs
// ============================================================================
// ==================== 1. MENU HAMBÚRGUER ====================
// Menu hambúrguer: no computador recolhe/expande a lateral; no celular abre por cima do conteúdo.
// Menu recolhido no computador fica guardado (ms-menu) e já vem aplicado antes de a página aparecer (script no
// início do <body> do _Layout). A animação de deslizar ("menu-animando", estilos.css seção 3) só vale para o clique.
(function () {
    const body = document.body;
    const btn = document.getElementById('btnMenu');
    const fundo = document.getElementById('fundoMenu');
    if (!btn) return;
    const tela = window.matchMedia('(max-width: 991.98px)');
    const celular = () => tela.matches;
    const recolhidoGuardado = () => { try { return localStorage.getItem('ms-menu') === 'recolhido'; } catch { return false; } };

    let fimAnimacao;
    function animar(mudar) {
        body.classList.add('menu-animando');
        mudar();
        clearTimeout(fimAnimacao);
        fimAnimacao = setTimeout(() => body.classList.remove('menu-animando'), 300); // um pouco mais que os .25s do CSS
    }
    const fecharNoCelular = () => { if (body.classList.contains('menu-aberto')) animar(() => body.classList.remove('menu-aberto')); };

    btn.addEventListener('click', () => animar(() => {
        if (celular()) {
            body.classList.toggle('menu-aberto');
        } else {
            body.classList.toggle('menu-recolhido');
            try { localStorage.setItem('ms-menu', body.classList.contains('menu-recolhido') ? 'recolhido' : 'aberto'); } catch { }
        }
    }));
    fundo?.addEventListener('click', fecharNoCelular);
    document.addEventListener('keydown', e => { if (e.key === 'Escape') fecharNoCelular(); });

    // Tela passou de celular para computador (ou o contrário): sem animação, o menu já fica como deve naquele tamanho —
    // no celular fechado; no computador como estava guardado.
    tela.addEventListener('change', () => {
        clearTimeout(fimAnimacao);
        body.classList.remove('menu-animando', 'menu-aberto');
        body.classList.toggle('menu-recolhido', !celular() && recolhidoGuardado());
    });
})();

// ==================== 2. MARCAR TODOS ====================
// Caixa "marcar todos" das listas. Em lista com páginas (data-total + data-todos), vale para a lista inteira:
// o campo escondido data-todos vai como true e a ação atinge todas as páginas. Desmarcar uma linha desfaz isso.
// A lista existe duas vezes na página (cartões no celular/tablet, tabela no computador): só vale a que está na tela.
const msVisivel = c => c.offsetParent !== null;
document.querySelectorAll('[data-marcar-todos]').forEach(mestre => {
    const todos = mestre.dataset.todos ? document.querySelector(mestre.dataset.todos) : null;
    const itens = () => document.querySelectorAll(mestre.dataset.marcarTodos);
    mestre.addEventListener('change', () => {
        itens().forEach(c => c.checked = mestre.checked && msVisivel(c));
        if (todos) todos.value = mestre.checked;
    });
    itens().forEach(c => c.addEventListener('change', () => {
        if (!c.checked) { mestre.checked = false; if (todos) todos.value = false; }
    }));
});

// ==================== 3. BOTÕES COM SELEÇÃO E CONFIRMAÇÃO ====================
// As perguntas aparecem numa janela do próprio sistema (e não no pop-up do navegador "localhost diz").
// Para usar em outro lugar do código:
//   msConfirmar('Pergunta?', { titulo, botao, classe: 'b-vermelho' }).then(sim => { if (sim) ... });
//   msConfirmar('Pergunta?', { campo: 'Motivo' }).then(texto => ...)  → janela com campo de texto obrigatório//   msAvisar('Mensagem.');   → só com o botão OK
// No botão: data-confirmar-campo="Motivo do retorno" data-confirmar-nome="motivo" → a janela pede o texto e ele vai
// junto no envio do formulário com esse nome (ex.: Retornar da Finalização).
(function () {
    const t = s => window.msT ? msT(s) : s;

    function janela({ titulo, mensagem, botao, classe, soOk, campo }) {
        return new Promise(resolve => {
            const div = document.createElement('div');
            div.className = 'modal fade janela-confirmar';
            div.tabIndex = -1;
            div.innerHTML = `<div class="modal-dialog modal-dialog-centered"><div class="modal-content">
                <div class="modal-header"><h5 class="modal-title"></h5><button type="button" class="btn-close" data-bs-dismiss="modal"></button></div>
                <div class="modal-body"></div>
                <div class="modal-footer">${soOk ? '' : '<button type="button" class="btn-ms b-cinza" data-bs-dismiss="modal"></button>'}<button type="button" class="btn-ms" data-ok></button></div>
            </div></div>`;
            div.querySelector('.modal-title').textContent = titulo;
            const corpo = div.querySelector('.modal-body');
            if (mensagem instanceof Node) corpo.append(mensagem); else corpo.textContent = mensagem;
            // Campo de texto obrigatório (ex.: motivo do retorno): sem texto, a janela não fecha.
            let texto = null;
            if (campo) {
                const rotulo = document.createElement('label');
                rotulo.className = 'form-label obrig d-block mt-3 mb-1';
                rotulo.textContent = campo;
                texto = document.createElement('textarea');
                texto.className = 'form-control';
                texto.rows = 3;
                texto.maxLength = 1000;
                rotulo.htmlFor = texto.id = 'janelaCampo' + Date.now();
                const aviso = document.createElement('div');
                aviso.className = 'invalid-feedback';
                aviso.textContent = t('Escreva o motivo.');
                corpo.append(rotulo, texto, aviso);
                texto.addEventListener('input', () => texto.classList.remove('is-invalid'));
            }
            if (!soOk) div.querySelector('.b-cinza').textContent = t('Cancelar');
            const ok = div.querySelector('[data-ok]');
            ok.textContent = botao;
            ok.classList.add(classe);
            let resposta = false;
            const modal = new bootstrap.Modal(div);
            ok.addEventListener('click', () => {
                if (texto && !texto.value.trim()) { texto.classList.add('is-invalid'); texto.focus(); return; }
                resposta = texto ? texto.value.trim() : true;
                modal.hide();
            });
            div.addEventListener('shown.bs.modal', () => (texto || ok).focus());   // Enter confirma, Esc cancela
            div.addEventListener('hidden.bs.modal', () => { div.remove(); resolve(resposta); });
            document.body.appendChild(div);
            modal.show();
        });
    }
    window.msConfirmar = (mensagem, o = {}) =>
        janela({ titulo: o.titulo || t('Confirmar'), mensagem, botao: o.botao || t('Confirmar'), classe: o.classe || 'b-azul', campo: o.campo });
    window.msAvisar = (mensagem, titulo) => janela({ titulo: titulo || t('Atenção'), mensagem, botao: 'OK', classe: 'b-azul', soOk: true });

    // A janela usa o nome e a cor do botão clicado (vermelho em Remover, verde em Aprovado...).
    const corDe = b => ['b-vermelho', 'b-verde', 'b-azulclaro', 'b-azul', 'b-cinza'].find(c => b.classList.contains(c)) || 'b-azul';
    const nomeDe = b => b.innerText.trim() || t('Confirmar');
    const perguntar = (b, msg) => msConfirmar(msg, {
        titulo: nomeDe(b), botao: nomeDe(b), classe: corDe(b), campo: b.dataset.confirmarCampo ? t(b.dataset.confirmarCampo) : null,
    });
    // Confirmou: envia pelo mesmo botão (mantém o handler da ação e o name=value dele). O texto do campo da janela
    // (data-confirmar-campo) vai num campo escondido com o nome de data-confirmar-nome.
    function prosseguir(b, resposta) {
        if (typeof resposta === 'string' && b.dataset.confirmarNome && b.form) {
            let oculto = b.form.querySelector(`input[type=hidden][name="${b.dataset.confirmarNome}"]`);
            if (!oculto) {
                oculto = document.createElement('input');
                oculto.type = 'hidden';
                oculto.name = b.dataset.confirmarNome;
                b.form.append(oculto);
            }
            oculto.value = resposta;
        }
        b.dataset.confirmado = '1';
        if (b.form) b.form.requestSubmit(b); else b.click();
        setTimeout(() => delete b.dataset.confirmado, 0);
    }

    // Botões que exigem ao menos uma linha marcada (Remover selecionados, Aprovado, Retornar...).
    document.querySelectorAll('[data-exige-selecao]').forEach(b => {
        b.addEventListener('click', e => {
            if (b.dataset.confirmado) return;
            e.preventDefault();
            // Pode haver um "marcar todos" nos cartões e outro na tabela: vale o que está na tela.
            const mestres = [...document.querySelectorAll(`[data-marcar-todos="${b.dataset.exigeSelecao}"]`)];
            const mestre = mestres.find(msVisivel) || mestres[0];
            const todos = !!(mestre?.checked && msVisivel(mestre) && mestre.dataset.total);
            // Caixinha marcada na outra versão da lista (fora da tela) não conta nem vai junto.
            document.querySelectorAll(b.dataset.exigeSelecao + ':checked').forEach(c => { if (!msVisivel(c)) c.checked = false; });
            const campoTodos = mestre?.dataset.todos ? document.querySelector(mestre.dataset.todos) : null;
            if (campoTodos && !todos) campoTodos.value = false;
            const marcados = todos ? +mestre.dataset.total : document.querySelectorAll(b.dataset.exigeSelecao + ':checked').length;
            if (!marcados) { msAvisar(t('Marque ao menos um item na lista.')); return; }
            const msg = todos && b.dataset.confirmarTodos ? b.dataset.confirmarTodos : b.dataset.confirmar;
            if (!msg) { prosseguir(b); return; }
            perguntar(b, msg.replace('{n}', marcados)).then(sim => { if (sim) prosseguir(b, sim); });
        });
    });

    // Botões com pergunta simples (Remover no cadastro, Concluir OS, Restaurar padrão...).
    document.querySelectorAll('[data-confirmar]:not([data-exige-selecao])').forEach(b => {
        b.addEventListener('click', e => {
            if (b.dataset.confirmado) return;
            e.preventDefault();
            perguntar(b, b.dataset.confirmar).then(sim => { if (sim) prosseguir(b, sim); });
        });
    });
})();

// ==================== 4. LISTAS DINÂMICAS ====================
// Listas dinâmicas (tarefas do plano, peças): botão adicionar clona o <template>.
document.querySelectorAll('[data-adicionar]').forEach(b => {
    b.addEventListener('click', () => {
        const tpl = document.getElementById(b.dataset.adicionar);
        const destino = document.getElementById(b.dataset.destino);
        destino.appendChild(tpl.content.cloneNode(true));
        destino.lastElementChild.querySelector('input:not([type=hidden]),textarea')?.focus();
    });
});
document.addEventListener('click', e => {
    const rem = e.target.closest('[data-remover-linha]');
    if (rem) rem.closest(rem.dataset.removerLinha).remove();
});
// ==================== 5. ABA ATIVA ====================
// Mantém a aba ativa quando a tela volta (erro ao salvar, ou endereço com ?aba=...).
document.querySelectorAll('.abas [data-bs-toggle="tab"]').forEach(a => {
    a.addEventListener('shown.bs.tab', () => {
        const campo = document.getElementById('abaAtiva');
        if (campo) campo.value = a.getAttribute('data-bs-target');
    });
});
const abaInicial = document.getElementById('abaAtiva')?.value;
if (abaInicial) {
    const link = document.querySelector(`.abas [data-bs-target="${abaInicial}"]`);
    if (link) bootstrap.Tab.getOrCreateInstance(link).show();
}

// ==================== 6. ATUALIZAR SEM RECARREGAR ====================
// Áreas com [data-sem-recarregar] (ex.: dashboard): links e filtros trocam só o conteúdo da área,
// sem recarregar a página — assim a tela não volta para o topo.
(function () {
    async function atualizar(area, url, empilhar) {
        area.classList.add('carregando');
        try {
            const resp = await fetch(url, { headers: { 'X-Requested-With': 'fetch' } });
            if (!resp.ok || resp.redirected) throw new Error(resp.status);
            const doc = new DOMParser().parseFromString(await resp.text(), 'text/html');
            const nova = doc.getElementById(area.id);
            if (!nova) throw new Error('sem área');
            area.innerHTML = nova.innerHTML;
            if (empilhar) history.pushState({ parcial: area.id }, '', url);
            // Link com âncora (ex.: calendário → #lista-dia): desce até a parte nova.
            const ancora = new URL(url).hash;
            if (empilhar && ancora) document.getElementById(ancora.slice(1))?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        } catch {
            location.href = url; // se algo falhar, recarrega do jeito normal
        } finally {
            area.classList.remove('carregando');
        }
    }

    document.addEventListener('click', e => {
        const a = e.target.closest('a[href]');
        const area = a?.closest('[data-sem-recarregar]');
        // getAttribute: nas fatias da rosca o <a> é SVG e .href/.target não são texto.
        if (!area || a.hasAttribute('data-normal') || e.ctrlKey || e.metaKey || e.shiftKey || a.getAttribute('target') === '_blank') return;
        const url = new URL(a.getAttribute('href'), location.href);
        if (url.origin !== location.origin || url.pathname !== location.pathname) return;
        e.preventDefault();
        atualizar(area, url.href, true);
    });

    document.addEventListener('submit', e => {
        const form = e.target;
        const area = form.closest('[data-sem-recarregar]');
        if (!area || (form.method || 'get').toLowerCase() !== 'get') return;
        e.preventDefault();
        const url = new URL(form.action || location.href, location.href);
        url.search = new URLSearchParams(new FormData(form)).toString();
        // Remove parâmetros vazios para a URL ficar limpa.
        [...url.searchParams.keys()].filter(k => !url.searchParams.get(k)).forEach(k => url.searchParams.delete(k));
        atualizar(area, url.href, true);
    });

    // Voltar/avançar do navegador também atualiza a área.
    window.addEventListener('popstate', () => {
        const area = document.querySelector('[data-sem-recarregar]');
        if (area) atualizar(area, location.href, false);
    });
})();

// ==================== 7. PRÓXIMA MANUTENÇÃO AUTOMÁTICA ====================
// Próxima manutenção = data da manutenção + periodicidade, preenchida sozinha (nunca no domingo: vai para a segunda).
// O campo pode ser editado: só é recalculado quando a data ou a periodicidade mudam. Domingo escolhido vira segunda.
// Plano novo (data-inicio-auto="7"): a data da 1ª manutenção acompanha a periodicidade — hoje + período, mínimo 7 dias —
// até a pessoa mudar a data à mão (mesma regra do servidor: Regras.PrimeiraManutencao).
// <input type="date" data-proxima="#alvo" data-periodo="#selectPeriodicidade">  (ou data-periodo-dias="7")
(function () {
    const DIAS = { Diaria: 1, Semanal: 7, Quinzenal: 15, Mensal: 30, Trimestral: 90, Semestral: 180, Anual: 365 };
    const dois = n => String(n).padStart(2, '0');
    const iso = d => d.getFullYear() + '-' + dois(d.getMonth() + 1) + '-' + dois(d.getDate());
    const lerData = v => { const [a, m, d] = v.split('-').map(Number); return new Date(a, m - 1, d); };
    const semDomingo = d => d.getDay() === 0 ? new Date(d.getFullYear(), d.getMonth(), d.getDate() + 1) : d;
    // Campo de data com domingo escolhido: passa para a segunda.
    const tirarDomingo = campo => {
        if (campo.type !== 'date' || !campo.value) return;
        const d = lerData(campo.value);
        if (d.getDay() === 0) campo.value = iso(semDomingo(d));
    };
    document.querySelectorAll('input[data-proxima]').forEach(inp => {
        const alvo = document.querySelector(inp.dataset.proxima);
        const sel = inp.dataset.periodo ? document.querySelector(inp.dataset.periodo) : null;
        if (!alvo) return;
        const inicioAuto = inp.dataset.inicioAuto ? parseInt(inp.dataset.inicioAuto, 10) : null;
        let dataMexida = false;
        const diasDoPeriodo = () => sel ? DIAS[sel.value] : parseInt(inp.dataset.periodoDias, 10);
        const calcular = () => {
            const dias = diasDoPeriodo();
            if (!inp.value || !dias) { alvo.value = ''; return; }
            const d = lerData(inp.value);
            const prox = semDomingo(new Date(d.getFullYear(), d.getMonth(), d.getDate() + dias));
            alvo.value = alvo.type === 'date' ? iso(prox) : prox.toLocaleDateString('pt-BR');
        };
        inp.addEventListener('input', () => { dataMexida = true; tirarDomingo(inp); calcular(); });
        alvo.addEventListener('change', () => tirarDomingo(alvo));
        sel?.addEventListener('change', () => {
            const dias = diasDoPeriodo();
            if (inicioAuto != null && !dataMexida && dias) {
                const h = new Date();
                inp.value = iso(semDomingo(new Date(h.getFullYear(), h.getMonth(), h.getDate() + Math.max(dias, inicioAuto))));
            }
            calcular();
        });
        if (!alvo.value) calcular(); // ao abrir, mantém a próxima já salva
    });
})();

// ==================== 8. CÓDIGO AUTOMÁTICO DO EQUIPAMENTO ====================
// Código gerado pelo sistema a partir de outros campos (cadastro de equipamento: prefixo do prédio + sigla do nome + número,
// ex.: PRÉDIO 1 + ADESIVADORA → PRDADS00113). Muda junto enquanto o nome ou o prédio são digitados.
// <input readonly data-codigo-de="#campoNome" data-codigo-predio="#campoPredio" data-codigo-url="/…?handler=Codigo">
document.querySelectorAll('input[data-codigo-de]').forEach(inp => {
    const origem = document.querySelector(inp.dataset.codigoDe);
    const predio = inp.dataset.codigoPredio ? document.querySelector(inp.dataset.codigoPredio) : null;
    if (!origem) return;
    let timer, pedido = 0;
    const atualizar = async () => {
        const nome = origem.value.trim(), pr = predio ? predio.value.trim() : '', meu = ++pedido;
        if (!nome || (predio && !pr)) { inp.value = ''; return; }
        try {
            const url = inp.dataset.codigoUrl + (inp.dataset.codigoUrl.includes('?') ? '&' : '?') + 'nome=' + encodeURIComponent(nome)
                + (predio ? '&predio=' + encodeURIComponent(pr) : '');
            const r = await fetch(url);
            if (r.ok && meu === pedido) inp.value = await r.text(); // ignora respostas atrasadas
        } catch { }
    };
    [origem, predio].forEach(c => c?.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(atualizar, 250); }));
    if (origem.value.trim()) atualizar();
});

// ==================== 9. VER MAIS / VER MENOS ====================
// "Ver mais / Ver menos": mostra ou esconde a parte extra de uma lista (ex.: aviso da importação).
// <button data-alternar="#alvo" data-texto-menos="Ver menos">Ver mais 20</button>
document.addEventListener('click', e => {
    const b = e.target.closest('[data-alternar]');
    const alvo = b && document.querySelector(b.dataset.alternar);
    if (!alvo) return;
    b.dataset.textoMais ??= b.textContent.trim();
    alvo.hidden = !alvo.hidden;
    b.textContent = alvo.hidden ? b.dataset.textoMais : b.dataset.textoMenos;
});

// ==================== 10. LINHAS E CARTÕES CLICÁVEIS ====================
// Linhas clicáveis (<tr data-href="...">): clicar em qualquer parte abre o registro.
// Links, botões e caixinhas da linha continuam funcionando normalmente; Ctrl+clique abre em outra aba.
document.addEventListener('click', e => {
    const tr = e.target.closest('tr[data-href], .cartao-os[data-href]'); // linha da tabela ou cartão (celular)
    if (!tr || e.target.closest('a, button, input, label, select, textarea, td.acoes-linha')) return;
    if (window.getSelection()?.toString()) return; // estava selecionando texto
    if (e.ctrlKey || e.metaKey) window.open(tr.dataset.href, '_blank');
    else location.href = tr.dataset.href;
});

// ==================== 11. VÁRIAS FOTOS ANTES DE SALVAR ====================
// Botão de foto (<input type="file" data-fotos="#lista">): cada foto tirada/escolhida SOMA às anteriores
// (antes a nova escolha substituía a outra) e aparece em miniatura na #lista até Salvar; o × tira antes de salvar.
// Cada escolha fica num campo próprio com o mesmo name, e o servidor recebe todas.
// Só vale foto tirada HOJE: a data vem de dentro da foto (EXIF que a câmera grava) ou, sem ela, da data do arquivo.
// Foto de outro dia é recusada com aviso; as aceitas mostram a data e a hora no canto e mandam essa hora junto
// (campo "<name>Hora"). O servidor confere de novo (Servicos/Fotos.cs → HoraDaFotoAsync).
const msDoisDigitos = n => String(n).padStart(2, '0');
const msDataHora = d => `${msDoisDigitos(d.getDate())}/${msDoisDigitos(d.getMonth() + 1)}/${d.getFullYear()} ${msDoisDigitos(d.getHours())}:${msDoisDigitos(d.getMinutes())}`;
const msDataIso = d => `${d.getFullYear()}-${msDoisDigitos(d.getMonth() + 1)}-${msDoisDigitos(d.getDate())}T${msDoisDigitos(d.getHours())}:${msDoisDigitos(d.getMinutes())}:${msDoisDigitos(d.getSeconds())}`;

// JPEG → bloco "Exif" → DateTimeOriginal (0x9003) ou DateTime (0x0132): "2026:10:01 08:42:13". Sem EXIF: null.
function msLerExif(v) {
    try {
        if (v.getUint16(0) !== 0xFFD8) return null;
        for (let p = 2; p + 10 < v.byteLength && v.getUint8(p) === 0xFF;) {
            const marca = v.getUint8(p + 1), tam = v.getUint16(p + 2);
            if (marca === 0xDA) break;
            if (marca === 0xE1 && v.getUint32(p + 4) === 0x45786966) {   // "Exif"
                const t = p + 10, le = v.getUint16(t) === 0x4949;
                const u16 = o => v.getUint16(t + o, le), u32 = o => v.getUint32(t + o, le);
                const campo = (ifd, tag) => { for (let i = 0; i < u16(ifd); i++) if (u16(ifd + 2 + i * 12) === tag) return ifd + 2 + i * 12; return null; };
                const texto = c => { const o = t + u32(c + 8); let s = ''; for (let i = 0; i < 19; i++) s += String.fromCharCode(v.getUint8(o + i)); return s; };
                const ifd0 = u32(4), exif = campo(ifd0, 0x8769);
                const original = exif !== null ? campo(u32(exif + 8), 0x9003) : null;
                const c = original ?? campo(ifd0, 0x0132);
                const m = c !== null && texto(c).match(/^(\d{4}):(\d\d):(\d\d) (\d\d):(\d\d):(\d\d)$/);
                return m && +m[1] > 2000 ? new Date(+m[1], m[2] - 1, +m[3], +m[4], +m[5], +m[6]) : null;
            }
            p += 2 + tam;
        }
    } catch { /* EXIF cortado ou estranho */ }
    return null;
}
async function msHoraDaFoto(f) {
    try {
        const exif = msLerExif(new DataView(await f.slice(0, 256 * 1024).arrayBuffer()));
        if (exif) return exif;
    } catch { }
    return new Date(f.lastModified || Date.now());
}

function msPrepararFotos(input) {
    input.addEventListener('change', async () => {
        if (!input.files.length) return;
        const lista = document.querySelector(input.dataset.fotos);
        const novo = input.cloneNode();   // campo vazio no botão, para a próxima foto
        novo.value = '';
        msPrepararFotos(novo);
        input.replaceWith(novo);

        // Separa as fotos de hoje das de outros dias.
        const hoje = new Date().toDateString();
        const fotos = await Promise.all([...input.files].map(async f => ({ f, hora: await msHoraDaFoto(f) })));
        let aceitas = fotos.filter(x => x.hora.toDateString() === hoje);
        const recusadas = fotos.filter(x => x.hora.toDateString() !== hoje);
        if (recusadas.length) {
            try {   // deixa no campo só as fotos de hoje
                const dt = new DataTransfer();
                aceitas.forEach(x => dt.items.add(x.f));
                input.files = dt.files;
            } catch { aceitas = []; }   // navegador antigo: não dá para tirar só algumas, então recusa a escolha toda
            const t = s => window.msT ? msT(s) : s;
            msAvisar(t('Só vale foto tirada hoje. Foto(s) recusada(s), tiradas em: {datas}.')
                .replace('{datas}', recusadas.map(x => msDataHora(x.hora)).join(', ')));
        }
        if (!aceitas.length) return;

        const grupo = document.createElement('div');
        grupo.className = 'fotos-novas';
        grupo.appendChild(input);         // o campo com as fotos vai junto no Salvar
        aceitas.forEach(({ f, hora }) => {
            const campoHora = document.createElement('input');
            campoHora.type = 'hidden';
            campoHora.name = input.name + 'Hora';
            campoHora.value = msDataIso(hora);
            grupo.appendChild(campoHora);

            const fig = document.createElement('figure');
            const quadro = document.createElement('span');
            quadro.className = 'foto-quadro';
            const img = document.createElement('img');
            img.src = URL.createObjectURL(f);
            img.alt = f.name;
            const etiqueta = document.createElement('span');
            etiqueta.className = 'foto-hora';
            etiqueta.textContent = msDataHora(hora);
            quadro.append(img, etiqueta);
            fig.appendChild(quadro);
            grupo.appendChild(fig);
        });
        const tirar = document.createElement('button');
        tirar.type = 'button';
        tirar.className = 'fotos-tirar';
        tirar.title = 'Tirar esta foto';
        tirar.textContent = '×';
        tirar.addEventListener('click', () => { grupo.remove(); msContarFotos(lista); });
        grupo.appendChild(tirar);
        lista.appendChild(grupo);
        msContarFotos(lista);
    });
}
function msContarFotos(lista) {
    const n = [...lista.querySelectorAll('input[type=file]')].reduce((s, i) => s + i.files.length, 0);
    const aviso = document.querySelector(`[data-fotos-aviso="#${lista.id}"]`);
    if (aviso) aviso.textContent = n ? `${n} foto(s) selecionada(s) — clique em Salvar` : '';
}
document.querySelectorAll('input[type=file][data-fotos]').forEach(msPrepararFotos);

// ==================== 12. CAIXA DE SELEÇÃO COM PESQUISA ====================
// <select data-pesquisa>: vira um campo onde se digita parte do código ou do nome ("aoi 1907") e a lista vai filtrando
// (sem diferença de acento ou maiúscula). Setas ↑↓ andam, Enter escolhe, Esc fecha. O <select> original fica escondido
// no formulário e recebe o valor (dispara "change", então quem escuta o select continua funcionando).
document.querySelectorAll('select[data-pesquisa]').forEach(sel => {
    const t = s => window.msT ? msT(s) : s;
    const normal = s => s.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase(); // tira acentos
    const textoDe = o => o.textContent.trim();
    const opcoes = [...sel.options].filter(o => o.value);

    const caixa = document.createElement('div');
    caixa.className = 'pesquisa-select';
    const campo = document.createElement('input');
    campo.type = 'text';
    campo.className = 'form-control';
    campo.autocomplete = 'off';
    campo.placeholder = 'Digite o código ou o nome para pesquisar';
    campo.id = sel.id + '_pesquisa';
    const lista = document.createElement('ul');
    lista.className = 'pesquisa-lista';
    lista.hidden = true;
    caixa.append(campo, lista);
    sel.after(caixa);
    document.querySelector(`label[for="${sel.id}"]`)?.setAttribute('for', campo.id);
    const obrigatorio = sel.required;
    sel.required = false; // a validação passa para o campo de pesquisa
    sel.hidden = true;

    let visiveis = [], ativo = -1;
    const validar = () => campo.setCustomValidity(obrigatorio && !sel.value ? t('Selecione o equipamento.') : '');
    const fechar = () => { lista.hidden = true; ativo = -1; };
    function marcar(i) {
        ativo = i;
        [...lista.children].forEach((li, j) => li.classList.toggle('ativo', j === i));
        lista.children[i]?.scrollIntoView({ block: 'nearest' });
    }
    function escolher(o) {
        sel.value = o.value;
        campo.value = textoDe(o);
        validar();
        fechar();
        sel.dispatchEvent(new Event('change', { bubbles: true }));
    }
    function filtrar(texto) {
        const termos = normal(texto).split(/\s+/).filter(Boolean);
        visiveis = opcoes.filter(o => termos.every(p => normal(textoDe(o)).includes(p)));
        lista.innerHTML = '';
        for (const o of visiveis) {
            const li = document.createElement('li');
            li.textContent = textoDe(o);
            li.addEventListener('mousedown', e => { e.preventDefault(); escolher(o); }); // antes do blur do campo
            lista.append(li);
        }
        if (!visiveis.length) {
            const li = document.createElement('li');
            li.className = 'vazio';
            li.textContent = 'Nenhum equipamento encontrado.';
            lista.append(li);
        }
        lista.hidden = false;
        const atual = visiveis.findIndex(o => o.value === sel.value);
        marcar(atual >= 0 ? atual : (visiveis.length ? 0 : -1));
    }

    campo.value = sel.value ? textoDe(sel.selectedOptions[0]) : '';
    validar();
    campo.addEventListener('focus', () => { campo.select(); filtrar(''); });   // ao entrar, mostra a lista toda
    campo.addEventListener('click', () => { if (lista.hidden) filtrar(''); }); // clicar de novo no campo reabre a lista
    campo.addEventListener('input', () => { sel.value = ''; validar(); filtrar(campo.value); });
    campo.addEventListener('keydown', e => {
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            e.preventDefault();
            if (lista.hidden) filtrar(campo.value);
            else if (visiveis.length) marcar((ativo + (e.key === 'ArrowDown' ? 1 : -1) + visiveis.length) % visiveis.length);
        } else if (e.key === 'Enter' && !lista.hidden) {
            e.preventDefault(); // Enter escolhe o equipamento em vez de enviar o formulário
            if (visiveis[ativo]) escolher(visiveis[ativo]);
        } else if (e.key === 'Escape' && !lista.hidden) {
            e.preventDefault();
            fechar();
        }
    });
    campo.addEventListener('blur', () => {
        fechar();
        // Saiu sem escolher: se só um equipamento bate com o texto, fica com ele; senão volta ao que estava escolhido.
        if (!sel.value && visiveis.length === 1 && campo.value.trim()) escolher(visiveis[0]);
        campo.value = sel.value ? textoDe(sel.selectedOptions[0]) : '';
        validar();
    });
});

// <input list="..." data-pesquisa>: a mesma lista com pesquisa, mas com texto livre — os nomes do <datalist> são
// sugestões (Equipamento da NRE: tipo de máquina). Clicar mostra todos, digitar filtra, e dá para escrever outro
// nome ou deixar em branco. Setas ↑↓ andam, Enter escolhe, Esc fecha.
document.querySelectorAll('input[list][data-pesquisa]').forEach(campo => {
    const normal = s => s.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase(); // tira acentos
    const nomes = [...(campo.list?.options || [])].map(o => o.value).filter(Boolean);
    campo.removeAttribute('list'); // sem a lista do próprio navegador por cima
    const caixa = document.createElement('div');
    caixa.className = 'pesquisa-select';
    const lista = document.createElement('ul');
    lista.className = 'pesquisa-lista';
    lista.hidden = true;
    campo.before(caixa);
    caixa.append(campo, lista);

    let visiveis = [], ativo = -1;
    const fechar = () => { lista.hidden = true; ativo = -1; };
    function marcar(i) {
        ativo = i;
        [...lista.children].forEach((li, j) => li.classList.toggle('ativo', j === i));
        lista.children[i]?.scrollIntoView({ block: 'nearest' });
    }
    function escolher(nome) {
        campo.value = nome;
        fechar();
        campo.dispatchEvent(new Event('change', { bubbles: true }));
    }
    // Ao entrar, mostra todos (marcando o que já está escrito); digitando, filtra e marca o primeiro que bate.
    function filtrar(texto) {
        const termos = normal(texto).split(/\s+/).filter(Boolean);
        visiveis = nomes.filter(n => termos.every(p => normal(n).includes(p)));
        lista.innerHTML = '';
        for (const n of visiveis) {
            const li = document.createElement('li');
            li.textContent = n;
            li.addEventListener('mousedown', e => { e.preventDefault(); escolher(n); }); // antes do blur do campo
            lista.append(li);
        }
        if (!visiveis.length) {
            const li = document.createElement('li');
            li.className = 'vazio';
            li.textContent = 'Nenhum equipamento na lista: fica o nome digitado.';
            lista.append(li);
        }
        lista.hidden = false;
        const igual = visiveis.findIndex(n => normal(n) === normal(campo.value.trim()));
        marcar(igual >= 0 ? igual : (termos.length && visiveis.length ? 0 : -1));
    }

    campo.addEventListener('focus', () => { campo.select(); filtrar(''); });
    campo.addEventListener('click', () => { if (lista.hidden) filtrar(''); });
    campo.addEventListener('input', () => filtrar(campo.value));
    campo.addEventListener('keydown', e => {
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            e.preventDefault();
            if (lista.hidden) filtrar(campo.value);
            else if (visiveis.length) marcar((ativo + (e.key === 'ArrowDown' ? 1 : -1) + visiveis.length) % visiveis.length);
        } else if (e.key === 'Enter' && !lista.hidden) {
            e.preventDefault(); // Enter escolhe em vez de enviar o formulário
            if (visiveis[ativo]) escolher(visiveis[ativo]); else fechar();
        } else if (e.key === 'Escape' && !lista.hidden) {
            e.preventDefault();
            fechar();
        }
    });
    campo.addEventListener('blur', () => {
        fechar();
        // Escreveu um nome que está na lista (outra maiúscula / sem acento): fica igual ao da lista.
        const igual = nomes.find(n => normal(n) === normal(campo.value.trim()));
        if (igual) campo.value = igual;
    });
});

// ==================== 13. DICA DOS GRÁFICOS ====================
// Mouse (ou foco do teclado) num elemento com data-dica: mostra um balão em cima dele com o conteúdo do
// <span class="dica-conteudo" hidden> que está dentro (já traduzido junto com a página). Ex.: meses do gráfico de
// cumprimento do Dashboard. Escuta no documento, então continua valendo depois que o painel atualiza sem recarregar.
(function () {
    let balao, alvo;
    function mostrar(el) {
        const conteudo = el.querySelector('.dica-conteudo');
        if (!conteudo) return;
        if (!balao) {
            balao = document.createElement('div');
            balao.className = 'dica-grafico';
            balao.setAttribute('role', 'tooltip');
            document.body.append(balao);
        }
        alvo = el;
        balao.innerHTML = conteudo.innerHTML;
        balao.hidden = false;
        // Em cima do elemento, centralizado; sem espaço em cima, embaixo. Nunca sai pelas laterais da tela.
        const r = el.getBoundingClientRect(), b = balao.getBoundingClientRect();
        const x = Math.max(8, Math.min(r.left + r.width / 2 - b.width / 2, document.documentElement.clientWidth - b.width - 8));
        const y = r.top - b.height - 8 >= 8 ? r.top - b.height - 8 : r.bottom + 8;
        balao.style.left = x + 'px';
        balao.style.top = y + 'px';
    }
    function esconder() {
        if (balao) balao.hidden = true;
        alvo = null;
    }
    document.addEventListener('mouseover', e => {
        const el = e.target.closest('[data-dica]');
        if (el && el !== alvo) mostrar(el);
        else if (!el && alvo) esconder();
    });
    document.addEventListener('mouseout', e => { if (!e.relatedTarget) esconder(); }); // saiu da janela
    document.addEventListener('focusin', e => { const el = e.target.closest('[data-dica]'); if (el) mostrar(el); else esconder(); });
    document.addEventListener('click', esconder);          // clicou: o painel vai mudar, a dica some
    window.addEventListener('scroll', esconder, true);
})();

// ==================== 14. MOTIVO DO RETORNO ====================
// Botão [data-retorno] — "Retornada" na coluna Retorno da lista de OS e "Motivo do retorno" no cartão do celular:
// abre uma janela com a OS, quem retornou, quando e o motivo (data-os, data-quem, data-quando, data-motivo).
// O clique não abre a OS (a linha/cartão clicável ignora botões — seção 10).
document.addEventListener('click', e => {
    const b = e.target.closest('[data-retorno]');
    if (!b) return;
    e.preventDefault();
    const t = s => window.msT ? msT(s) : s;
    const caixa = document.createElement('div');
    caixa.className = 'retorno-janela';
    caixa.dataset.semTraducao = '';
    const linha = (rotulo, valor) => {
        const p = document.createElement('div');
        p.className = 'retorno-linha';
        const r = document.createElement('span');
        r.textContent = t(rotulo);
        const v = document.createElement('b');
        v.textContent = valor || '-';
        p.append(r, v);
        return p;
    };
    const motivo = document.createElement('div');
    motivo.className = 'retorno-motivo';
    motivo.textContent = b.dataset.motivo;
    const dica = document.createElement('p');
    dica.className = 'small text-muted mt-2 mb-0';
    dica.textContent = t('Refaça o que o coordenador pediu e conclua a OS de novo. O motivo sai daqui quando a OS for concluída (fica no Histórico).');
    caixa.append(linha('Ordem de serviço', b.dataset.os), linha('Retornada por', b.dataset.quem), linha('Retornada em', b.dataset.quando), motivo, dica);
    msAvisar(caixa, t('Motivo do retorno'));
});

// ==================== 15. VOLTAR PARA A LISTA COM O FILTRO ====================
// Pedido do usuário em 20/10/2026. A lista guarda o filtro dela num cookie (o servidor usa ao voltar de um registro:
// Servicos/VoltaParaLista.cs) e a rolagem na sessão do navegador. Chegando com #voltou, volta para a mesma rolagem.
// O menu lateral abre a lista limpa (não passa pelo ?volta=1).
(function () {
    const m = location.pathname.match(/^\/(equipamentos|cronograma|ordens|finalizacao|nre|spareparts|usuarios|historicojms|grupos)(\/index)?\/?$/i);
    if (!m) return;
    const pasta = m[1].toLowerCase();
    const chaveRolagem = 'ms-rolagem:' + pasta + location.search;
    const guardar = () => {
        document.cookie = `ms-lista-${pasta}=${encodeURIComponent(location.search)}; path=/; samesite=lax`;
        try { sessionStorage.setItem(chaveRolagem, String(Math.round(window.scrollY))); } catch { }
    };
    // A rolagem guardada é lida ANTES de guardar a de agora (que ainda é o topo).
    if (location.hash === '#voltou') {
        history.replaceState(history.state, '', location.pathname + location.search);
        let y = 0;
        try { y = parseInt(sessionStorage.getItem(chaveRolagem) || '0', 10) || 0; } catch { }
        if (y) window.addEventListener('load', () => window.scrollTo(0, y));
    }
    document.cookie = `ms-lista-${pasta}=${encodeURIComponent(location.search)}; path=/; samesite=lax`;
    window.addEventListener('pagehide', guardar);
    document.addEventListener('click', guardar, true);   // antes de abrir um registro
})();
