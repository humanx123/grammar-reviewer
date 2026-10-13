using Clipboard = System.Windows.Clipboard;
using GrammarBubble.App.Interop;

namespace GrammarBubble.App.Services;

public sealed class CapturedText
{
    public required IntPtr SourceWindow { get; init; }
    public string? Text { get; init; }
    public required System.Drawing.Point Anchor { get; init; }
}

public static class TextCapture
{
    public static async Task<CapturedText> CaptureAsync()
    {
        var source = Native.GetForegroundWindow();
        var anchor = CaretLocator.Locate(source);
        var previous = ReadClipboardText();
        var sentinel = "\u0001grammar-bubble:" + Guid.NewGuid();

        Native.ReleaseModifiers();
        await Task.Delay(120);

        var copied = await CopyAsync(sentinel);
        if (copied is null)
        {
            Native.SendCtrl(Native.VkA);
            await Task.Delay(80);
            copied = await CopyAsync(sentinel);
        }

        RestoreClipboard(previous);

        var text = string.IsNullOrWhiteSpace(copied) ? null : copied;
        return new CapturedText { SourceWindow = source, Text = text, Anchor = anchor };
    }

    public static async Task PasteAsync(IntPtr target, string text)
    {
        Native.SetForegroundWindow(target);
        await Task.Delay(150);
        SetClipboard(text);
        await Task.Delay(40);
        Native.SendCtrl(Native.VkV);
    }

    private static async Task<string?> CopyAsync(string sentinel)
    {
        SetClipboard(sentinel);
        await Task.Delay(40);
        Native.SendCtrl(Native.VkC);

        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(40);
            var value = ReadClipboardText();
            if (value is not null && value != sentinel) return value;
        }
        return null;
    }

    private static string? ReadClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SetClipboard(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                return;
            }
            catch
            {
                Thread.Sleep(30);
            }
        }
    }

    private static void RestoreClipboard(string? previous)
    {
        if (previous is null)
        {
            try { Clipboard.Clear(); } catch { }
            return;
        }
        SetClipboard(previous);
    }
}

public static class CaretLocator
{
    public static System.Drawing.Point Locate(IntPtr window)
    {
        var threadId = Native.GetWindowThreadProcessId(window, out _);
        var info = new Native.GuiThreadInfo { CbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.GuiThreadInfo>() };

        if (Native.GetGUIThreadInfo(threadId, ref info) && info.HwndCaret != IntPtr.Zero)
        {
            var point = new Native.Point { X = info.RcCaret.Left, Y = info.RcCaret.Bottom };
            if (Native.ClientToScreen(info.HwndCaret, ref point))
                return new System.Drawing.Point(point.X, point.Y);
        }

        var cursor = System.Windows.Forms.Cursor.Position;
        return cursor;
    }
}
