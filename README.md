# DeskWidgets

Widget de área de trabalho para Windows 11, nativo (C# / WPF), feito a partir do modelo: notas em checklist, imagem, clima, bateria dos dispositivos, relógio e calendário.

## Como gerar o .exe

**Opção A: GitHub Actions (não precisa instalar nada no PC)**

1. Crie um repositório no GitHub e envie esta pasta inteira (inclusive `.github/`).
2. Abra a aba **Actions** → *Build DeskWidgets* → **Run workflow**.
3. Quando terminar, baixe o artefato:
   - `DeskWidgets-completo-win-x64`: roda direto em qualquer Windows 10/11.
   - `DeskWidgets-leve-win-x64`: bem menor, mas exige o **.NET 10 Desktop Runtime** instalado.

**Opção B: no seu PC**

1. Instale o **.NET 10 SDK** (dotnet.microsoft.com/download).
2. Dê dois cliques em `build.bat`. O executável sai em `publish\DeskWidgets.exe`.

Para testar durante o desenvolvimento: `dotnet run` na pasta do projeto.

## Uso

| Ação | Como |
|---|---|
| Mover | Arraste qualquer bloco |
| Redimensionar | **Ctrl + roda do mouse** sobre o widget, ou o controle deslizante nas configurações |
| Configurações | Botão direito no widget, ou no ícone da bandeja |
| Checklist | Clique no bloco **Notas**; Enter adiciona, Esc fecha |
| Imagem | Clique no bloco vazio, ou botão direito → *Escolher imagem…* |
| Atualizar clima | Clique no clima |
| Calendário | Setas trocam o mês; clique no nome do mês para voltar ao atual |
| Trazer de volta (após Win+D) | Clique no ícone da bandeja |

## Personalização

- 10 paletas com gradiente prontas, ou cores livres (topo, base, destaque e texto) com seletor de cor.
- Direção do gradiente (vertical, horizontal, diagonal), opacidade do fundo de 0% a 100% e arredondamento dos cantos.
- Relógio: qualquer fonte instalada, tamanho de 28 a 140 px, negrito, 12/24 h e segundos opcionais.
- Data e calendário em português, inglês ou espanhol (o dia de hoje fica marcado com a cor de destaque).
- Clima por cidade (busca integrada), °C ou °F, com ícones que mudam conforme o tempo e dia/noite.
- Liga/desliga cada bloco e escolhe quais dispositivos aparecem na bateria.
- Camada: fixo na área de trabalho, janela comum ou sempre por cima.

## Bateria: o que é detectado

| Tipo | Funciona? |
|---|---|
| Fones, mouses, teclados e controles **Bluetooth** (os que mostram bateria em Configurações → Bluetooth) | Sim |
| Dispositivos **Bluetooth LE** com o serviço padrão de bateria | Sim |
| Controles Xbox (adaptador sem fio ou Bluetooth) | Sim |
| Dispositivos 2.4 GHz que se apresentam ao Windows como bateria HID | Sim |
| Receptores 2.4 GHz com protocolo próprio: Logitech Lightspeed/Unifying/Bolt, Razer HyperSpeed, Corsair Slipstream, HyperX etc. | **Não** — o Windows não recebe essa informação; só o software da marca (G HUB, Synapse, iCUE) consegue ler |

Se um dispositivo seu não aparecer, diga a marca/modelo: para Logitech, por exemplo, dá para implementar o protocolo HID++ dentro do app.

## Consumo de recursos

O app foi feito para ficar parado quase o tempo todo:

- Roda em **modo de eficiência** do Windows 11 (prioridade baixa + EcoQoS), então nunca disputa CPU com jogos.
- O relógio acorda uma vez por minuto, exatamente na virada (uma vez por segundo só se você ativar os segundos).
- Clima: 1 requisição pequena a cada 30 min. Bateria: 1 leitura a cada 3 min, fora da thread da interface.
- Sem animações; ícones de clima são vetoriais e desenhados uma única vez.
- Imagens grandes são decodificadas em tamanho reduzido, e a memória ociosa é devolvida ao Windows depois de carregar.

Abra o Gerenciador de Tarefas para conferir no seu PC.

## Onde ficam os dados

`%AppData%\DeskWidgets\`
- `settings.json`: configurações
- `notas.json`: checklist
- `erros.log`: só existe se algo der errado

## Estrutura

```
App.xaml(.cs)            instância única + ícone da bandeja
MainWindow.xaml(.cs)     o widget
SettingsWindow.xaml(.cs) configurações (aplicam na hora)
Services/                clima (Open-Meteo), bateria, configurações, inicialização com o Windows
Helpers/                 ícones de clima, cores, chamadas nativas do Windows
```
