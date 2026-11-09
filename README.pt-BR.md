# KulyCopy

**Gerenciador de área de transferência open source para Windows, inspirado no [CopyQ](https://github.com/hluk/CopyQ).**

O KulyCopy guarda tudo que você copia, organiza em abas e traz para o Windows os recursos que fazem
o CopyQ ser tão bom no Linux: histórico pesquisável, itens fixados, notas, comandos programáveis,
atalhos globais e uma interface de linha de comando completa.

Escrito em C# (.NET 8 / WPF), licença MIT. Versão em inglês: [README.md](README.md).

## Recursos

- **Histórico** de texto, HTML, RTF, imagens (salvas como PNG) e arquivos copiados, com remoção de
  duplicatas e limite de itens configurável por aba.
- **Abas** para organizar (`clipboard`, `Notas`, `URLs`…), mover/copiar itens entre abas, limite por
  aba, importação/exportação em JSON.
- **Busca instantânea**: é só começar a digitar na janela principal (o filtro aceita regex).
- **Itens fixados** ficam no topo e não são removidos nem descartados pelo limite.
- **Notas** em qualquer item.
- **Ativar para colar**: Enter copia o item, esconde a janela e cola no programa de onde você veio.
  Duplo clique e o menu da bandeja fazem o mesmo.
- **Editar itens**, criar itens à mão, ver todos os formatos guardados de um item.
- **Comandos** (estilo CopyQ): automáticos (rodam a cada novo conteúdo: rotear para abas, ignorar
  gerenciadores de senha, transformar texto…), de menu (aparecem no menu de contexto do item) e com
  atalho global. Os comandos são JavaScript com API igual à do CopyQ.
- **Atalhos globais**: mostrar/esconder, colar próximo/anterior, colar como texto puro, ligar/desligar
  a gravação, editar a área de transferência, novo item, além de qualquer comando.
- **Linha de comando**: `kulycopy read 0`, `kulycopy add "texto"`, `kulycopy eval "..."`, conversando
  com a instância em execução, como a CLI do CopyQ.
- **Ícone na bandeja** com itens recentes, tema claro/escuro (segue o Windows), modo portátil,
  iniciar com o Windows.

## Baixar e instalar

Windows 10 (1809+) ou Windows 11, 64 bits. Não precisa instalar mais nada: os pacotes já incluem o runtime do .NET.

| Pacote | Onde pegar | Observações |
| --- | --- | --- |
| **Instalador** (recomendado) | [`KulyCopy-Setup-x.y.z.exe`](https://github.com/GuilhermeKulyk/kulycopy/releases/latest) | Por usuário, sem precisar de administrador. Cria atalho no Menu Iniciar, opção *iniciar com o Windows* e desinstalador. |
| **Portátil** | [`KulyCopy-x.y.z-portable-win-x64.zip`](https://github.com/GuilhermeKulyk/kulycopy/releases/latest) | Descompacte em qualquer pasta e rode `kulycopy.exe`; as configurações ficam na pasta `data` ao lado. |
| **MSIX** | anexado em cada release | Para a Microsoft Store e instalação corporativa (sem assinatura). |

Depois de instalar, o KulyCopy fica na bandeja do sistema. Pressione **Ctrl+Shift+V** (ou clique no
ícone da bandeja) para abrir o histórico e Enter em um item para colar.

O instalador ainda não é assinado digitalmente, então o SmartScreen pode mostrar "O Windows protegeu o
computador": clique em *Mais informações › Executar assim mesmo*. Publicação no winget e na Microsoft
Store está planejada; veja [docs/publishing.md](docs/publishing.md).

### Compilar do código-fonte

Requisito: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone https://github.com/GuilhermeKulyk/kulycopy.git
cd kulycopy
dotnet build -c Release          # app: src\KulyCopy\bin\Release\net8.0-windows\kulycopy.exe
dotnet test                      # testes
.\build.ps1                      # dist\: exe único, zip portátil e instalador (-Msix gera o pacote da loja)
```

**Modo portátil** sem o zip: crie um arquivo vazio chamado `portable` ao lado do `kulycopy.exe` (ou inicie
com `--portable`). Configurações e histórico ficam na pasta `data` junto do executável.

## Onde ficam os dados

| O quê | Onde |
| --- | --- |
| Configurações | `%APPDATA%\KulyCopy\settings.json` e `commands.json` |
| Histórico | `%LOCALAPPDATA%\KulyCopy\history.db` (SQLite) e `kulycopy.log` |

Atalho global padrão para mostrar/esconder: **Ctrl+Shift+V** (mude em Preferences › Shortcuts).

## Teclado (janela principal)

| Teclas | Ação |
| --- | --- |
| digitar | Buscar (regex) · `Esc` limpa / esconde |
| `Enter` | Ativar: copia, esconde e cola na janela anterior |
| `Ctrl+Enter` | Só copiar para a área de transferência |
| `F2` / `Shift+F2` | Editar item / editar notas |
| `F4` | Ver conteúdo do item (todos os formatos) |
| `F5` | Action: rodar um script nos itens selecionados |
| `F6` | Comandos e atalhos globais |
| `Delete` | Remover itens selecionados |
| `Ctrl+N` | Novo item |
| `Ctrl+Shift+P` | Fixar / desafixar |
| `Ctrl+Cima/Baixo`, `Ctrl+Home/End` | Mover item |
| `Ctrl+T`, `Ctrl+F2`, `Ctrl+W` | Nova / renomear / remover aba |
| `Esquerda`, `Direita`, `Ctrl+Tab`, `Ctrl+1..9` | Trocar de aba |
| `Ctrl+Shift+X` | Ligar/desligar a gravação |
| `Ctrl+P` | Preferências |

## Linha de comando

```text
kulycopy                      inicia, ou mostra/esconde se já estiver rodando
kulycopy <função> [args..]    chama uma função de script e imprime o resultado
kulycopy eval <script>        avalia JavaScript
kulycopy - < script.js        lê o script da entrada padrão

kulycopy add "um texto"
kulycopy read 0
kulycopy read image/png 0 > imagem.png
kulycopy copy "olá" && kulycopy paste
kulycopy eval "tab('Notas'); add('lembrar disso')"
```

`kulycopy --help` mostra a lista completa. Referência da API de scripts: [docs/scripting.md](docs/scripting.md).

## O que falta em relação ao CopyQ

Ainda não implementado (contribuições são bem-vindas): abas criptografadas (o CopyQ usa GPG),
sincronização de aba com pasta, tags, ícones personalizados de item, traduções, sistema de plugins,
classes `File`/`Dir` dos scripts e funções exclusivas do X11.

## Licença

[MIT](LICENSE). Projeto independente, sem vínculo com o CopyQ.
