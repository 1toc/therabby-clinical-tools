using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Therabby.LoadBalanceViewer;

public sealed class MainForm : Form
{
    private readonly WebView2 _webView = new();
    private readonly LoadBalanceController _controller = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new();

    private bool _webReady;
    private bool _autoConnectAttempted;

    public MainForm()
    {
        Text = "Therabby Load Balance Viewer v0.3";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1440, 900);
        MinimumSize = new Size(1120, 720);
        BackColor = Color.FromArgb(7, 17, 31);
        AutoScaleMode = AutoScaleMode.Dpi;

        _webView.Dock = DockStyle.Fill;
        Controls.Add(_webView);

        Shown += async (_, _) => await InitializeWebViewAsync();
        FormClosing += (_, _) =>
        {
            _uiTimer.Stop();
            _controller.Dispose();
        };

        _uiTimer.Interval = 33; // UI ~= 30fps. Sensor/logging remain independent.
        _uiTimer.Tick += (_, _) => PublishSnapshot();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Therabby",
                    "LoadBalanceViewer",
                    "WebView2"));

            await _webView.EnsureCoreWebView2Async(env);

            var core = _webView.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsWebMessageEnabled = true;

            core.WebMessageReceived += OnWebMessageReceived;
            core.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase)
                    && e.Uri != "about:blank")
                {
                    e.Cancel = true;
                }
            };

            _webView.Source = new Uri("about:blank");
            _webView.NavigateToString(BuildEmbeddedHtml());
            _uiTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "WebView2の初期化に失敗しました。\r\n\r\n" +
                ex.Message +
                "\r\n\r\nMicrosoft Edge WebView2 Runtimeがインストールされているか確認してください。",
                "Therabby Load Balance Viewer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async void OnWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement)) return;
            var type = typeElement.GetString() ?? "";

            switch (type)
            {
                case "ready":
                    _webReady = true;
                    PublishSnapshot();

                    if (!_autoConnectAttempted)
                    {
                        _autoConnectAttempted = true;
                        await _controller.SetModeAsync("real");
                        PublishSnapshot();

                        // Convenience only: failed auto-connect is silent.
                        await _controller.ConnectRealAsync(silent: true);
                        PublishSnapshot();
                    }
                    break;

                case "mode":
                    if (root.TryGetProperty("mode", out var modeEl))
                    {
                        await _controller.SetModeAsync(modeEl.GetString() ?? "real");
                        PublishSnapshot();
                    }
                    break;

                case "connect":
                    await _controller.ConnectRealAsync();
                    PublishSnapshot();
                    break;

                case "zero":
                    await _controller.ZeroAsync();
                    PublishSnapshot();
                    break;

                case "center":
                    _controller.SetCenter();
                    PublishSnapshot();
                    break;

                case "toggleLog":
                    _controller.ToggleLog();
                    PublishSnapshot();
                    break;

                case "mockWeight":
                    if (root.TryGetProperty("value", out var weightEl)
                        && weightEl.TryGetDouble(out var weight))
                    {
                        _controller.SetMockWeight(weight);
                    }
                    break;

                case "mockPose":
                    if (root.TryGetProperty("x", out var xEl)
                        && root.TryGetProperty("y", out var yEl)
                        && xEl.TryGetDouble(out var x)
                        && yEl.TryGetDouble(out var y))
                    {
                        _controller.SetMockPose(x, y);
                    }
                    break;

                case "openLogs":
                    Directory.CreateDirectory(_controller.LogsDirectory);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _controller.LogsDirectory,
                        UseShellExecute = true
                    });
                    break;
            }
        }
        catch
        {
            // Bad web message must never crash sensor acquisition.
        }
    }

    private void PublishSnapshot()
    {
        if (!_webReady || _webView.CoreWebView2 is null) return;

        try
        {
            var json = JsonSerializer.Serialize(
                _controller.GetSnapshot(),
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

            _webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch
        {
            // UI rendering is deliberately isolated from WBB acquisition.
        }
    }

    private static string BuildEmbeddedHtml()
    {
        var html = ReadEmbeddedText("ui.index.html");
        var css = ReadEmbeddedText("ui.styles.css");
        var js = ReadEmbeddedText("ui.app.js");
        var png = ReadEmbeddedBytes("ui.wii-footprints.png");
        var footprintData = "data:image/png;base64," + Convert.ToBase64String(png);

        return html
            .Replace("__INLINE_CSS__", css)
            .Replace("__INLINE_JS__", js)
            .Replace("__FOOTPRINT_DATA__", footprintData);
    }

    private static string ReadEmbeddedText(string suffix)
    {
        using var stream = OpenEmbedded(suffix);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] ReadEmbeddedBytes(string suffix)
    {
        using var stream = OpenEmbedded(suffix);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static Stream OpenEmbedded(string suffix)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        if (name is null)
            throw new InvalidOperationException($"Embedded resource not found: {suffix}");

        return asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource stream not found: {suffix}");
    }
}
