# Moonlight Launcher 🌙

Launcher de Minecraft Java para Windows, feito em C# + WPF + CmlLib.Core.

## Novidades da versão 1.8.9 — Compartilhar tela de verdade 🖥️📞

- **Janelinha "Configurar compartilhamento"** ao clicar em 🖥: resolução (**4K, 1080p, 720p, 480p**), quadros por segundo (**15, 30, 60**) e "Compartilhar áudio". **1080p, 4K e 60 fps só com o Golden Moon**: sem ele essas opções mostram um 🔒 e levam para a aba Golden Moon. Sem o Golden Moon o máximo é 720p e 30 fps.
- **Janelinha "Escolha o que compartilhar"**: abas **Janela** (com miniatura e ícone de cada janela aberta) e **Tela inteira** (um cartão por monitor). Duplo clique compartilha na hora. Se a janela compartilhada for fechada, a transmissão para sozinha. Janelas de jogo que saem pretas na captura são copiadas direto da tela.
- **Quem assiste não fica com a tela tomada:** a transmissão aparece em **metade** do launcher (a conversa continua à vista) e o botão **⛶ Tela inteira** abre em tela cheia; lá, **◧ Voltar para metade** (ou Esc) volta.
- **Supressão de ruído do microfone** nas chamadas (ligada por padrão; em ⚙ dá para desligar). Tira ventilador, chiado e barulho de fundo sem biblioteca extra (`NoiseSuppressor.cs`).
- **Foto de quem fala na barrinha da chamada** (a que fica por cima de tudo, até do Minecraft): aparece o rosto de quem está falando, com anel verde; se os dois falam, aparecem os dois. Sem ninguém falando, fica só o ícone da chamada.
- A captura agora poupa CPU e internet: pula quadros quando a rede ainda está enviando o anterior (o fps real cai sozinho, a imagem nunca atrasa).

## Novidades da versão 1.8.8 — PS1 secreto + conquista 🏆🎮

- **O botão PS1 só aparece depois do código secreto.** Digitar o código na aba Golden Moon libera o tema pela primeira vez; o botão (RGB animado) surge ao lado do Midnight em **Ajustes → Personalizar → Temas**. Quem já usava o PS1 continua com o botão.
- **Conquista desbloqueada:** painel no alto da tela com uma linha reta em RGB, cores correndo da esquerda para a direita, brilho branco passando e um som de item raro (`Assets/ps1/achievement.wav`, original).
- Com o tema já liberado, digitar o código de novo liga/desliga o PS1, como antes.

## Novidades da versão 1.8.7 — Logo do PS1 limpo 🎮🌙

- **Quadrado sumiu de vez:** era o brilho roxo atrás do logo, que o WPF cortava num retângulo. O brilho foi removido.
- **Logo parado:** sem brilho pulsando, sem giro e sem brilhinhos. Ele só entra na tela na abertura do tema.
- **Novo logo** (emblema pixelado) com fundo transparente.

## Novidades da versão 1.8.5 — Tema PS1 sem quadrado e sem travar 🎮🌙

- **Botão PS1 nos temas:** em **Ajustes → Personalizar → Temas** o botão **🎮 PS1** fica ao lado do Midnight, com a cor em **RGB animado**. Clicar liga o tema com a abertura do console; escolher outro tema desliga. O código secreto continua funcionando e o botão acompanha.
- **Quadrado em volta do logo sumiu:** o logo girava dentro de um `Viewport3D`, que numa janela com `AllowsTransparency` era desenhado por software e aparecia numa caixa escura. Agora é uma imagem 2D com giro falso (largura + inclinação).
- **Fundo animado:** losangos coloridos subindo devagar atrás do menu e das abas (some no modo Leve), além do céu que desliza.
- **Muito mais leve:** sem `DropShadowEffect` nos botões do menu, scanlines/vinheta e brilho do logo em cache de bitmap com pincéis congelados, céu em cache, animações ambientes a 30 quadros e imagem do logo decodificada num tamanho menor.

