## TODOS:
### Legenda
- [FEITO] [DIFICULDADE] [PRIORIDADE] Descrição

### Correção de Bugs
#### Animação Blazor na hora de carregar a página
- [X] [2] [4] Readicionar app.css ao projeto
- [X] [1] [4] Arrumar a animação de carregamento

#### Dashboard
- [X] [2] [4] Implementar limite de datapoints visíveis
- [ ] [5] [5] Chart está quebrando com erro desconhecido

#### Estatísticas de Sites
- [ ] [1] [5] Identificar o motivo das estatísticas não serem exibidas
- [ ] [1] [5] Verificar a conexão com a API que retorna as estatísticas
- [ ] [2] [5] Corrigir a exibição das estatísticas por site

### Gerenciamento de Arquivos
#### Atualização de Arquivos
- [X] [1] [5] Identificar por que todos os arquivos estão sendo atualizados
- [X] [1] [5] Arrumar bug que está atualizando a data hora dos arquivos
- [ ] [3] [5] Quando atualização por grupo, validar os arquivos site por site e não só o primeiro

#### Feedback ao Usuário
- [X] [1] [2] Exibir mensagem de sucesso ou falha após a operação de enviar arquivos modificados
- [X] [1] [3] Ajustar interface para listar arquivos a serem atualizados, de um lado arquivos do servidor, do outro o arquivo correspondente que vai ser atualizado (em ambas as listas nome e data de modificação)
- [X] [1] [3] Exibir o motivo de por que cada arquivo será atualizado (se o arquivo não existe no servidor ou se a data de modificação está diferente)
- [X] [1] [2] Mostrar em listagem separada arquivos ignorados que estão no cliente e não vão ser enviados para o serivdor

### Gerenciamento de Configurações
#### Handler de Configurações
- [ ] [1] [4] Criar serviço para monitorar alterações no arquivo de configurações
- [ ] [2] [4] Implementar padrão observer para notificação de alterações
- [ ] [2] [4] Desenvolver mecanismo de recarga de configurações nos clientes

#### Certificados
- [ ] [1] [3] Desenvolver interface para configuração de certificados por site
- [ ] [2] [3] Implementar lógica para gerenciar certificados individuais
- [ ] [2] [3] Criar interface para agrupar sites
- [ ] [2] [3] Implementar lógica para aplicar certificados a grupos de sites

#### Protocolos e Bindings
- [ ] [1] [3] Criar interface para edição de protocolo (HTTP/HTTPS)
- [ ] [1] [3] Implementar lógica para alteração segura de protocolos
- [ ] [2] [3] Desenvolver interface de configuração de bindings
- [ ] [2] [3] Implementar validação de configurações de binding

#### Web Config
- [ ] [1] [3] Criar editor de web.config com highlighting
- [ ] [2] [3] Implementar parser para web.config
- [ ] [2] [3] Desenvolver interface gráfica para edição de seções comuns
- [ ] [2] [2] Criar validador de sintaxe para web.config
- [ ] [1] [2] Adicionar verificação de erros comuns

### Monitoramento e Scripts
- [ ] [1] [3] Desenvolver interface para criação de scripts customizados
- [ ] [2] [3] Implementar executor seguro de scripts
- [ ] [2] [3] Criar biblioteca de funções comuns para scripts
- [ ] [1] [3] Desenvolver sistema de alertas baseado nos resultados dos scripts

### Conectividade
- [ ] [1] [2] Criar interface para gerenciar múltiplas conexões
- [ ] [2] [2] Implementar pool de conexões a servidores
- [ ] [2] [2] Desenvolver mecanismo de balanceamento de requisições
- [ ] [1] [2] Adicionar dashboard consolidado para múltiplos servidores

### Expansão de Plataforma
#### Suporte Apache
- [ ] [2] [1] Pesquisar APIs e comandos do Apache equivalentes aos do IIS
- [ ] [3] [1] Implementar camada de abstração para operações comuns
- [ ] [3] [1] Desenvolver adaptadores específicos para Apache

#### Suporte Linux
- [ ] [2] [1] Identificar dependências específicas do Windows
- [ ] [3] [1] Remover ou substituir APIs exclusivas do Windows
- [ ] [3] [1] Implementar camada de abstração para sistema operacional
- [ ] [2] [1] Criar pipeline de CI/CD para Linux

### Banco de Dados
#### Interface de Banco de Dados
- [ ] [1] [2] Criar categoria visual para banco de dados no menu
- [ ] [2] [2] Desenvolver interface para listar bancos disponíveis
- [ ] [2] [2] Implementar telas para gestão de conexões

#### Conectividade com Banco
- [ ] [1] [2] Desenvolver serviço de conexão a diferentes tipos de bancos

#### Monitoramento de Banco
- [ ] [1] [2] Implementar verificação de status do banco de dados
- [ ] [2] [2] Criar dashboard com métricas de saúde do banco

#### Replicação
- [ ] [1] [1] Pesquisar métodos de replicação para diferentes bancos
- [ ] [3] [1] Implementar interface para configuração de replicação
- [ ] [3] [1] Desenvolver monitor de status da replicação
- [ ] [2] [1] Criar ferramentas para resolução de conflitos

### Notificações
#### Telegram
- [ ] [2] [2] Desenvolver sistema de alerta para problemas no banco
- [ ] [2] [2] Desenvolver sistema de alerta para problemas no iis
- [ ] [2] [2] Desenvolver sistema de alerta para problemas no apache
