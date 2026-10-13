using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using GrammarBubble.App.Interop;
using GrammarBubble.App.Services;
using GrammarBubble.Core;
using Microsoft.Web.WebView2.Core;

namespace GrammarBubble.App.Windows;

public partial class BubbleWindow : Window
{
    private const string Host = "grammarbubble.local";
    private static readonly JsonSerializerOptions JsonOut = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly List<string> _pending = new();
    private bool _ready;
    private CapturedText? _captured;
    private CancellationTokenSource? _runCts;
    private bool _busyApplying;
    private CorrectionResult? _result;
    private CorrectionMode _mode;
    private string _pipeline = "";
    private bool[] _selected = Array.Empty<bool>();
    private DocLanguage _lang = Preferences.LoadLanguage();
    private static string LangCode(DocLanguage lang) => lang == DocLanguage.English ? "en" : "es";

    private const double CompactSize = 56;
    private const double MinWidth = 300;
    private const double MinHeight = 360;
    private const double MaxWidth = 900;
    private const double MaxHeight = 1000;
    private double _expandedWidth = 392;
    private double _expandedHeight = 500;
    private bool _compact;

    public bool IsCompact => _compact;

    public BubbleWindow()
    {
        InitializeComponent();
        Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(251, 250, 246);
        Loaded += async (_, _) => await InitializeWebAsync();
    }

    private void SetCompact(bool on)
    {
        if (on == _compact) return;
        _compact = on;
        if (on)
        {
            _expandedWidth = Width;
            _expandedHeight = Height;
            Width = CompactSize;
            Height = CompactSize;
        }
        else
        {
            Width = _expandedWidth;
            Height = _expandedHeight;
        }
        ClampToScreen();
        Post(new { type = "compact", on });
    }

    private void Resize(double dx, double dy)
    {
        if (_compact) return;
        Width = Math.Clamp(Width + dx, MinWidth, MaxWidth);
        Height = Math.Clamp(Height + dy, MinHeight, MaxHeight);
        _expandedWidth = Width;
        _expandedHeight = Height;
        ClampToScreen();
    }

    private void Move(double dx, double dy)
    {
        Left += dx;
        Top += dy;
        ClampToScreen();
    }