## Novidades da versão com a interface nova 🌙

- **Interface refeita:** barra lateral, céu estrelado com estrelas piscando, auroras, estrelas cadentes e efeito de profundidade com o mouse.
- **Abertura:** só a lua do launcher na tela; ela pisca e o launcher abre.
- **Ao clicar em Jogar:** o launcher treme, fica branco e some enquanto o Minecraft abre. Quando o jogo fecha ele volta.
- **Skin em 3D:** na aba Skins e na tela Jogar, o boneco da conta aparece em 3D e a cabeça acompanha o mouse (como no inventário do jogo). Sem skin, aparece um boneco lunar.
- **OptiFine automático:** nos modos *OptiFine* e *Forge + OptiFine* a lista mostra as versões que têm OptiFine. Ao clicar em Jogar, o OptiFine da versão exata é baixado e instalado sozinho (sem perguntar). O botão "Usar meu .jar" ficou só como opção avançada.
- **Aba Ajustes (otimização):** modo Automático / Completo / Leve, memória em uso, "Liberar memória agora" e "Limpar cache". Minimizado ou jogando, as animações e o 3D param por completo.

## Novidades da versão 1.7.0 — Chamadas com os amigos 📞🌙

- **Ligar para amigos:** botão **📞** no topo da conversa. O amigo recebe uma janelinha com o rosto da skin, toque e botões **Atender / Recusar** (funciona até com o Minecraft aberto). Respeita o **Não perturbe**.
- **Painel da chamada (estilo Discord):** aparece no alto da conversa, com os dois rostos redondos, anel verde de quem está falando e selo vermelho de microfone desligado. Botões: 🎙 mudo, 🎧 surdo, 🖥 compartilhar tela, ⚙ volumes e 📞 desligar.
- **Compartilhar tela com som:** o amigo vê sua tela (e pode abrir em tela cheia). Em ⚙ dá para incluir ou não o **som do PC**. A captura só liga quando o amigo está assistindo. Use fones para ele não ouvir a própria voz de volta.
- **Barrinha da chamada:** se você sair da conversa ou o jogo abrir, uma barrinha por cima de tudo mostra o tempo, mudo e desligar (duplo clique abre a conversa).
- **Histórico no chat:** "Você iniciou uma chamada", "Chamada encerrada · 5 min", "Chamada perdida"…
- **Precisa do servidor atualizado:** troque os arquivos do seu serviço no Render pelos da pasta `servidor-amigos/` (agora ele também faz as chamadas em `/call`). Amigos, chat e mundos continuam iguais.
- Novo pacote: **NAudio** (áudio). O GitHub Actions baixa sozinho.

## Novidades da versão 1.6.3 — Atualização e amigos corrigidos 🌙🔧

- **Atualizações:** a verificação não é mais pulada quando o launcher está ocupado na abertura (ela insiste até conseguir e depois confere de 15 em 15 min). Se a API do GitHub falhar, usa o site como plano B. Aceita tag `v1.6.3`/`1.6.3`, escolhe a maior Release (mesmo se a última for "pre-release") e aceita o `.exe` mesmo se o GitHub renomear o arquivo. O download tem 3 tentativas e sem limite de 60 s. A trava contra loop não bloqueia mais uma versão cuja troca falhou. **Ajustes → Atualizações → Verificar** agora diz exatamente o motivo (sem Release, Release sem `.exe`, versão igual, etc.).
- **Amigos:** janelinha **"ficou online"** e **"entrou no Minecraft"** (respeita Não perturbe e o som). O mundo aberto para LAN agora também é detectado pelo aviso UDP do próprio Minecraft (funciona em qualquer versão/idioma) e pelo texto do log mesmo sem `[CHAT]`. Quando você sai do mundo, ele deixa de ser compartilhado sozinho. Na lista aparece "🎮 Jogando Minecraft x.y" quando o servidor repassa o campo `game`.

## Novidades da versão 1.6.3 — Atualização e amigos corrigidos 🌙🔧

