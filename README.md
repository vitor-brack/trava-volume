# Trava de volume

Programa para Windows que trava o volume dos dispositivos de áudio (alto-falantes, fones e microfones) no nível que você escolheu. As mudanças que você mesmo faz continuam funcionando. As feitas por qualquer outro programa são desfeitas em milissegundos e ficam anotadas num registro.

Serve para quando algum programa mexe no volume sem pedir, como um navegador ajustando o microfone durante uma chamada, uma sessão de acesso remoto repassando teclas de volume ou um jogo.

## O que é travado

- Volume e mudo de todas as saídas de áudio ativas.
- Volume de todos os microfones ativos.
- Controles extras da aba **Níveis** do painel de Som, como o retorno do microfone no fone e as entradas de linha da placa de som.

O mudo dos microfones **nunca** é travado. Se a trava desfizesse um "mudo", poderia reativar o microfone sem você saber.

## Como ele sabe que foi você

O Windows não informa qual programa mudou o volume de um dispositivo. Por isso, a trava só aceita mudanças que chegam por caminhos que apenas uma pessoa usa:

- teclas de volume de um teclado ou mouse de verdade (teclas simuladas por programas não contam);
- rodinha do mouse sobre o ícone de volume da barra de tarefas;
- controles de volume do próprio Windows (Configurações rápidas, Configurações, painel de Som e Mixer), com você mexendo naquele momento.

Nos controles da aba Níveis o Windows informa qual processo fez a mudança, e só o painel de Som é aceito. Um programa bloqueado uma vez continua bloqueado nas tentativas seguintes.

## Requisitos

Windows 10 ou 11. O compilador C# do .NET Framework 4.x já vem com o Windows, então não é preciso instalar nada.

## Como compilar

Execute `build.cmd`. O programa é gerado em `bin\TravaVolume.exe`.

O `build.cmd` compila os arquivos de `src\` na ordem da lista que ele traz. Ao criar um arquivo novo, acrescente-o à lista.

## Organização do código

Os arquivos ficam em `src\`, na ordem em que o `build.cmd` os compila:

| Arquivo | Conteúdo |
|---|---|
| `AssemblyInfo.cs` | Título, produto e versão do programa |
| `Audio\CoreAudio.cs` | Interfaces COM do Core Audio: dispositivos, volume e mudo, avisos de mudança e sessões de áudio |
| `Audio\DeviceTopology.cs` | Interfaces COM da topologia do aparelho, de onde saem os controles extras da aba Níveis |
| `Native.cs` | Funções e estruturas do Win32: Raw Input, janelas, cursor e última entrada do usuário |
| `Program.cs` | Ponto de entrada; impede abrir duas cópias ao mesmo tempo |
| `Models.cs` | Dados de cada dispositivo travado e de cada controle extra |
| `Callbacks.cs` | Recebem os avisos de mudança de volume que o Windows manda |
| `InputWindow.cs` | Janela invisível que recebe as teclas de volume e de mídia |
| `Guard.cs` | Classe principal (`Guard`): constantes, campos e construtor |
| `Guard.Devices.cs` | Encontrar os dispositivos de áudio, começar e parar de protegê-los |
| `Guard.Controls.cs` | Controles extras da aba Níveis |
| `Guard.Decisions.cs` | Decidir se uma mudança foi sua (aceita) ou de outro programa (desfeita) |
| `Guard.RawInput.cs` | Ler as teclas de volume e os sinais de mídia de cada teclado e mouse |
| `Guard.Helpers.cs` | Comparar níveis, achar nomes de processos, janelas e dispositivos, listar quem está usando o áudio |
| `Guard.Tray.cs` | Ícone da bandeja, menu, ligar e desligar a trava, balão de aviso e início com o Windows |
| `Guard.Storage.cs` | Registro, níveis salvos, configuração e encerramento do programa |

A `Guard` é uma classe só, repartida em vários arquivos com `partial class`.

## Como usar

Abra o `TravaVolume.exe`. Ele fica na bandeja do sistema, perto do relógio:

- cadeado verde fechado: trava ligada;
- cadeado cinza aberto: trava desligada.

Clique no ícone, com o botão esquerdo ou o direito, para ver os níveis travados e as opções:

- **Ligar a trava / Desligar a trava.** Ao religar, os níveis daquele momento passam a ser os travados.
- **Abrir o registro.**
- **Iniciar com o Windows.**
- **Sair.**

## Arquivos

Ficam em `%LOCALAPPDATA%\TravaVolume`:

| Arquivo | Conteúdo |
|---|---|
| `registro.txt` | O que foi aceito, bloqueado ou restaurado, e por quem |
| `niveis.txt` | Níveis travados de cada dispositivo |
| `controles.txt` | Níveis travados dos controles da aba Níveis |
| `config.txt` | Se a trava está ligada ou desligada |

## Desinstalar

No menu do ícone, desmarque **Iniciar com o Windows** e clique em **Sair**. Depois, apague a pasta `%LOCALAPPDATA%\TravaVolume`.
