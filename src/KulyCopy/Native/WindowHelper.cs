using System.Diagnostics;
using System.Text;

namespace KulyCopy.Native;

/// <summary>Utilidades sobre janelas de outros processos (foco, título) e envio de teclas.</summary>
internal static class WindowHelper
{
    public static string GetWindowTitle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "";
        var len = NativeMethods.GetWindowTextLength(hwnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetForegroundWindowTitle() => GetWindowTitle(NativeMethods.GetForegroundWindow());

    public static string GetClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool IsOwnWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>Janela em primeiro plano que não pertence a este processo nem à barra de tarefas.</summary>
    public static IntPtr GetForeignForegroundWindow()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || IsOwnWindow(hwnd)) return IntPtr.Zero;
        var cls = GetClassName(hwnd);
        if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "Progman" or "WorkerW")
            return IntPtr.Zero;
        return hwnd;
    }

    /// <summary>Traz a janela para frente, usando o truque de AttachThreadInput para contornar restrições de foco.</summary>
    public static void FocusWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return;
        if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

        var fg = NativeMethods.GetForegroundWindow();
        if (fg == hwnd) return;

        var current = NativeMethods.GetCurrentThreadId();
        var fgThread = fg == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(fg, out _);
        var targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);

        var attachedFg = fgThread != 0 && fgThread != current && NativeMethods.AttachThreadInput(current, fgThread, true);
        var attachedTarget = targetThread != 0 && targetThread != current && NativeMethods.AttachThreadInput(current, targetThread, true);
        try
        {
            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedFg) NativeMethods.AttachThreadInput(current, fgThread, false);
            if (attachedTarget) NativeMethods.AttachThreadInput(current, targetThread, false);
        }
    }

    /// <summary>Solta modificadores que o usuário ainda possa estar segurando (ex.: após um atalho global).</summary>
    public static void ReleaseModifiers()
    {
        var ups = new List<NativeMethods.INPUT>();
        foreach (var vk in new ushort[] { NativeMethods.VK_CONTROL, NativeMethods.VK_SHIFT, NativeMethods.VK_MENU, NativeMethods.VK_LWIN, NativeMethods.VK_RWIN })
            if (NativeMethods.IsKeyDown(vk)) ups.Add(NativeMethods.KeyInput(vk, true));
        if (ups.Count > 0) Send(ups.ToArray());
    }

    public static void SendKeyCombo(ushort modifier, ushort key)
    {
        Send(new[]
        {
            NativeMethods.KeyInput(modifier, false),
            NativeMethods.KeyInput(key, false),
            NativeMethods.KeyInput(key, true),
            NativeMethods.KeyInput(modifier, true),
        });
    }

    public static void SendCtrlV() => SendKeyCombo(NativeMethods.VK_CONTROL, NativeMethods.VK_V);
    public static void SendCtrlC() => SendKeyCombo(NativeMethods.VK_CONTROL, NativeMethods.VK_C);
    public static void SendShiftInsert() => SendKeyCombo(NativeMethods.VK_SHIFT, NativeMethods.VK_INSERT);

    private static void Send(NativeMethods.INPUT[] inputs)
    {
        var size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>();
        NativeMethods.SendInput((uint)inputs.Length, inputs, size);
    }

    /// <summary>
    /// Foca a janela alvo e envia Ctrl+V. Síncrono: deve ser chamado fora da thread de UI ou quando
    /// um pequeno bloqueio for aceitável (scripts).
    /// </summary>
    public static void PasteInto(IntPtr target, bool shiftInsert = false)
    {
        if (target != IntPtr.Zero)
        {
            FocusWindow(target);
            // Dá tempo da janela receber o foco antes das teclas.
            for (var i = 0; i < 20 && NativeMethods.GetForegroundWindow() != target; i++) Thread.Sleep(10);
            Thread.Sleep(60);
        }
        ReleaseModifiers();
        Thread.Sleep(20);
        if (shiftInsert) SendShiftInsert(); else SendCtrlV();
    }

    public static void CopyFrom(IntPtr target)
    {
        if (target != IntPtr.Zero)
        {
            FocusWindow(target);
            Thread.Sleep(80);
        }
        ReleaseModifiers();
        Thread.Sleep(20);
        SendCtrlC();
    }

    public static string ProcessNameOf(IntPtr hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return "";
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return "";
        }
    }
}