- **Atualizações:** a verificação não é mais pulada quando o launcher está ocupado na abertura (ela insiste até conseguir e depois confere de 15 em 15 min). Se a API do GitHub falhar, usa o site como plano B. Aceita tag `v1.6.3`/`1.6.3`, escolhe a maior Release (mesmo se a última for "pre-release") e aceita o `.exe` mesmo se o GitHub renomear o arquivo. O download tem 3 tentativas e sem limite de 60 s. A trava contra loop não bloqueia mais uma versão cuja troca falhou. **Ajustes → Atualizações → Verificar** agora diz exatamente o motivo (sem Release, Release sem `.exe`, versão igual, etc.).
- **Amigos:** janelinha **"ficou online"** e **"entrou no Minecraft"** (respeita Não perturbe e o som). O mundo aberto para LAN agora também é detectado pelo aviso UDP do próprio Minecraft (funciona em qualquer versão/idioma) e pelo texto do log mesmo sem `[CHAT]`. Quando você sai do mundo, ele deixa de ser compartilhado sozinho. Na lista aparece "🎮 Jogando Minecraft x.y" quando o servidor repassa o campo `game`.

## Novidades da versão 1.6.2

- O launcher agora conecta por padrão no servidor dos amigos em `wss://moonlight-amigos.onrender.com` (`FriendsConfig.DefaultRelay`). Para testar outro servidor sem gerar o .exe, crie `%AppData%\MoonlightLauncher\relay.txt` com o endereço.

## Novidades da versão 1.6.1 — Amigos de verdade 🌙🔧

- **Servidor dos amigos incluído** (pasta `MoonlightServidor/`, fora do launcher): sem ele o botão de amigos não tem com quem falar. Zero dependências, sobe no Render em 2 minutos (leia o `LEIA-ME.md` dele). O endereço padrão continua em `FriendsConfig.DefaultRelay`.
- **Corrigido:** o `servers.dat` ficava corrompido quando a lista de servidores do Minecraft estava vazia (os mundos dos amigos não apareciam no Multijogador).
- **Corrigido:** entradas `[Moonlight]` antigas ficavam no Multijogador depois de fechar o launcher; agora saem ao abrir e ao fechar.
- **Mais seguro:** o launcher só abre o seu mundo para quem está na sua lista de amigos, mesmo que o servidor peça outra coisa.

## Novidades da versão 1.6 — Amigos com cara de app de mensagens 💬🌙

- **Conexão automática:** assim que você cria uma conta (offline/Moonlight ou Microsoft), o launcher já entra no servidor dos amigos. Não existe campo para colar endereço nem botão "Conectar". Sem conta, a tela mostra "Crie uma conta para entrar".
- **Tela de amigos redesenhada (estilo WhatsApp):** lista de conversas com rosto redondo, última mensagem, hora e contador de não lidas; pesquisa nas conversas; chat com balões, fundo escurecido, campo arredondado e botão redondo de enviar.
- **➕ Novo amigo:** painel próprio (por código `MOON-…` ou nome) com resultados, pedidos de amizade na própria lista e o seu código para copiar.
- **⋯ Menu:** código de amigo, Não perturbe, som e **Meu mundo** ficam juntos num painel que abre e fecha.
- O endereço do servidor continua em `FriendsConfig.DefaultRelay` (`FriendsData.cs`); troque só se o endereço do seu site no Render for outro.

## Novidades da versão 1.5 — Amigos 👥🌙

Na tela **Jogar** há o botão **👥 Adicionar amigos**.

