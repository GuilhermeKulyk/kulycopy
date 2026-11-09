using KulyCopy.Native;
using WinForms = System.Windows.Forms;

namespace KulyCopy.Clip;

/// <summary>
/// Janela invisível (message-only) que recebe WM_CLIPBOARDUPDATE e WM_HOTKEY.
/// Usa NativeWindow do WinForms porque é a forma mais simples e estável de ter um HWND próprio.
/// </summary>
public sealed class MessageWindow : WinForms.NativeWindow, IDisposable
{
    public event Action? ClipboardUpdated;
    public event Action<int>? HotkeyPressed;

    public MessageWindow()
    {
        CreateHandle(new WinForms.CreateParams
        {
            Parent = NativeMethods.HWND_MESSAGE,
            Caption = "KulyCopyMessageWindow",
        });
    }

    protected override void WndProc(ref WinForms.Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WM_CLIPBOARDUPDATE:
                ClipboardUpdated?.Invoke();
                m.Result = IntPtr.Zero;
                return;
            case NativeMethods.WM_HOTKEY:
                HotkeyPressed?.Invoke(m.WParam.ToInt32());
                m.Result = IntPtr.Zero;
                return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