    private void ClampToScreen()
    {
        var area = System.Windows.SystemParameters.WorkArea;
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    private const double OffscreenCoordinate = -20000;
    private bool _open;

    public bool IsOpen => _open;

    public void Park()
    {
        _open = false;
        Left = OffscreenCoordinate;
        Top = OffscreenCoordinate;
    }

    public new void Hide() => Park();

    private void Reveal()
    {
        _open = true;
        var handle = new WindowInteropHelper(this).Handle;
        var foreground = Native.GetForegroundWindow();
        var foregroundThread = Native.GetWindowThreadProcessId(foreground, out _);
        var currentThread = Native.GetCurrentThreadId();
        Native.AttachThreadInput(currentThread, foregroundThread, true);
        Native.SetForegroundWindow(handle);
        Native.BringWindowToTop(handle);
        Native.AttachThreadInput(currentThread, foregroundThread, false);
        Activate();
    }

    private async Task InitializeWebAsync()
    {
        var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GrammarBubble", "webview");
        var env = await CoreWebView2Environment.CreateAsync(null, userData);
        await Web.EnsureCoreWebView2Async(env);

        var ui = Path.Combine(AppContext.BaseDirectory, "ui");
        Web.CoreWebView2.SetVirtualHostNameToFolderMapping(Host, ui, CoreWebView2HostResourceAccessKind.Allow);
        Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
        Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        Web.CoreWebView2.WebMessageReceived += (_, e) => OnMessage(e.WebMessageAsJson);
        Web.CoreWebView2.NavigationCompleted += (_, _) =>
        {
            _ready = true;
            foreach (var msg in _pending) Web.CoreWebView2.PostWebMessageAsJson(msg);
            _pending.Clear();
            _ = RefreshHealthAsync();
        };
        Web.CoreWebView2.Navigate($"https://{Host}/index.html");
    }

    public void OpenWith(CapturedText captured)
    {
        _captured = captured;
        SetCompact(false);
        PlaceNear(captured.Anchor);
        Reveal();
        FocusWeb();

        if (string.IsNullOrWhiteSpace(captured.Text))
        {
            Post(new { type = "notice", message = "Selecciona un texto o pulsa Ctrl+A en el campo y vuelve a intentar." });
            return;
        }

        var keys = SecretStore.Load();
        Post(new { type = "open", text = captured.Text, lang = LangCode(_lang), hasGroq = !string.IsNullOrWhiteSpace(keys.Groq), hasGemini = !string.IsNullOrWhiteSpace(keys.Gemini), hasNvidia = !string.IsNullOrWhiteSpace(keys.Nvidia) });
        if (ProviderFactory.CreateService(keys) is null)
            Post(new { type = "needKeys" });
        else
            _ = RunAsync(CorrectionMode.Correct);
    }

    public void OpenView(string view)
    {
        switch (view)
        {
            case "settings": OpenSettings(); break;
            case "help": OpenHelp(); break;
            case "write": OpenWriter(); break;
            case "chat": OpenChat(); break;
        }
    }

    public void OpenHelp()
    {
        SetCompact(false);
        CenterOnScreen();
        Reveal();
        FocusWeb();
        Post(new { type = "help" });
    }

    public void OpenSettings()
    {
        SetCompact(false);
        CenterOnScreen();
        Reveal();
        FocusWeb();
        _ = RefreshHealthAsync();
        var keys = SecretStore.Load();
        Post(new { type = "settings", lang = LangCode(_lang), hasGroq = !string.IsNullOrWhiteSpace(keys.Groq), hasGemini = !string.IsNullOrWhiteSpace(keys.Gemini), hasNvidia = !string.IsNullOrWhiteSpace(keys.Nvidia) });
    }

    private async Task RefreshHealthAsync()
    {
        var keys = SecretStore.Load();
        var results = await KeyChecker.CheckAllAsync(keys);
        Post(new
        {
            type = "health",
            overall = KeyChecker.Overall(results),
            providers = results.Select(r => new { name = r.Name, state = r.State.ToString().ToLowerInvariant(), detail = r.Detail }),
            masks = new { groq = KeyChecker.Mask(keys.Groq), gemini = KeyChecker.Mask(keys.Gemini), nvidia = KeyChecker.Mask(keys.Nvidia), kilo = KeyChecker.Mask(keys.Kilo) }
        });
    }

    private void FocusWeb()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Web.Focus();
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    public void Toggle()
    {
        if (_open) Hide();
        else OpenSettings();
    }

    private void PlaceNear(System.Drawing.Point anchor)
    {
        var scale = Native.GetDpiForSystem() / 96.0;
        var screen = System.Windows.Forms.Screen.FromPoint(anchor).WorkingArea;
        double x = anchor.X / scale + 12;
        double y = anchor.Y / scale + 18;

        var maxX = screen.Right / scale - Width - 8;
        var maxY = screen.Bottom / scale - Height - 8;
        if (y > maxY) y = anchor.Y / scale - Height - 24;
        Left = Math.Clamp(x, screen.Left / scale + 8, Math.Max(screen.Left / scale + 8, maxX));
        Top = Math.Clamp(y, screen.Top / scale + 8, Math.Max(screen.Top / scale + 8, maxY));
    }

    private void CenterOnScreen()
    {
        var area = System.Windows.SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 3;
    }

    private async Task RunAsync(CorrectionMode mode)
    {
        if (_captured?.Text is null) return;

        _runCts?.Cancel();
        _runCts = new CancellationTokenSource();
        var token = _runCts.Token;
        Post(new { type = "loading", mode = mode.ToString() });

        var service = ProviderFactory.CreateService(SecretStore.Load());
        if (service is null)
        {
            Post(new { type = "needKeys" });
            return;
        }

        try
        {
            _result = await service.CorrectAsync(_captured.Text, mode, _lang, token);
            _mode = mode;
            _pipeline = service.Pipeline;
            _selected = Enumerable.Repeat(true, _result.Changes.Count).ToArray();
            PostResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (GroqException ex)
        {
            Post(new { type = "error", message = ex.Message });
        }
        finally
        {
            service.Dispose();
        }
    }

    public const int MaxInputChars = 10000;
    private const int MaxChatTurns = 16;
    private readonly List<ChatTurn> _chat = new();

    public void OpenChat()
    {
        SetCompact(false);
        CenterOnScreen();
        Reveal();
        FocusWeb();
        Post(new { type = "chat" });
    }

    private async Task ChatAsync(string text)
    {
        _chat.Add(new ChatTurn("user", text));
        if (_chat.Count > MaxChatTurns) _chat.RemoveRange(0, _chat.Count - MaxChatTurns);
        Post(new { type = "chatBusy" });

        using var chat = ProviderFactory.CreateChatService(SecretStore.Load());
        try
        {
            var reply = await chat.AskAsync(_chat);
            _chat.Add(new ChatTurn("assistant", reply.Text));
            Post(new { type = "chatReply", text = reply.Text, provider = reply.Provider });
        }
        catch (GroqException ex)
        {
            Post(new { type = "chatError", message = ex.Message });
        }
    }

    public void OpenWriter()
    {
        SetCompact(false);
        CenterOnScreen();
        Reveal();
        FocusWeb();
        Post(new { type = "write", max = MaxInputChars });
    }

    private async Task ApplyAsync(string text)
    {
        if (_captured is null) return;
        if (_captured.SourceWindow == IntPtr.Zero)
        {
            try { System.Windows.Clipboard.SetDataObject(text, true); } catch { }
            Post(new { type = "applied-local", text });
            return;
        }
        _busyApplying = true;
        var target = _captured.SourceWindow;
        Hide();
        try
        {
            await TextCapture.PasteAsync(target, text);
        }
        finally
        {
            _busyApplying = false;
        }
    }

    private void OnMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString();

        switch (type)
        {
            case "ready":
                break;
            case "run":
                var mode = Enum.TryParse<CorrectionMode>(root.GetProperty("mode").GetString(), out var parsed)
                    ? parsed : CorrectionMode.Correct;
                _ = RunAsync(mode);
                break;
            case "apply":
                _ = ApplyAsync(root.GetProperty("text").GetString() ?? "");
                break;
            case "copy":
                try { System.Windows.Clipboard.SetDataObject(root.GetProperty("text").GetString() ?? "", true); } catch { }
                break;
            case "close":
                Hide();
                break;
            case "move":
                Move(root.GetProperty("dx").GetDouble(), root.GetProperty("dy").GetDouble());
                break;
            case "openChat":
                OpenChat();
                break;
            case "chatSend":
                var question = root.GetProperty("text").GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(question)) _ = ChatAsync(question);
                break;
            case "chatClear":
                _chat.Clear();
                break;
            case "reviewText":
                var typed = root.GetProperty("text").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(typed)) break;
                if (typed.Length > MaxInputChars)
                {
                    Post(new { type = "error", message = $"El texto supera el límite de {MaxInputChars:N0} caracteres." });
                    break;
                }
                _captured = new CapturedText
                {
                    SourceWindow = IntPtr.Zero,
                    Text = typed,
                    Anchor = System.Windows.Forms.Cursor.Position
                };
                _ = RunAsync(CorrectionMode.Correct);
                break;
            case "setLanguage":
                _lang = root.GetProperty("lang").GetString() == "en" ? DocLanguage.English : DocLanguage.Spanish;
                Preferences.SaveLanguage(_lang);
                Post(new { type = "language", lang = LangCode(_lang) });
                if (_captured?.Text is not null)
                    _ = RunAsync(_mode);
                break;
            case "toggle":
                var index = root.GetProperty("index").GetInt32();
                if (index >= 0 && index < _selected.Length)
                {
                    _selected[index] = root.GetProperty("on").GetBoolean();
                    PostResult();
                }
                break;
            case "setAll":
                _selected = Enumerable.Repeat(root.GetProperty("on").GetBoolean(), _selected.Length).ToArray();
                PostResult();
                break;
            case "resize":
                Resize(root.GetProperty("dx").GetDouble(), root.GetProperty("dy").GetDouble());
                break;
            case "collapse":
                SetCompact(root.GetProperty("on").GetBoolean());
                break;
            case "saveKeys":
                var groq = root.GetProperty("groq").GetString();
                var gemini = root.GetProperty("gemini").GetString();
                var nvidia = root.GetProperty("nvidia").GetString();
                var current = SecretStore.Load();
                var merged = new ApiKeys(
                    string.IsNullOrWhiteSpace(groq) ? current.Groq : groq.Trim(),
                    string.IsNullOrWhiteSpace(gemini) ? current.Gemini : gemini.Trim(),
                    string.IsNullOrWhiteSpace(nvidia) ? current.Nvidia : nvidia.Trim(),
                    current.Kilo);
                try
                {
                    SecretStore.Save(merged);
                    Post(new { type = "saved", ok = true });
                }
                catch (Exception ex)
                {
                    Post(new { type = "saved", ok = false, message = "No se pudieron guardar las claves: " + ex.Message });
                }
                _ = RefreshHealthAsync();
                break;
            case "checkKeys":
                _ = RefreshHealthAsync();
                break;
        }
    }

    private void PostResult()
    {
        if (_result is null || _captured?.Text is null) return;

        if (_mode == CorrectionMode.Translate)
        {
            Post(new
            {
                type = "result",
                mode = _mode.ToString(),
                pipeline = _pipeline,
                corrected = _result.Corrected,
                changes = _result.Changes.Select(c => new { original = c.Original, replacement = c.Replacement, reason = c.Reason, selected = true, applicable = false, sentence = "" }),
                segments = Array.Empty<object>()
            });
            return;
        }

        var (text, applied) = ChangeApplier.Apply(_captured.Text, _result.Changes, _selected);
        var segments = DiffEngine.Diff(_captured.Text, text)
            .Select(s => new { kind = s.Kind.ToString().ToLowerInvariant(), text = s.Text });
        Post(new
        {
            type = "result",
            mode = _mode.ToString(),
            pipeline = _pipeline,
            corrected = text,
            changes = applied.Select((a, i) => new
            {
                original = a.Change.Original,
                replacement = a.Change.Replacement,
                reason = a.Change.Reason,
                selected = _selected[i],
                applicable = a.Applicable,
                sentence = a.Sentence
            }),
            segments
        });
    }

    private void Post(object message)
    {
        var json = JsonSerializer.Serialize(message, JsonOut);
        if (_ready && Web.CoreWebView2 is not null)
            Web.CoreWebView2.PostWebMessageAsJson(json);
        else
            _pending.Add(json);
    }
}