- **Adicionar:** por código (`MOON-XXXXX-XXXXX`) ou pelo nome, com conta Microsoft ou offline. Aparece o **rosto da skin** de cada pessoa.
- **Mundos dos amigos:** quando um amigo abre o mundo dele (Abrir para LAN), ele aparece na lista e no menu **Multijogador** do Minecraft. O botão **▶ Entrar no mundo** abre o jogo na versão certa e já conecta.
- **Mods:** se o mundo usa mods, o launcher avisa e pergunta se você quer baixar todos (pelo Modrinth) antes de abrir o Minecraft.
- **Chat** pelo launcher, com **janelinha** no canto da tela (funciona até com o jogo aberto), som agradável e **Não perturbe** (sem janelinha e sem som).
- Funciona como um "Radmin" sem VPN: o tráfego passa pelo **servidor dos amigos**, que roda dentro do próprio site do launcher (o endereço já vem fixo em `FriendsConfig.DefaultRelay`, arquivo `FriendsData.cs`). Ninguém precisa configurar nada.

## Aba Mods renovada 🌙

- **Barra de ferramentas:** menu **Ordenar** (Relevância, Mais baixados, Mais seguidos, Mais novos, Atualizados) e botão **💡 Sugestões** que abre uma janelinha com todos os temas. A lista de resultados ganhou o espaço todo.
- **Modo Lista / Detalhado** (o botão lembra a sua escolha). O **Detalhado** mostra cartões com uma faixa fina de **banner** colada no topo, foto do mod, autor, descrição, etiquetas, downloads, seguidores e última atualização.
- **Sem banner?** O launcher usa a cor do mod (Modrinth) ou analisa a foto e escolhe a cor dominante. Essa cor também aparece na barrinha da lista e nas etiquetas.

## Novidades da versão 1.3 🌙🎨

- **Ajustes com sub-abas:** **⚙ Geral** (desempenho, memória e cache) e **🎨 Personalizar**, que reúne **Temas**, **Paletas de cores**, **Ícones**.
- **Mais leve:** o contador de memória só roda na sub-aba Geral, e o build ganhou compilação rápida e sem símbolos de depuração.

## Novidades da versão 1.1 🌙✨

- **Temas (Ajustes):** Padrão (o de sempre), Claro (branco suave), Escuro (cinza escuro) e Midnight (preto). Fundo, cartões, listas, abas e botões se adaptam.
- **Golden Moon (aba no canto inferior esquerdo):** mostra os benefícios e ativa a assinatura por código. Assinantes liberam **2 paletas de cores** (em Ajustes) que formam um degradê no launcher inteiro.
- **Modpacks (Mods → Meus modpacks):** crie um modpack (nome, versão, loader), adicione mods pela busca ("No modpack") e clique em **Jogar com este modpack**. Os mods dele entram na pasta mods só enquanto o jogo está aberto; a pasta original volta sozinha.
- **Criador de skin (Skins → Criador de skin):** textura 2D com guias + boneco 3D ao vivo, lápis, borracha, balde, conta-gotas, linha, retângulos, clarear/escurecer, ruído, espelhar braço/perna, camadas, desfazer/refazer.
- **OptiFine / Forge + OptiFine:** as versões 26.1, 26.1.1, 26.1.2, 26.2 e 26.3 sempre aparecem; se o OptiFine ainda não existir para a versão, o launcher avisa e baixa quando sair.
- **Mods:** sugestões por tema (vários ao mesmo tempo), seletor de versão digitando ou pela seta (lista só o que cada loader suporta) e resultados em páginas (Aba 1, Aba 2, Aba 3…).

### Configurar o Golden Moon
1. Em `Theme.cs`, troque `GoldenMoon.Secret` por um segredo só seu e `SubscribeUrl` pelo link da sua página de assinatura.
2. Abra `tools/gerar-codigo-golden.html` no navegador, use o mesmo segredo e gere os códigos.
3. Entregue um código para cada assinante. Ele cola na aba Golden Moon e ativa (funciona sem internet).

> Atenção: a verificação é local. Quem decompilar o `.exe` consegue achar o segredo; para uma cobrança de verdade, o ideal é validar a assinatura em um servidor (ex.: o seu no Render).

## Gerar o executável no Windows

Com o .NET 8 SDK instalado, abra o terminal nesta pasta e execute:

