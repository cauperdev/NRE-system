// ============================================================================
// OLHO NA SENHA (pedido do usuário em 24/10/2026) — todo campo de senha ganha um botão com um olho, à direita:
// clicar mostra a senha digitada, clicar de novo esconde. Vale no Login e no cadastro de Usuários.
// Carregado antes do traducao.js (Shared/_Layout.cshtml e Conta/Login.cshtml), que traduz o "Mostrar senha".
// Estilos: estilos.css "Olho na senha".
// ============================================================================
(function () {
    const olho = "<path d='M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z'/><circle cx='12' cy='12' r='3'/>";
    const olhoRiscado = "<path d='M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94'/>" +
        "<path d='M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19'/>" +
        "<path d='M14.12 14.12a3 3 0 1 1-4.24-4.24'/><line x1='1' y1='1' x2='23' y2='23'/>";
    const svg = d => `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${d}</svg>`;
    const tr = s => (window.msT ? window.msT(s) : s);

    document.querySelectorAll('input[type="password"]').forEach(campo => {
        const caixa = document.createElement('span');
        caixa.className = 'campo-senha';
        campo.parentNode.insertBefore(caixa, campo);
        caixa.appendChild(campo);

        const botao = document.createElement('button');
        botao.type = 'button';
        botao.className = 'btn-ver-senha';
        botao.tabIndex = -1;   // o Tab vai do campo de senha direto para o próximo campo / Entrar
        function mostrar(visivel) {
            campo.type = visivel ? 'text' : 'password';
            botao.innerHTML = svg(visivel ? olhoRiscado : olho);
            const rotulo = visivel ? 'Esconder senha' : 'Mostrar senha';
            botao.title = tr(rotulo);
            botao.setAttribute('aria-label', tr(rotulo));
            botao.setAttribute('aria-pressed', visivel ? 'true' : 'false');
        }
        botao.addEventListener('click', () => {
            const fim = campo.selectionEnd;
            mostrar(campo.type === 'password');
            campo.focus();
            try { campo.setSelectionRange(fim, fim); } catch (e) { }   // cursor continua onde estava
        });
        // Ao enviar o formulário a senha volta a ficar escondida (o navegador não guarda como texto).
        campo.form?.addEventListener('submit', () => mostrar(false));
        mostrar(false);
        caixa.appendChild(botao);
    });
})();
