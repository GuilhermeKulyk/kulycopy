using System.Windows.Threading;
using KulyCopy.Core;
using KulyCopy.Native;

namespace KulyCopy.Clip;

/// <summary>
/// Escuta WM_CLIPBOARDUPDATE, aplica um pequeno debounce (alguns programas disparam várias
/// atualizações seguidas) e entrega um snapshot do conteúdo.
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Func<ClipboardSnapshot?> _reader;
    private readonly DispatcherTimer _debounce;
    private uint _lastSequence;
    private bool _listening;

    public bool Enabled { get; set; } = true;

    public event Action<ClipboardSnapshot>? Changed;

    public ClipboardMonitor(MessageWindow window, Func<ClipboardSnapshot?> reader)
    {
        _window = window;
        _reader = reader;
        _debounce = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(120) };
        _debounce.Tick += (_, _) => Process(force: false);
        _window.ClipboardUpdated += OnClipboardUpdated;

        _listening = NativeMethods.AddClipboardFormatListener(_window.Handle);
        if (!_listening) Log.Error("AddClipboardFormatListener failed; clipboard monitoring is unavailable");
        _lastSequence = NativeMethods.GetClipboardSequenceNumber();
    }

    private void OnClipboardUpdated()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Processa o conteúdo atual mesmo sem mudança (menu "Process clipboard").</summary>
    public void CheckNow() => Process(force: true);

    private void Process(bool force)
    {
        _debounce.Stop();
        var seq = NativeMethods.GetClipboardSequenceNumber();
        if (!force && seq == _lastSequence) return;
        _lastSequence = seq;
        if (!Enabled && !force) return;

        ClipboardSnapshot? snapshot;
        try { snapshot = _reader(); }
        catch (Exception ex)
        {
            Log.Error("Failed to read clipboard", ex);
            return;
        }

        if (snapshot is null || snapshot.Data.Count == 0) return;
        Changed?.Invoke(snapshot);
    }

    public void Dispose()
    {
        _debounce.Stop();
        _window.ClipboardUpdated -= OnClipboardUpdated;
        if (_listening) NativeMethods.RemoveClipboardFormatListener(_window.Handle);
        _listening = false;
    }
}