```powershell
dotnet publish MoonlightLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

O arquivo será gerado em:

`bin\Release\net8.0-windows\win-x64\publish\MoonlightLauncher.exe`

## Gerar pelo GitHub Actions (sem instalar .NET no seu PC)

1. Envie o CONTEÚDO da pasta (MoonlightLauncher.csproj, .github, Assets…) para a raiz de um repositório GitHub.
2. Abra a aba **Actions** do repositório.
3. Selecione **Build Moonlight Launcher (Windows)** e clique em **Run workflow** (ou aguarde o build automático após enviar código para `main`/`master`).
4. Quando terminar, abra a execução e baixe o artefato `MoonlightLauncher-Windows-x64`.
5. Extraia o ZIP do artefato. Dentro estará `MoonlightLauncher.exe`.

O build usa Windows porque o projeto é WPF. O executável é publicado como self-contained, então não exige que o usuário final instale o .NET Desktop Runtime.

## Publicar uma atualização (os jogadores recebem o aviso sozinhos)

As Releases ficam no repositório **Moonlight-Downloads** (público). O launcher olha a última Release de lá.

1. Envie o código novo com o GitHub Desktop (Commit + Push).
2. Na aba **Actions** do repositório do código: **Build Moonlight Launcher (Windows) → Run workflow** e, no campo *Versao*, digite a nova versão (ex.: `1.4.0`, sempre maior que a anterior). Aguarde terminar e baixe o artefato `MoonlightLauncher-Windows-x64`; extraia o ZIP.
3. Em **Moonlight-Downloads → Releases → Draft a new release**: em *Choose a tag* digite `v1.4.0` (a MESMA versão do passo 2), escreva as novidades, arraste o `MoonlightLauncher.exe` (nome exato) para a área de arquivos e clique em **Publish release**.
4. Pronto: quem abrir (ou já estiver com o launcher aberto) vê "Nova versão disponível", uma barra de download e o launcher reinicia sozinho. Em **Ajustes → Geral → Atualizações** dá para ligar a atualização 100% automática ou verificar na hora. Nenhum arquivo extra fica no PC.

> Se a versão digitada no passo 2 for diferente da tag do passo 3, o launcher vai achar que ainda está desatualizado e oferecer a atualização de novo.
> Quem já tem uma versão **sem** esse sistema precisa baixar o `.exe` novo uma única vez; depois disso as atualizações são automáticas.

## Aba Mods renovada 🌙

- **Barra de ferramentas:** menu **Ordenar** (Relevância, Mais baixados, Mais seguidos, Mais novos, Atualizados) e botão **💡 Sugestões** que abre uma janelinha com todos os temas. A lista de resultados ganhou o espaço todo.
- **Modo Lista / Detalhado** (o botão lembra a sua escolha). O **Detalhado** mostra cartões com uma faixa fina de **banner** colada no topo, foto do mod, autor, descrição, etiquetas, downloads, seguidores e última atualização.
- **Sem banner?** O launcher usa a cor do mod (Modrinth) ou analisa a foto e escolhe a cor dominante. Essa cor também aparece na barrinha da lista e nas etiquetas.

## Novidades da versão 1.3 🌙🎨

- **Ajustes com sub-abas:** **⚙ Geral** (desempenho, memória e cache) e **🎨 Personalizar**, que reúne **Temas**, **Paletas de cores**, **Ícones**.
- **Mais leve:** o contador de memória só roda na sub-aba Geral, e o build ganhou compilação rápida e sem símbolos de depuração.

## Novidades da versão 1.1 🌙✨

- **Temas (Ajustes):** Padrão (o de sempre), Claro (branco suave), Escuro (cinza escuro) e Midnight (preto). Fundo, cartões, listas, abas e botões se adaptam.
- **Golden Moon (aba no canto inferior esquerdo):** mostra os benefícios e ativa a assinatura por código. Assinantes liberam **2 paletas de cores** (em Ajustes) que formam um degradê no launcher inteiro.
- **Modpacks (Mods → Meus modpacks):** crie um modpack (nome, versão, loader), adicione mods pela busca ("No modpack") e clique em **Jogar com este modpack**. Os mods dele entram na pasta mods só enquanto o jogo está aberto; a pasta original volta sozinha.
- **Criador de skin (Skins → Criador de skin):** textura 2D com guias + boneco 3D ao vivo, lápis, borracha, balde, conta-gotas, linha, retângulos, clarear/escurecer, ruído, espelhar braço/perna, camadas, desfazer/refazer.
- **OptiFine / Forge + OptiFine:** as versões 26.1, 26.1.1, 26.1.2, 26.2 e 26.3 sempre aparecem; se o OptiFine ainda não existir para a versão, o launcher avisa e baixa quando sair.
- **Mods:** sugestões por tema (vários ao mesmo tempo), seletor de versão digitando ou pela seta (lista só o que cada loader suporta) e resultados em páginas (Aba 1, Aba 2, Aba 3…).

### Configurar o Golden Moon
1. Em `Theme.cs`, troque `GoldenMoon.Secret` por um segredo só seu e `SubscribeUrl` pelo link da sua página de assinatura.
2. Abra `tools/gerar-codigo-golden.html` no navegador, use o mesmo segredo e gere os códigos.
3. Entregue um código para cada assinante. Ele cola na aba Golden Moon e ativa (funciona sem internet).

> Atenção: a verificação é local. Quem decompilar o `.exe` consegue achar o segredo; para uma cobrança de verdade, o ideal é validar a assinatura em um servidor (ex.: o seu no Render).

## Gerar o executável no Windows

Com o .NET 8 SDK instalado, abra o terminal nesta pasta e execute:

```powershell
dotnet publish MoonlightLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

