using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace KulyCopy.Core;

/// <summary>
/// Comando configurável pelo usuário, no mesmo espírito do CopyQ:
/// pode rodar automaticamente quando algo novo chega na área de transferência,
/// aparecer no menu de contexto dos itens, ter atalho global ou ser só um script.
/// </summary>
public sealed class Command : INotifyPropertyChanged
{
    private string _name = "New command";
    private bool _enabled = true;
    private bool _automatic;
    private bool _inMenu;
    private bool _isGlobalShortcut;
    private bool _isScript;
    private string _match = "";
    private string _window = "";
    private string _input = "";
    private string _output = "";
    private string _tab = "";
    private string _outputTab = "";
    private bool _remove;
    private bool _hideWindow;
    private string _shortcut = "";
    private string _globalShortcut = "";
    private string _icon = "";
    private string _cmd = "";

    /// <summary>Nome exibido no menu/configurações.</summary>
    public string Name { get => _name; set => Set(ref _name, value); }

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    /// <summary>Executa automaticamente para cada novo conteúdo da área de transferência.</summary>
    public bool Automatic { get => _automatic; set => Set(ref _automatic, value); }

    /// <summary>Aparece no menu de contexto dos itens.</summary>
    public bool InMenu { get => _inMenu; set => Set(ref _inMenu, value); }

    /// <summary>Executa a partir de um atalho global (GlobalShortcut).</summary>
    public bool IsGlobalShortcut { get => _isGlobalShortcut; set => Set(ref _isGlobalShortcut, value); }

    /// <summary>Apenas define funções/constantes para outros scripts (executado antes de cada script).</summary>
    public bool IsScript { get => _isScript; set => Set(ref _isScript, value); }

    /// <summary>Regex que o texto do item precisa casar para o comando se aplicar.</summary>
    public string Match { get => _match; set => Set(ref _match, value); }

    /// <summary>Regex que o título da janela de origem precisa casar.</summary>
    public string Window { get => _window; set => Set(ref _window, value); }

    /// <summary>Formato (mime) que o item precisa ter para o comando se aplicar.</summary>
    public string Input { get => _input; set => Set(ref _input, value); }

    /// <summary>Formato em que a saída do script é guardada.</summary>
    public string Output { get => _output; set => Set(ref _output, value); }

    /// <summary>Aba para onde o item é copiado (automático) ou onde os itens selecionados são adicionados (menu).</summary>
    public string Tab { get => _tab; set => Set(ref _tab, value); }

    /// <summary>Aba onde a saída do script vira um novo item.</summary>
    public string OutputTab { get => _outputTab; set => Set(ref _outputTab, value); }

    /// <summary>Automático: não guarda na aba da área de transferência. Menu: remove os itens selecionados após executar.</summary>
    public bool Remove { get => _remove; set => Set(ref _remove, value); }

    /// <summary>Esconde a janela principal antes de executar (comandos de menu).</summary>
    public bool HideWindow { get => _hideWindow; set => Set(ref _hideWindow, value); }

    /// <summary>Atalho dentro da janela principal, ex.: "Ctrl+Shift+O".</summary>
    public string Shortcut { get => _shortcut; set => Set(ref _shortcut, value); }

    /// <summary>Atalho global do sistema, ex.: "Ctrl+Alt+V".</summary>
    public string GlobalShortcut { get => _globalShortcut; set => Set(ref _globalShortcut, value); }

    public string Icon { get => _icon; set => Set(ref _icon, value); }

    /// <summary>Script JavaScript (API no estilo do CopyQ). Pode ser vazio para comandos só de roteamento.</summary>
    public string Cmd { get => _cmd; set => Set(ref _cmd, value); }

    [JsonIgnore]
    public bool HasScript => !string.IsNullOrWhiteSpace(Cmd);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public Command Clone() => new()
    {
        Name = Name, Enabled = Enabled, Automatic = Automatic, InMenu = InMenu,
        IsGlobalShortcut = IsGlobalShortcut, IsScript = IsScript, Match = Match, Window = Window,
        Input = Input, Output = Output, Tab = Tab, OutputTab = OutputTab, Remove = Remove,
        HideWindow = HideWindow, Shortcut = Shortcut, GlobalShortcut = GlobalShortcut, Icon = Icon, Cmd = Cmd,
    };
}
