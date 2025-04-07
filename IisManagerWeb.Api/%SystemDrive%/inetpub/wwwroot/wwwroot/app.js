window.applyMasks = function () {
    // Aplica a máscara de CPF
    document.querySelectorAll('.mask-cpf').forEach(function (element) {
        console.log("aplicando mask cpf");
        IMask(element, {
            mask: '000.000.000-00'
        });
    });

    // Aplica a máscara de CNPJ
    document.querySelectorAll('.mask-cnpj').forEach(function (element) {
        IMask(element, {
            mask: '00.000.000/0000-00'
        });
    });

    // Aplica a máscara de CEP
    document.querySelectorAll('.mask-cep').forEach(function (element) {
        console.log("aplicando mask cep");
        IMask(element, {
            mask: '00.000-000'
        });
    });

    document.querySelectorAll('.mask-telefone').forEach(function (element) {
        console.log("aplicando mask telefone");
        IMask(element, {
            mask: [
                {
                    mask: '(00) 0000-0000', // Para telefones fixos com 8 dígitos
                },
                {
                    mask: '(00) 00000-0000', // Para celulares com 9 dígitos
                }
            ]
        });
    });
};
window.setAuthCookie = function (loginRequest) {
    return fetch('/api/Cookie/SetAuthCookie', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: loginRequest
    });
}