O arquivo será gerado em:

`bin\Release\net8.0-windows\win-x64\publish\MoonlightLauncher.exe`

## Gerar pelo GitHub Actions (sem instalar .NET no seu PC)

1. Envie o CONTEÚDO da pasta (MoonlightLauncher.csproj, .github, Assets…) para a raiz de um repositório GitHub.
2. Abra a aba **Actions** do repositório.
3. Selecione **Build Moonlight Launcher (Windows)** e clique em **Run workflow** (ou aguarde o build automático após enviar código para `main`/`master`).
4. Quando terminar, abra a execução e baixe o artefato `MoonlightLauncher-Windows-x64`.
5. Extraia o ZIP do artefato. Dentro estará `MoonlightLauncher.exe`.

O build usa Windows porque o projeto é WPF. O executável é publicado como self-contained, então não exige que o usuário final instale o .NET Desktop Runtime.

## Publicar uma atualização (os jogadores recebem o aviso sozinhos)

1. (Nada para configurar: o GitHub Actions grava o seu repositório dentro do `.exe` na hora do build.)
2. Envie o código novo com o GitHub Desktop (Commit + Push).
3. No site do GitHub: **Releases → Draft a new release**. Em *Choose a tag* digite a nova versão
   (ex.: `v1.1.0`, sempre maior que a anterior), dê um título, escreva as novidades e clique em **Publish release**.
4. Aguarde o build da aba **Actions** terminar (uns 3–5 min). Ele anexa o `MoonlightLauncher.exe` na Release.
5. Pronto: quem abrir (ou já estiver com o launcher aberto) vê "Nova versão disponível", uma barra de download e o launcher reinicia sozinho. Em **Ajustes → Geral → Atualizações** dá para ligar a atualização 100% automática (sem perguntar) ou verificar na hora. Nenhum arquivo extra fica no PC: o .exe antigo é apagado na abertura seguinte.

> Quem já tem uma versão **sem** esse sistema precisa baixar o `.exe` novo uma única vez; depois disso as atualizações são automáticas.

## Aba Mods

- **Modrinth** funciona direto, sem configurar nada.
- **CurseForge** precisa de uma chave de API gratuita (botão "Pegar chave grátis" dentro da aba). Cole e salve; ela fica só no seu PC.
- Mods, texturas e shaders são instalados em `%AppData%\.minecraft\mods`, `resourcepacks` e `shaderpacks`.

