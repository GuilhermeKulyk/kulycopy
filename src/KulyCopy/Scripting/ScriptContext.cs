using System.Text;
using KulyCopy.Core;
using KulyCopy.Models;

namespace KulyCopy.Scripting;

/// <summary>Estado de uma execução de script: dados de entrada, aba atual, seleção, saída.</summary>
public sealed class ScriptContext
{
    /// <summary>Dados do item sendo processado (comandos automáticos/menu). null = usar a área de transferência.</summary>
    public Dictionary<string, byte[]>? InputData { get; set; }

    /// <summary>Formato principal da entrada (campo Input do comando).</summary>
    public string InputMime { get; set; } = Mime.Text;

    /// <summary>Aba corrente para operações por linha; definida por tab(). null = aba visível na janela.</summary>
    public TabViewModel? Tab { get; set; }

    /// <summary>Itens selecionados (comandos de menu). null = seleção atual da janela principal.</summary>
    public List<ClipboardItem>? SelectedItems { get; set; }

    /// <summary>Aba a que a seleção pertence.</summary>
    public TabViewModel? SelectionTab { get; set; }

    public string[] Arguments { get; set; } = Array.Empty<string>();

    public string? Stdin { get; set; }

    public StringBuilder Stdout { get; } = new();

    public bool Ignore { get; set; }

    public int ExitCode { get; set; }

    public string? WindowTitle { get; set; }

    public Command? Command { get; set; }
}

public sealed class ScriptResult
{
    public object? Value { get; set; }
    public string? Error { get; set; }
    public int ExitCode { get; set; }
    public string Stdout { get; set; } = "";
    public bool Ignored { get; set; }
    public bool Aborted { get; set; }
}