## Aba Skins

- Escolha a skin por **nick**, **link do NameMC** (perfil `namemc.com/profile/Nick` ou skin `namemc.com/skin/…`), **UUID**, link `.png` ou arquivo do PC.
- **Conta Microsoft:** clique em *Enviar para a Mojang*. A skin passa a valer no jogo todo e aparece no NameMC.
- **Conta offline:** a skin é aplicada no jogo ao clicar em Jogar (um resource pack `MoonlightSkin.zip` é gerado em `resourcepacks` e ligado no `options.txt`). Contas offline não existem na Mojang, então o NameMC não consegue mostrá-las.
- As skins ficam guardadas em `%AppData%\MoonlightLauncher\skins`.

## Observações

- O ícone do aplicativo vem de `Assets\icon.ico`.
- O launcher usa `%AppData%\.minecraft` como pasta do jogo.
- Para distribuir login Microsoft, configure suas credenciais de aplicativo e respeite os requisitos de autenticação da Microsoft/Mojang.

## Novidades da versão 1.2.0

- **Aba Mods**: páginas só com números (1 2 3 … 40), abas e filtros reorganizados, botões com tamanho igual.
- **CurseForge**: a chave de API já vem no launcher; os mods aparecem sem configurar nada. Se a chave for recusada, aparece um campo para colar outra.
- **Golden Moon libera**: criador de skin, criador de modpacks ("Meus modpacks") e troca de ícones. Sem ele, aparece um cartão com cadeado.
- **Ícones**: Ajustes > Ícones Golden Moon troca o ícone do aplicativo (janela/barra de tarefas) e a mini lua. O ícone do .exe não muda em tempo de execução.
- **Animações**: desfile de benefícios ao abrir a aba Golden Moon e explosão dourada ao ativar.
- **Criador de skin**: círculos, atalhos de teclado (P E B I L R F O K C D N, [ ]), ajustes de cor da skin inteira e rascunho automático.

Observação: não foi possível compilar aqui (sem .NET). Rode `dotnet build` e me avise se aparecer algum erro.

---

## Novidades desta versão (1.13)

- **Abertura**: a luazinha saiu; a janela branca agora treme/balança bem mais forte e o tremor cresce até abrir.
- **Aba Jogar detalhada** (botão `▦ Detalhado` / `☰ Lista`): destaque grande da versão escolhida (foto em bloco, banner,
  nome da atualização, descrição e destaques) + cartões de todas as versões com o banner do que cada uma adiciona
  (a 1.20 tem banner de cerejeira, a 1.15 de abelhas, 1.16 Nether etc.). Os banners são pixel art em `Assets/versions/`
  e podem ser refeitos com `python3 tools/gerar-banners.py Assets/versions`.
- **Modo leve**: sem tremor/brilhos de abertura, sem efeito de mouse, animações a 20 qps, imagens em qualidade simples.
- **Modo normal**: o launcher para de desenhar quando a janela não está em foco, 3D parado redesenha menos, e o .exe
  sai pré-compilado (ReadyToRun) para abrir mais rápido.
- **Editor de skin → `🧍 Pintar em 3D por parte`**: escolha cabeça / corpo / braço / perna e pinte direto na peça 3D
  (botão direito arrasta para girar, rodinha dá zoom, clique rápido direito = conta-gotas). Camada base ou externa.

## Modo Leve (interface simples)

Em **Ajustes → Desempenho → 🍃 Leve** o launcher troca a janela grande por uma janelinha simples, parecida com o
Legacy Launcher (conta, modo, versão, **Jogar** e quatro botões), com o fundo da noite chuvosa (`Assets/lite/bg.jpg`).
Dentro dela, o botão ☰ → *Configurações* tem a RAM e o botão para voltar à interface completa.
Código: `LiteWindow.cs`, `LiteDialogs.cs` e `MainWindow.Lite.cs`.
