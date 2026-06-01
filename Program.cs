// Program.cs
// .NET WinForms single-file sample
// Required NuGet:
//   Microsoft.Web.WebView2
//
// Recommended csproj settings:
// <Project Sdk="Microsoft.NET.Sdk">
//   <PropertyGroup>
//     <OutputType>WinExe</OutputType>
//     <TargetFramework>net8.0-windows</TargetFramework>
//     <UseWindowsForms>true</UseWindowsForms>
//     <Nullable>enable</Nullable>
//   </PropertyGroup>
//   <ItemGroup>
//     <PackageReference Include="Microsoft.Web.WebView2" Version="1.*" />
//   </ItemGroup>
// </Project>

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.ComponentModel;

namespace IzCommentViewer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class MainForm : Form
{
    private readonly AppSettings _settings = AppSettings.Load();
    private const string DefaultVoicevoxUrl = "http://127.0.0.1:50021";
    private readonly TextBox _voicevoxUrlBox = new() { Text = DefaultVoicevoxUrl, Width = 220 };
    private readonly Button _resetVoicevoxUrlButton = new() { Text = "URL初期化", Width = 85, Height = 26 };
    private readonly NumericUpDown _speakerBox = new() { Minimum = 0, Maximum = 999, Value = 3, Width = 70 };
    private readonly CheckBox _voiceEnabledBox = new() { Text = "VOICEVOX読み上げ", Checked = true, AutoSize = true };
    private readonly CheckBox _dedupeBox = new() { Text = "重複コメント無視", Checked = true, AutoSize = true };
    private readonly CheckBox _readNameBox = new() { Text = "名前も読む", Checked = false, AutoSize = true };

    private readonly TextBox _youtubeUrlBox = new() { Width = 430, PlaceholderText = "YouTube配信URL or videoId" };
    private readonly Button _youtubeConnectButton = new() { Text = "YouTube接続", Width = 110 };
    private readonly Button _youtubeDisconnectButton = new() { Text = "切断", Width = 70, Enabled = false };
    private readonly ListView _youtubeList = CreateChatListView();
    private readonly WebView2 _youtubeWebView = new() { Dock = DockStyle.Fill, Visible = false }; // YouTubeコメント取得用の内部WebView。UIには表示しない。

    private readonly TextBox _twitchChannelBox = new() { Width = 260, PlaceholderText = "Twitch配信URL or Twitchチャンネル名" };
    private readonly Button _twitchConnectButton = new() { Text = "Twitch接続", Width = 110 };
    private readonly Button _twitchDisconnectButton = new() { Text = "切断", Width = 70, Enabled = false };
    private readonly ListView _twitchList = CreateChatListView();
    private readonly ListView _allChatList = CreateAllChatListView();

    private readonly FlowLayoutPanel _youtubeCaptureFlow = CreateCaptureFlow();
    private readonly FlowLayoutPanel _twitchCaptureFlow = CreateCaptureFlow();

    private readonly CheckBox _captureShowNameBox = new() { Text = "ブラウザで名前を表示", Checked = true, AutoSize = true };
    private readonly CheckBox _showIconBox = new() { Text = "アイコン表示", Checked = true, AutoSize = true };
    private readonly NumericUpDown _iconSizeBox = new() { Minimum = 16, Maximum = 128, Value = 40, Width = 70 };
    private readonly NumericUpDown _captureFontSizeBox = new() { Minimum = 10, Maximum = 72, Value = 18, Width = 70 };
    private readonly Button _selectCaptureFontButton = new() { Text = "フォント選択", Width = 110 };
    private readonly Label _captureFontLabel = new() { Text = "Meiryo UI", AutoSize = true, Padding = new Padding(0, 6, 0, 0) };

    private readonly CheckBox _saveLogBox = new() { Text = "コメントログ保存", Checked = true, AutoSize = true };
    private readonly CheckBox _fastModeBox = new() { Text = "高速モード", Checked = true, AutoSize = true };
    private readonly Button _openUserLogButton = new() { Text = "ユーザーログ", Width = 100 };
    private readonly Button _openLogFolderButton = new() { Text = "設定/ログ 保存フォルダを開く", Width = 190 };

    private readonly CheckBox _overlayEnableBox = new() { Text = "ブラウザソース有効化", Checked = false, AutoSize = true };
    private readonly NumericUpDown _overlayPortBox = new() { Minimum = 49152, Maximum = 65535, Value = 51766, Width = 85 };
    private readonly CheckBox _overlayAutoPortBox = new() { Text = "空きポート自動", Checked = true, AutoSize = true };
    private readonly Button _copyYoutubeOverlayUrlButton = new() { Text = "YoutubeコメントURL", Width = 140 };
    private readonly Button _copyTwitchOverlayUrlButton = new() { Text = "TwitchコメントURL", Width = 140 };
    private readonly Label _overlayUrlLabel = new() { Text = "ブラウザソース: 無効", AutoSize = true, Padding = new Padding(0, 6, 0, 0) };

    private string _captureFontFamily = "Meiryo UI";
    private CaptureForm? _captureForm;
    private UserLogForm? _userLogForm;
    private readonly ChatLogStore _chatLogStore = new();
    private readonly ChatAvatarCache _avatarCache = new();

    private OverlayServer? _overlayServer;
    private CancellationTokenSource? _overlayCts;

    private readonly Label _statusLabel = new() { AutoSize = true, Text = "Ready" };
    private readonly VoicevoxClient _voicevoxClient = new();
    private readonly ConcurrentDictionary<string, byte> _seen = new();
    private readonly ConcurrentQueue<ChatMessage> _uiMessageQueue = new();
    private readonly System.Windows.Forms.Timer _uiFlushTimer = new() { Interval = 50 };
    private readonly ConcurrentQueue<ChatMessage> _logMessageQueue = new();
    private readonly System.Windows.Forms.Timer _logFlushTimer = new() { Interval = 1000 };

    private YoutubeWebViewChatSource? _youtubeSource;
    private TwitchIrcChatSource? _twitchSource;
    private CancellationTokenSource? _youtubeCts;
    private CancellationTokenSource? _twitchCts;

    public MainForm()
    {
        Text = "IzCommentViewer - YouTube / Twitch Comment Viewer";
        Width = 1220;
        Height = 820;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Meiryo UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(BuildSettingsPanel(), 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildAllChatTab());
        tabs.TabPages.Add(BuildYoutubeTab());
        tabs.TabPages.Add(BuildTwitchTab());
        root.Controls.Add(tabs, 0, 1);

        var bottom = new Panel { Dock = DockStyle.Fill, Height = 30, Padding = new Padding(8, 5, 8, 5) };
        bottom.Controls.Add(_statusLabel);
        root.Controls.Add(bottom, 0, 2);

        ApplySettingsToUi();

        _resetVoicevoxUrlButton.Click += (_, _) =>
        {
            _voicevoxUrlBox.Text = DefaultVoicevoxUrl;
            SetStatus("VOICEVOX URLをデフォルトに戻しました: " + DefaultVoicevoxUrl);
        };

        _youtubeConnectButton.Click += async (_, _) => await ConnectYoutubeAsync();
        _youtubeDisconnectButton.Click += (_, _) => DisconnectYoutube();
        _twitchConnectButton.Click += async (_, _) => await ConnectTwitchAsync();
        _twitchDisconnectButton.Click += (_, _) => DisconnectTwitch();
        _openUserLogButton.Click += (_, _) => OpenUserLogWindow();
        _openLogFolderButton.Click += (_, _) => OpenLogFolder();
        _selectCaptureFontButton.Click += (_, _) => SelectCaptureFont();
        _captureFontSizeBox.ValueChanged += (_, _) => ApplyCaptureFontEverywhere();
        _showIconBox.CheckedChanged += (_, _) => ApplyIconSettingsEverywhere();
        _iconSizeBox.ValueChanged += (_, _) => ApplyIconSettingsEverywhere();
        _overlayEnableBox.CheckedChanged += async (_, _) => await ToggleOverlayServerAsync();
        _copyYoutubeOverlayUrlButton.Click += (_, _) => CopyOverlayUrl("youtube");
        _copyTwitchOverlayUrlButton.Click += (_, _) => CopyOverlayUrl("twitch");
        _uiFlushTimer.Tick += (_, _) => FlushUiMessageQueue();
        _uiFlushTimer.Start();
        _logFlushTimer.Tick += (_, _) => FlushLogQueue();
        _logFlushTimer.Start();

        if (_overlayEnableBox.Checked)
        {
            _ = StartOverlayServerAsync();
        }

        FormClosing += (_, _) =>
        {
            try { SaveSettingsFromUi(); } catch { }
            try { _uiFlushTimer.Stop(); } catch { }
            try { _logFlushTimer.Stop(); } catch { }
            try { FlushLogQueue(); } catch { }
            try { _userLogForm?.Close(); } catch { }
            StopOverlayServer();
            DisconnectYoutube();
            DisconnectTwitch();
            _voicevoxClient.Dispose();
            _avatarCache.Dispose();
        };
    }

    private Control BuildSettingsPanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8, 6, 8, 4),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildVoicevoxSettingsRow(), 0, 0);
        root.Controls.Add(BuildBrowserAppearanceRow(), 0, 1);
        root.Controls.Add(BuildOverlaySettingsRow(), 0, 2);
        root.Controls.Add(BuildLogAndUtilityRow(), 0, 3);

        return root;
    }

    private Control BuildVoicevoxSettingsRow()
    {
        var row = CreateSettingsRow();
        row.Controls.Add(CreateSectionLabel("VOICEVOX"));
        row.Controls.Add(CreateSmallLabel("URL"));
        row.Controls.Add(_voicevoxUrlBox);
        row.Controls.Add(_resetVoicevoxUrlButton);
        row.Controls.Add(CreateSmallLabel("話者ID"));
        row.Controls.Add(_speakerBox);
        row.Controls.Add(_voiceEnabledBox);
        row.Controls.Add(_readNameBox);

        var testButton = new Button { Text = "読み上げテスト", Width = 120, Height = 26, Margin = new Padding(8, 2, 3, 2) };
        testButton.Click += async (_, _) => await SpeakAsync("VOICEVOXのテストです。", "System");
        row.Controls.Add(testButton);
        return row;
    }

    private Control BuildBrowserAppearanceRow()
    {
        var row = CreateSettingsRow();
        row.Controls.Add(CreateSectionLabel("ブラウザ表示"));
        row.Controls.Add(_captureShowNameBox);
        row.Controls.Add(_showIconBox);
        row.Controls.Add(CreateSmallLabel("アイコン"));
        row.Controls.Add(_iconSizeBox);
        row.Controls.Add(CreateSmallLabel("フォント"));
        row.Controls.Add(_captureFontSizeBox);
        row.Controls.Add(_selectCaptureFontButton);
        row.Controls.Add(_captureFontLabel);
        return row;
    }

    private Control BuildOverlaySettingsRow()
    {
        var row = CreateSettingsRow();
        row.Controls.Add(CreateSectionLabel("OBSブラウザソース"));
        row.Controls.Add(_overlayEnableBox);
        row.Controls.Add(CreateSmallLabel("ブラウザソースURLをクリップボードにコピー:"));
        row.Controls.Add(CreateSmallLabel("Port"));
        row.Controls.Add(_overlayPortBox);
        row.Controls.Add(_overlayAutoPortBox);
        row.Controls.Add(_copyYoutubeOverlayUrlButton);
        row.Controls.Add(_copyTwitchOverlayUrlButton);
        return row;
    }

    private Control BuildLogAndUtilityRow()
    {
        var row = CreateSettingsRow();
        row.Controls.Add(CreateSectionLabel("ログ/処理"));
        row.Controls.Add(_dedupeBox);
        row.Controls.Add(_saveLogBox);
        row.Controls.Add(_fastModeBox);
        row.Controls.Add(_openUserLogButton);
        row.Controls.Add(_openLogFolderButton);
        return row;
    }

    private static FlowLayoutPanel CreateSettingsRow()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 1, 0, 1),
            Margin = new Padding(0, 0, 0, 2),
        };
    }

    private static Label CreateSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Width = 120,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Meiryo UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 40, 40),
            Padding = new Padding(0, 2, 0, 0),
            Margin = new Padding(0, 2, 8, 2),
        };
    }

    private static Label CreateSmallLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 5, 0, 0),
            Margin = new Padding(8, 2, 3, 2),
        };
    }

    private TabPage BuildAllChatTab()
    {
        var page = new TabPage("統合コメント");
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(root);

        var info = new Label
        {
            Text = "YouTube / Twitch のコメントを時系列でまとめて表示します。新しいコメントへ自動スクロールします。",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 6),
        };
        root.Controls.Add(info, 0, 0);
        root.Controls.Add(_allChatList, 0, 1);
        return page;
    }

    private TabPage BuildYoutubeTab()
    {
        var page = new TabPage("YouTube");
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(root);

        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        top.Controls.Add(new Label { Text = "URL / videoId", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        top.Controls.Add(_youtubeUrlBox);
        top.Controls.Add(_youtubeConnectButton);
        top.Controls.Add(_youtubeDisconnectButton);
        root.Controls.Add(top, 0, 0);

        root.Controls.Add(_youtubeList, 0, 1);

        // _youtubeWebView はYouTubeコメント取得用の内部WebView。
        // 表示UIには載せず、フォームの非表示コントロールとして保持する。
        if (_youtubeWebView.Parent == null)
        {
            _youtubeWebView.Width = 1;
            _youtubeWebView.Height = 1;
            _youtubeWebView.Left = -10000;
            _youtubeWebView.Top = -10000;
            Controls.Add(_youtubeWebView);
        }

        return page;
    }

    private TabPage BuildTwitchTab()
    {
        var page = new TabPage("Twitch");
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(root);

        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        top.Controls.Add(new Label { Text = "チャンネル", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        top.Controls.Add(_twitchChannelBox);
        top.Controls.Add(_twitchConnectButton);
        top.Controls.Add(_twitchDisconnectButton);
        root.Controls.Add(top, 0, 0);

        root.Controls.Add(_twitchList, 0, 1);
        return page;
    }

    private static Control BuildCaptureColumn(string title, FlowLayoutPanel flow, Color accent)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.FromArgb(18, 18, 22),
            Margin = new Padding(8),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Height = 46,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Meiryo UI", 18F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = accent,
        };

        panel.Controls.Add(header, 0, 0);
        panel.Controls.Add(flow, 0, 1);
        return panel;
    }

    private void SelectCaptureFont()
    {
        using var dialog = new FontDialog
        {
            Font = new Font(_captureFontFamily, (float)_captureFontSizeBox.Value, FontStyle.Bold),
            ShowEffects = false,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _captureFontFamily = dialog.Font.FontFamily.Name;
        _captureFontSizeBox.Value = Math.Max(
            _captureFontSizeBox.Minimum,
            Math.Min(_captureFontSizeBox.Maximum, (decimal)dialog.Font.Size)
        );
        _captureFontLabel.Text = _captureFontFamily;
        ApplyCaptureFontEverywhere();
    }

    private void ApplyCaptureFontEverywhere()
    {
        ApplyCaptureFontToExistingBubbles(_youtubeCaptureFlow);
        ApplyCaptureFontToExistingBubbles(_twitchCaptureFlow);

        if (_captureForm != null && !_captureForm.IsDisposed)
        {
            _captureForm.CaptureFontFamilyName = _captureFontFamily;
            _captureForm.CaptureFontSize = (float)_captureFontSizeBox.Value;
            _captureForm.ApplyFontToExistingBubbles();
        }
    }

    private void ApplyCaptureFontToExistingBubbles(FlowLayoutPanel flow)
    {
        foreach (Control c in flow.Controls)
        {
            foreach (var label in c.Controls.OfType<Label>().Where(x => x.Name == "CommentText"))
            {
                label.Font = new Font(_captureFontFamily, (float)_captureFontSizeBox.Value, FontStyle.Bold);
            }
            RecalculateBubbleLayout(c);
        }
        ScrollFlowToBottom(flow);
    }

    private void ApplyIconSettingsEverywhere()
    {
        foreach (Control c in _youtubeCaptureFlow.Controls) ApplyIconSettingsToBubble(c);
        foreach (Control c in _twitchCaptureFlow.Controls) ApplyIconSettingsToBubble(c);

        if (_captureForm != null && !_captureForm.IsDisposed)
        {
            _captureForm.CaptureShowIcon = _showIconBox.Checked;
            _captureForm.CaptureIconSize = (int)_iconSizeBox.Value;
            _captureForm.ApplyIconSettingsToExistingBubbles();
        }
    }

    private void ApplyIconSettingsToBubble(Control bubble)
    {
        RecalculateBubbleLayout(bubble);
    }

    private void OpenCaptureWindow()
    {
        if (_captureForm == null || _captureForm.IsDisposed)
        {
            _captureForm = new CaptureForm(_avatarCache);
            _captureForm.FormClosed += (_, _) => _captureForm = null;
        }

        _captureForm.CaptureFontFamilyName = _captureFontFamily;
        _captureForm.CaptureFontSize = (float)_captureFontSizeBox.Value;
        _captureForm.CaptureShowName = _captureShowNameBox.Checked;
        _captureForm.CaptureShowIcon = _showIconBox.Checked;
        _captureForm.CaptureIconSize = (int)_iconSizeBox.Value;
        _captureForm.ApplyFontToExistingBubbles();
        _captureForm.ApplyIconSettingsToExistingBubbles();
        _captureForm.Show();
        _captureForm.BringToFront();
    }

    private void OpenUserLogWindow()
    {
        if (_userLogForm == null || _userLogForm.IsDisposed)
        {
            _userLogForm = new UserLogForm(_chatLogStore);
            _userLogForm.FormClosed += (_, _) => _userLogForm = null;
        }

        _userLogForm.Show();
        _userLogForm.BringToFront();
        _userLogForm.RefreshUsers();
    }

    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.SettingsDirectory);
            Directory.CreateDirectory(_chatLogStore.BaseDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppSettings.SettingsDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "保存フォルダを開けませんでした", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ToggleOverlayServerAsync()
    {
        if (_overlayEnableBox.Checked)
        {
            await StartOverlayServerAsync();
        }
        else
        {
            StopOverlayServer();
        }
    }

    private async Task StartOverlayServerAsync()
    {
        StopOverlayServer();

        try
        {
            var requestedPort = (int)_overlayPortBox.Value;
            var port = _overlayAutoPortBox.Checked ? FindAvailablePort(requestedPort, Math.Min(65535, requestedPort + 84)) : requestedPort;
            _overlayPortBox.Value = port;

            _overlayCts = new CancellationTokenSource();
            _overlayServer = new OverlayServer(port, GetOverlayAppearance);
            _overlayServer.Start(_overlayCts.Token);
            SetStatus($"ブラウザソースを有効化しました。YoutubeコメントURL / TwitchコメントURL ボタンからOBS用URLをコピーできます。ポート: {port}");
        }
        catch (Exception ex)
        {
            _overlayEnableBox.Checked = false;
            StopOverlayServer();
            MessageBox.Show(ex.Message, "ブラウザソース開始失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        await Task.CompletedTask;
    }

    private void StopOverlayServer()
    {
        try { _overlayCts?.Cancel(); } catch { }
        try { _overlayServer?.Dispose(); } catch { }
        try { _overlayCts?.Dispose(); } catch { }
        _overlayCts = null;
        _overlayServer = null;
    }

    private OverlayAppearance GetOverlayAppearance()
    {
        return new OverlayAppearance
        {
            FontFamily = _captureFontFamily,
            FontSize = (int)_captureFontSizeBox.Value,
            IconSize = (int)_iconSizeBox.Value,
            ShowIcon = _showIconBox.Checked,
            ShowName = _captureShowNameBox.Checked,
        };
    }

    private void ApplySettingsToUi()
    {
        _voicevoxUrlBox.Text = string.IsNullOrWhiteSpace(_settings.VoicevoxUrl) ? DefaultVoicevoxUrl : _settings.VoicevoxUrl;
        _speakerBox.Value = ClampDecimal(_settings.Speaker, _speakerBox.Minimum, _speakerBox.Maximum);
        _voiceEnabledBox.Checked = _settings.VoiceEnabled;
        _dedupeBox.Checked = _settings.DedupeEnabled;
        _readNameBox.Checked = _settings.ReadName;
        _captureShowNameBox.Checked = _settings.CaptureShowName;
        _showIconBox.Checked = _settings.ShowIcon;
        _iconSizeBox.Value = ClampDecimal(_settings.IconSize, _iconSizeBox.Minimum, _iconSizeBox.Maximum);
        _captureFontSizeBox.Value = ClampDecimal(_settings.CaptureFontSize, _captureFontSizeBox.Minimum, _captureFontSizeBox.Maximum);
        _captureFontFamily = string.IsNullOrWhiteSpace(_settings.CaptureFontFamily) ? "Meiryo UI" : _settings.CaptureFontFamily;
        _captureFontLabel.Text = _captureFontFamily;
        _saveLogBox.Checked = _settings.SaveLog;
        _fastModeBox.Checked = _settings.FastMode;
        _overlayEnableBox.Checked = _settings.OverlayEnabled;
        _overlayPortBox.Value = ClampDecimal(_settings.OverlayPort, _overlayPortBox.Minimum, _overlayPortBox.Maximum);
        _overlayAutoPortBox.Checked = _settings.OverlayAutoPort;
    }

    private void SaveSettingsFromUi()
    {
        _settings.VoicevoxUrl = _voicevoxUrlBox.Text.Trim();
        _settings.Speaker = (int)_speakerBox.Value;
        _settings.VoiceEnabled = _voiceEnabledBox.Checked;
        _settings.DedupeEnabled = _dedupeBox.Checked;
        _settings.ReadName = _readNameBox.Checked;
        _settings.CaptureShowName = _captureShowNameBox.Checked;
        _settings.ShowIcon = _showIconBox.Checked;
        _settings.IconSize = (int)_iconSizeBox.Value;
        _settings.CaptureFontSize = (int)_captureFontSizeBox.Value;
        _settings.CaptureFontFamily = _captureFontFamily;
        _settings.SaveLog = _saveLogBox.Checked;
        _settings.FastMode = _fastModeBox.Checked;
        _settings.OverlayEnabled = _overlayEnableBox.Checked;
        _settings.OverlayPort = (int)_overlayPortBox.Value;
        _settings.OverlayAutoPort = _overlayAutoPortBox.Checked;
        _settings.Save();
    }

    private static decimal ClampDecimal(int value, decimal min, decimal max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private void CopyOverlayUrl(string mode)
    {
        var port = _overlayServer?.Port ?? (int)_overlayPortBox.Value;
        var url = mode switch
        {
            "youtube" => $"http://127.0.0.1:{port}/overlay/youtube",
            "twitch" => $"http://127.0.0.1:{port}/overlay/twitch",
            _ => $"http://127.0.0.1:{port}/overlay/youtube",
        };

        try
        {
            Clipboard.SetText(url);
            SetStatus("コピーしました: " + url);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "コピー失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static int FindAvailablePort(int startPort, int endPort)
    {
        for (var port = startPort; port <= endPort; port++)
        {
            if (IsPortAvailable(port)) return port;
        }
        throw new InvalidOperationException("使用可能なポートが見つかりませんでした。");
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ConnectYoutubeAsync()
    {
        var input = _youtubeUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            MessageBox.Show("YouTubeの配信URLかvideoIdを入力してください。", "YouTube", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DisconnectYoutube();
        _youtubeCts = new CancellationTokenSource();
        _youtubeSource = new YoutubeWebViewChatSource(_youtubeWebView, input);
        _youtubeSource.MessageReceived += OnChatMessage;
        _youtubeSource.StatusChanged += SetStatus;

        _youtubeConnectButton.Enabled = false;
        _youtubeDisconnectButton.Enabled = true;

        try
        {
            await _youtubeSource.StartAsync(_youtubeCts.Token);
            SetStatus("YouTube接続中");
        }
        catch (Exception ex)
        {
            SetStatus("YouTube接続失敗: " + ex.Message);
            DisconnectYoutube();
            MessageBox.Show(ex.Message, "YouTube接続失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DisconnectYoutube()
    {
        try
        {
            _youtubeCts?.Cancel();
            _youtubeSource?.Stop();
        }
        catch { }
        finally
        {
            if (_youtubeSource != null)
            {
                _youtubeSource.MessageReceived -= OnChatMessage;
                _youtubeSource.StatusChanged -= SetStatus;
            }
            _youtubeSource = null;
            _youtubeCts?.Dispose();
            _youtubeCts = null;
            _youtubeConnectButton.Enabled = true;
            _youtubeDisconnectButton.Enabled = false;
        }
    }

    private async Task ConnectTwitchAsync()
    {
        var channel = NormalizeTwitchChannel(_twitchChannelBox.Text);
        if (string.IsNullOrWhiteSpace(channel))
        {
            MessageBox.Show("Twitchチャンネル名を入力してください。", "Twitch", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DisconnectTwitch();
        _twitchCts = new CancellationTokenSource();
        _twitchSource = new TwitchIrcChatSource(channel);
        _twitchSource.MessageReceived += OnChatMessage;
        _twitchSource.StatusChanged += SetStatus;

        _twitchConnectButton.Enabled = false;
        _twitchDisconnectButton.Enabled = true;

        try
        {
            await _twitchSource.StartAsync(_twitchCts.Token);
            SetStatus("Twitch接続中: #" + channel);
        }
        catch (Exception ex)
        {
            SetStatus("Twitch接続失敗: " + ex.Message);
            DisconnectTwitch();
            MessageBox.Show(ex.Message, "Twitch接続失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DisconnectTwitch()
    {
        try
        {
            _twitchCts?.Cancel();
            _twitchSource?.Stop();
        }
        catch { }
        finally
        {
            if (_twitchSource != null)
            {
                _twitchSource.MessageReceived -= OnChatMessage;
                _twitchSource.StatusChanged -= SetStatus;
            }
            _twitchSource = null;
            _twitchCts?.Dispose();
            _twitchCts = null;
            _twitchConnectButton.Enabled = true;
            _twitchDisconnectButton.Enabled = false;
        }
    }


    private void OnChatMessage(ChatMessage message)
    {
        if (IsDisposed) return;

        if (_dedupeBox.Checked)
        {
            var key = $"{message.Platform}:{message.UserName}:{message.Text}";
            if (!_seen.TryAdd(key, 0)) return;
            if (_seen.Count > 5000) _seen.Clear();
        }

        // 重い処理はUIスレッドに載せない。UI反映はタイマーでまとめて処理する。
        _uiMessageQueue.Enqueue(message);
        _overlayServer?.Broadcast(message);

        if (_saveLogBox.Checked)
        {
            _logMessageQueue.Enqueue(message);
        }

        if (_voiceEnabledBox.Checked)
        {
            // 流速が速いときは読み上げを間引く。VOICEVOXのキューが詰まると全体の体感が悪くなるため。
            if (!_fastModeBox.Checked || _voicevoxClient.PendingCount < 8)
            {
                var text = _readNameBox.Checked ? $"{message.UserName}さん。{message.Text}" : message.Text;
                _ = SpeakAsync(text, message.Platform);
            }
        }
    }

    private void FlushUiMessageQueue()
    {
        if (IsDisposed) return;
        if (_uiMessageQueue.IsEmpty) return;

        var maxPerTick = _fastModeBox.Checked ? 160 : 80;
        var processed = 0;
        var updatedUserLog = false;

        _allChatList.BeginUpdate();
        _youtubeList.BeginUpdate();
        _twitchList.BeginUpdate();
        try
        {
            while (processed < maxPerTick && _uiMessageQueue.TryDequeue(out var message))
            {
                AddToAllList(_allChatList, message);
                AddToList(message.Platform == "YouTube" ? _youtubeList : _twitchList, message);
                updatedUserLog = true;
                processed++;
            }
        }
        finally
        {
            _allChatList.EndUpdate();
            _youtubeList.EndUpdate();
            _twitchList.EndUpdate();
        }

        if (updatedUserLog && !_fastModeBox.Checked)
        {
            _userLogForm?.RefreshUsers(keepSelection: true);
        }

        if (!_uiMessageQueue.IsEmpty)
        {
            SetStatus($"コメント処理中: 残り {_uiMessageQueue.Count} 件");
        }
    }

    private void FlushLogQueue()
    {
        if (_logMessageQueue.IsEmpty) return;

        var batch = new List<ChatMessage>();
        var max = _fastModeBox.Checked ? 1000 : 300;
        while (batch.Count < max && _logMessageQueue.TryDequeue(out var msg))
        {
            batch.Add(msg);
        }

        if (batch.Count == 0) return;

        _ = Task.Run(() =>
        {
            try
            {
                _chatLogStore.SaveBatch(batch);
            }
            catch (Exception ex)
            {
                SetStatus("ログ保存エラー: " + ex.Message);
            }
        });
    }

    private async Task SpeakAsync(string text, string platform)
    {
        try
        {
            _voicevoxClient.BaseUrl = _voicevoxUrlBox.Text.Trim().TrimEnd('/');
            _voicevoxClient.Speaker = (int)_speakerBox.Value;
            await _voicevoxClient.EnqueueAsync(CleanForSpeech(text));
        }
        catch (Exception ex)
        {
            SetStatus($"VOICEVOXエラー({platform}): {ex.Message}");
        }
    }

    private static string CleanForSpeech(string text)
    {
        text = Regex.Replace(text, @"https?://\S+", " URL ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length > 120) text = text[..120] + "、以下略";
        return text;
    }

    private static void AddToList(ListView list, ChatMessage msg)
    {
        var item = new ListViewItem(msg.Time.ToString("HH:mm:ss"));
        item.SubItems.Add(msg.UserName);
        item.SubItems.Add(msg.Text);
        list.Items.Add(item);
        while (list.Items.Count > 300) list.Items.RemoveAt(0);
        EnsureLastVisible(list);
    }

    private static void AddToAllList(ListView list, ChatMessage msg)
    {
        var item = new ListViewItem(msg.Time.ToString("HH:mm:ss"));
        item.SubItems.Add(msg.Platform);
        item.SubItems.Add(msg.UserName);
        item.SubItems.Add(msg.Text);
        list.Items.Add(item);
        while (list.Items.Count > 500) list.Items.RemoveAt(0);
        EnsureLastVisible(list);
    }

    private static void EnsureLastVisible(ListView list)
    {
        if (list.Items.Count <= 0) return;
        var last = list.Items[list.Items.Count - 1];
        last.EnsureVisible();
        list.TopItem = Math.Max(0, list.Items.Count - 1) >= 0 ? list.Items[Math.Max(0, list.Items.Count - 1)] : null;
    }

    private void AddToCaptureView(ChatMessage msg)
    {
        var flow = msg.Platform == "YouTube" ? _youtubeCaptureFlow : _twitchCaptureFlow;
        var bubble = CreateCaptureBubble(
            msg,
            flow.ClientSize.Width,
            _captureFontFamily,
            (float)_captureFontSizeBox.Value,
            _captureShowNameBox.Checked,
            _showIconBox.Checked,
            (int)_iconSizeBox.Value,
            _avatarCache
        );

        flow.Controls.Add(bubble);
        while (flow.Controls.Count > 30)
        {
            var first = flow.Controls[0];
            flow.Controls.RemoveAt(0);
            first.Dispose();
        }
        ScrollFlowToBottom(flow);
    }

    public static Panel CreateCaptureBubble(ChatMessage msg, int flowWidth, string fontFamily, float fontSize, bool showName, bool showIcon, int iconSize, ChatAvatarCache avatarCache)
    {
        var width = Math.Max(240, flowWidth - 30);
        var text = showName ? msg.UserName + ": " + msg.Text : msg.Text;
        var padding = 10;
        var gap = showIcon ? 10 : 0;
        var visibleIconSize = showIcon ? iconSize : 0;
        var textX = padding + visibleIconSize + gap;
        var textWidth = Math.Max(80, width - textX - padding);

        using var measureFont = new Font(fontFamily, fontSize, FontStyle.Bold);
        var measured = TextRenderer.MeasureText(
            text,
            measureFont,
            new Size(textWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl
        );
        var contentHeight = Math.Max(showIcon ? iconSize : 0, measured.Height);
        var height = Math.Max(44, contentHeight + padding * 2);

        var bubble = new Panel
        {
            Width = width,
            Height = height,
            AutoSize = false,
            BackColor = msg.Platform == "YouTube" ? Color.FromArgb(60, 26, 26) : Color.FromArgb(42, 32, 62),
            Padding = new Padding(0),
            Margin = new Padding(0, 0, 0, 8),
            Tag = msg.Platform,
        };

        var icon = new PictureBox
        {
            Name = "AvatarIcon",
            Width = iconSize,
            Height = iconSize,
            Left = padding,
            Top = padding,
            SizeMode = PictureBoxSizeMode.Zoom,
            Visible = showIcon,
            Image = avatarCache.GetAvatar(msg, iconSize),
        };

        var label = new Label
        {
            Name = "CommentText",
            AutoSize = false,
            Left = textX,
            Top = padding,
            Width = textWidth,
            Height = measured.Height + 4,
            Text = text,
            Font = new Font(fontFamily, fontSize, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = bubble.BackColor,
            Padding = new Padding(0),
            Margin = new Padding(0),
        };

        bubble.Controls.Add(icon);
        bubble.Controls.Add(label);
        return bubble;
    }

    private void RecalculateBubbleLayout(Control control)
    {
        if (control is not Panel panel) return;

        var iconSize = (int)_iconSizeBox.Value;
        var showIcon = _showIconBox.Checked;
        var padding = 10;
        var gap = showIcon ? 10 : 0;
        var visibleIconSize = showIcon ? iconSize : 0;
        panel.Width = Math.Max(240, panel.Parent?.ClientSize.Width - 30 ?? panel.Width);

        var icon = panel.Controls.OfType<PictureBox>().FirstOrDefault(x => x.Name == "AvatarIcon");
        var label = panel.Controls.OfType<Label>().FirstOrDefault(x => x.Name == "CommentText");
        if (label == null) return;

        if (icon != null)
        {
            icon.Visible = showIcon;
            icon.Width = iconSize;
            icon.Height = iconSize;
            icon.Left = padding;
            icon.Top = padding;
        }

        var textX = padding + visibleIconSize + gap;
        var textWidth = Math.Max(80, panel.Width - textX - padding);
        label.Left = textX;
        label.Top = padding;
        label.Width = textWidth;
        label.Font = new Font(_captureFontFamily, (float)_captureFontSizeBox.Value, FontStyle.Bold);

        var measured = TextRenderer.MeasureText(
            label.Text,
            label.Font,
            new Size(textWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl
        );
        label.Height = measured.Height + 4;

        var contentHeight = Math.Max(showIcon ? iconSize : 0, label.Height);
        panel.Height = Math.Max(44, contentHeight + padding * 2);
    }

    private void SetStatus(string text)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(text));
            return;
        }
        _statusLabel.Text = text;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ResizeCaptureBubbles(_youtubeCaptureFlow);
        ResizeCaptureBubbles(_twitchCaptureFlow);
    }

    private void ResizeCaptureBubbles(FlowLayoutPanel flow)
    {
        var width = Math.Max(200, flow.ClientSize.Width - 30);
        foreach (Control c in flow.Controls)
        {
            c.Width = width;
            RecalculateBubbleLayout(c);
        }
        ScrollFlowToBottom(flow);
    }

    private static void ScrollFlowToBottom(FlowLayoutPanel flow)
    {
        if (flow.Controls.Count <= 0) return;
        var last = flow.Controls[flow.Controls.Count - 1];
        flow.ScrollControlIntoView(last);
        flow.VerticalScroll.Value = flow.VerticalScroll.Maximum;
        flow.PerformLayout();
    }

    private static FlowLayoutPanel CreateCaptureFlow()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.FromArgb(18, 18, 22),
            Padding = new Padding(10),
        };
    }

    private static ListView CreateChatListView()
    {
        var lv = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
        };
        lv.Columns.Add("時刻", 90);
        lv.Columns.Add("名前", 180);
        lv.Columns.Add("コメント", 760);
        return lv;
    }

    private static ListView CreateAllChatListView()
    {
        var lv = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
        };
        lv.Columns.Add("時刻", 90);
        lv.Columns.Add("種別", 90);
        lv.Columns.Add("名前", 180);
        lv.Columns.Add("コメント", 760);
        return lv;
    }

    private static string NormalizeTwitchChannel(string input)
    {
        input = input.Trim();
        input = Regex.Replace(input, @"^https?://(www\.)?twitch\.tv/", "", RegexOptions.IgnoreCase);
        input = input.Trim('/').Trim();
        input = input.TrimStart('#');
        return Regex.Replace(input, @"[^a-zA-Z0-9_]", "").ToLowerInvariant();
    }
}

public sealed class AppSettings
{
    public string VoicevoxUrl { get; set; } = "http://127.0.0.1:50021";
    public int Speaker { get; set; } = 3;
    public bool VoiceEnabled { get; set; } = true;
    public bool DedupeEnabled { get; set; } = true;
    public bool ReadName { get; set; } = false;
    public bool CaptureShowName { get; set; } = true;
    public bool ShowIcon { get; set; } = true;
    public int IconSize { get; set; } = 40;
    public int CaptureFontSize { get; set; } = 18;
    public string CaptureFontFamily { get; set; } = "Meiryo UI";
    public bool SaveLog { get; set; } = true;
    public bool FastMode { get; set; } = true;
    public bool OverlayEnabled { get; set; } = false;
    public int OverlayPort { get; set; } = 51766;
    public bool OverlayAutoPort { get; set; } = true;

    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IzCommentViewer");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
        });
        File.WriteAllText(SettingsPath, json, Encoding.UTF8);
    }
}

public sealed class OverlayAppearance
{
    public string FontFamily { get; set; } = "Meiryo UI";
    public int FontSize { get; set; } = 24;
    public int IconSize { get; set; } = 46;
    public bool ShowIcon { get; set; } = true;
    public bool ShowName { get; set; } = true;
}

public sealed class ChatMessage
{
    public string Platform { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Text { get; set; } = "";
    public string MessageHtml { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.Now;
}

public sealed class ChatAvatarCache : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly ConcurrentDictionary<string, Image> _cache = new();
    private readonly ConcurrentDictionary<string, byte> _downloadStarted = new();

    public Image GetAvatar(ChatMessage msg, int size)
    {
        var placeholderKey = "placeholder:" + msg.Platform + ":" + msg.UserName + ":" + size;

        if (string.IsNullOrWhiteSpace(msg.AvatarUrl))
        {
            return _cache.GetOrAdd(placeholderKey, _ => CreatePlaceholder(msg, size));
        }

        var imageKey = msg.AvatarUrl + ":" + size;
        if (_cache.TryGetValue(imageKey, out var cached)) return cached;

        // UIスレッドを止めないため、初回は仮アイコンを返して裏で実画像を取得する。
        if (_downloadStarted.TryAdd(imageKey, 0))
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var img = LoadAvatar(msg, size);
                    _cache[imageKey] = img;
                }
                catch
                {
                }
            });
        }

        return _cache.GetOrAdd(placeholderKey, _ => CreatePlaceholder(msg, size));
    }

    private Image LoadAvatar(ChatMessage msg, int size)
    {
        if (!string.IsNullOrWhiteSpace(msg.AvatarUrl))
        {
            try
            {
                var bytes = _http.GetByteArrayAsync(msg.AvatarUrl).GetAwaiter().GetResult();
                using var ms = new MemoryStream(bytes);
                using var original = Image.FromStream(ms);
                return MakeCircleBitmap(original, size);
            }
            catch
            {
            }
        }
        return CreatePlaceholder(msg, size);
    }

    private static Image MakeCircleBitmap(Image src, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath();
        path.AddEllipse(0, 0, size - 1, size - 1);
        g.SetClip(path);
        g.DrawImage(src, 0, 0, size, size);
        return bmp;
    }

    private static Image CreatePlaceholder(ChatMessage msg, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bg = msg.Platform == "YouTube" ? Color.FromArgb(180, 40, 40) : Color.FromArgb(95, 60, 170);
        using var brush = new SolidBrush(bg);
        g.FillEllipse(brush, 0, 0, size - 1, size - 1);

        var initial = string.IsNullOrWhiteSpace(msg.UserName) ? "?" : msg.UserName.Trim()[0].ToString().ToUpperInvariant();
        using var font = new Font("Meiryo UI", Math.Max(8, size * 0.42f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        var textSize = g.MeasureString(initial, font);
        g.DrawString(initial, font, textBrush, (size - textSize.Width) / 2, (size - textSize.Height) / 2);
        return bmp;
    }

    public void Dispose()
    {
        foreach (var image in _cache.Values) image.Dispose();
        _http.Dispose();
    }
}

public sealed class ChatLogEntry
{
    public string Platform { get; set; } = "";
    public string UserName { get; set; } = "";
    public string NormalizedUserKey { get; set; } = "";
    public string Text { get; set; } = "";
    public string MessageHtml { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.Now;
}

public sealed class UserLogSummary
{
    public string Platform { get; set; } = "";
    public string UserName { get; set; } = "";
    public string NormalizedUserKey { get; set; } = "";
    public int Count { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public string LastText { get; set; } = "";
}

public sealed class ChatLogStore
{
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public string BaseDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IzCommentViewer",
        "Logs");

    public void Save(ChatMessage message)
    {
        SaveBatch(new[] { message });
    }

    public void SaveBatch(IEnumerable<ChatMessage> messages)
    {
        var entries = messages.Select(message => new ChatLogEntry
        {
            Platform = message.Platform,
            UserName = message.UserName,
            NormalizedUserKey = NormalizeUserKey(message.UserName),
            Text = message.Text,
            MessageHtml = message.MessageHtml,
            AvatarUrl = message.AvatarUrl,
            Time = message.Time,
        }).ToList();

        if (entries.Count == 0) return;

        Directory.CreateDirectory(BaseDirectory);

        lock (_lock)
        {
            var dailyGroups = entries.GroupBy(x => x.Time.ToString("yyyy-MM-dd"));
            foreach (var g in dailyGroups)
            {
                var lines = string.Join(Environment.NewLine, g.Select(x => JsonSerializer.Serialize(x, _jsonOptions))) + Environment.NewLine;
                File.AppendAllText(Path.Combine(BaseDirectory, "all_" + g.Key + ".jsonl"), lines, Encoding.UTF8);
            }

            foreach (var platformGroup in entries.GroupBy(x => x.Platform))
            {
                Directory.CreateDirectory(GetPlatformDirectory(platformGroup.Key));

                foreach (var dateGroup in platformGroup.GroupBy(x => x.Time.ToString("yyyy-MM-dd")))
                {
                    var lines = string.Join(Environment.NewLine, dateGroup.Select(x => JsonSerializer.Serialize(x, _jsonOptions))) + Environment.NewLine;
                    File.AppendAllText(Path.Combine(GetPlatformDirectory(platformGroup.Key), platformGroup.Key + "_" + dateGroup.Key + ".jsonl"), lines, Encoding.UTF8);
                }

                foreach (var userGroup in platformGroup.GroupBy(x => x.NormalizedUserKey))
                {
                    var first = userGroup.First();
                    Directory.CreateDirectory(GetUserDirectory(first.Platform, first.UserName));
                    var lines = string.Join(Environment.NewLine, userGroup.Select(x => JsonSerializer.Serialize(x, _jsonOptions))) + Environment.NewLine;
                    File.AppendAllText(GetUserLogPath(first.Platform, first.UserName), lines, Encoding.UTF8);
                }
            }

            UpdateUserSummaries(entries);
        }
    }

    public List<UserLogSummary> LoadSummaries()
    {
        var path = GetSummaryPath();
        if (!File.Exists(path)) return new List<UserLogSummary>();
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<UserLogSummary>>(json) ?? new List<UserLogSummary>();
        }
        catch
        {
            return new List<UserLogSummary>();
        }
    }

    public List<ChatLogEntry> LoadUserEntries(string platform, string userName, int max = 300)
    {
        var path = GetUserLogPath(platform, userName);
        if (!File.Exists(path)) return new List<ChatLogEntry>();

        try
        {
            var lines = File.ReadLines(path, Encoding.UTF8)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Reverse()
                .Take(max)
                .Reverse();

            var result = new List<ChatLogEntry>();
            foreach (var line in lines)
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<ChatLogEntry>(line);
                    if (entry != null) result.Add(entry);
                }
                catch { }
            }
            return result;
        }
        catch
        {
            return new List<ChatLogEntry>();
        }
    }

    public string GetUserLogPath(string platform, string userName)
    {
        return Path.Combine(GetUserDirectory(platform, userName), "comments.jsonl");
    }

    private void UpdateUserSummary(ChatLogEntry entry)
    {
        UpdateUserSummaries(new[] { entry });
    }

    private void UpdateUserSummaries(IEnumerable<ChatLogEntry> entries)
    {
        var summaries = LoadSummaries();
        var map = summaries.ToDictionary(x => x.Platform + ":" + x.NormalizedUserKey, x => x);

        foreach (var entry in entries)
        {
            var key = entry.Platform + ":" + entry.NormalizedUserKey;
            if (!map.TryGetValue(key, out var summary))
            {
                summary = new UserLogSummary
                {
                    Platform = entry.Platform,
                    UserName = entry.UserName,
                    NormalizedUserKey = entry.NormalizedUserKey,
                    Count = 0,
                    FirstSeen = entry.Time,
                    LastSeen = entry.Time,
                };
                map[key] = summary;
                summaries.Add(summary);
            }

            summary.UserName = entry.UserName;
            summary.Count++;
            if (summary.FirstSeen == default || entry.Time < summary.FirstSeen) summary.FirstSeen = entry.Time;
            if (entry.Time > summary.LastSeen) summary.LastSeen = entry.Time;
            summary.LastText = entry.Text;
        }

        summaries = summaries
            .OrderByDescending(x => x.LastSeen)
            .ThenBy(x => x.Platform)
            .ThenBy(x => x.UserName)
            .ToList();

        var json = JsonSerializer.Serialize(summaries, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
        });
        File.WriteAllText(GetSummaryPath(), json, Encoding.UTF8);
    }

    private string GetSummaryPath()
    {
        Directory.CreateDirectory(BaseDirectory);
        return Path.Combine(BaseDirectory, "users.summary.json");
    }

    private string GetDailyLogPath(DateTime time)
    {
        return Path.Combine(BaseDirectory, "all_" + time.ToString("yyyy-MM-dd") + ".jsonl");
    }

    private string GetPlatformDailyLogPath(string platform, DateTime time)
    {
        return Path.Combine(GetPlatformDirectory(platform), platform + "_" + time.ToString("yyyy-MM-dd") + ".jsonl");
    }

    private string GetPlatformDirectory(string platform)
    {
        return Path.Combine(BaseDirectory, SafeFileName(platform));
    }

    private string GetUserDirectory(string platform, string userName)
    {
        return Path.Combine(GetPlatformDirectory(platform), "Users", SafeFileName(NormalizeUserKey(userName)));
    }

    public static string NormalizeUserKey(string userName)
    {
        var key = userName.Trim().ToLowerInvariant();
        key = Regex.Replace(key, @"\s+", "_");
        return string.IsNullOrWhiteSpace(key) ? "unknown" : key;
    }

    private static string SafeFileName(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "unknown" : value;
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        value = Regex.Replace(value, @"[^\p{L}\p{N}_\-\.]+", "_");
        return value.Length > 80 ? value[..80] : value;
    }
}

public sealed class UserLogForm : Form
{
    private readonly ChatLogStore _store;
    private readonly ListView _userList = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        HideSelection = false,
    };
    private readonly TextBox _logText = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Meiryo UI", 10F),
    };
    private readonly TextBox _searchBox = new() { Dock = DockStyle.Fill, PlaceholderText = "ユーザー名で検索" };
    private readonly Button _refreshButton = new() { Text = "更新", Width = 80 };
    private List<UserLogSummary> _summaries = new();

    public UserLogForm(ChatLogStore store)
    {
        _store = store;
        Text = "ユーザー別コメントログ";
        Width = 1000;
        Height = 650;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Meiryo UI", 9F);

        _userList.Columns.Add("Platform", 90);
        _userList.Columns.Add("User", 180);
        _userList.Columns.Add("Count", 70);
        _userList.Columns.Add("LastSeen", 150);
        _userList.Columns.Add("LastComment", 360);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(_searchBox, 0, 0);
        top.Controls.Add(_refreshButton, 1, 0);
        root.Controls.Add(top, 0, 0);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 520,
        };
        split.Panel1.Controls.Add(_userList);
        split.Panel2.Controls.Add(_logText);
        root.Controls.Add(split, 0, 1);

        _refreshButton.Click += (_, _) => RefreshUsers();
        _searchBox.TextChanged += (_, _) => RenderUsers();
        _userList.SelectedIndexChanged += (_, _) => LoadSelectedUserLog();

        RefreshUsers();
    }

    public void NotifyNewMessage(ChatMessage message)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => NotifyNewMessage(message));
            return;
        }
        RefreshUsers(keepSelection: true);
    }

    public void RefreshUsers(bool keepSelection = false)
    {
        string? selectedKey = null;
        if (keepSelection && _userList.SelectedItems.Count > 0)
        {
            selectedKey = _userList.SelectedItems[0].Tag as string;
        }

        _summaries = _store.LoadSummaries();
        RenderUsers(selectedKey);
    }

    private void RenderUsers(string? selectedKey = null)
    {
        var q = _searchBox.Text.Trim();
        _userList.BeginUpdate();
        _userList.Items.Clear();

        foreach (var s in _summaries)
        {
            if (!string.IsNullOrWhiteSpace(q) &&
                !s.UserName.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !s.Platform.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = s.Platform + ":" + s.NormalizedUserKey;
            var item = new ListViewItem(s.Platform);
            item.SubItems.Add(s.UserName);
            item.SubItems.Add(s.Count.ToString());
            item.SubItems.Add(s.LastSeen.ToString("yyyy-MM-dd HH:mm"));
            item.SubItems.Add(s.LastText);
            item.Tag = key;
            _userList.Items.Add(item);

            if (selectedKey == key)
            {
                item.Selected = true;
                item.Focused = true;
            }
        }

        _userList.EndUpdate();
    }

    private void LoadSelectedUserLog()
    {
        if (_userList.SelectedItems.Count == 0)
        {
            _logText.Clear();
            return;
        }

        var item = _userList.SelectedItems[0];
        var platform = item.SubItems[0].Text;
        var userName = item.SubItems[1].Text;
        var entries = _store.LoadUserEntries(platform, userName, 300);

        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            sb.AppendLine("[" + e.Time.ToString("yyyy-MM-dd HH:mm:ss") + "] " + e.Text);
        }
        _logText.Text = sb.ToString();
    }
}

public sealed class CaptureForm : Form
{
    private readonly ChatAvatarCache _avatarCache;
    private readonly FlowLayoutPanel _youtubeFlow = CreateFlow();
    private readonly FlowLayoutPanel _twitchFlow = CreateFlow();

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string CaptureFontFamilyName { get; set; } = "Meiryo UI";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float CaptureFontSize { get; set; } = 18F;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool CaptureShowName { get; set; } = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool CaptureShowIcon { get; set; } = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CaptureIconSize { get; set; } = 40;

    public CaptureForm(ChatAvatarCache avatarCache)
    {
        _avatarCache = avatarCache;
        Text = "OBS Capture - YouTube / Twitch Comments";
        Width = 1000;
        Height = 600;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(12, 12, 16);
        Font = new Font("Meiryo UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.FromArgb(12, 12, 16),
            Padding = new Padding(10),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        Controls.Add(root);

        root.Controls.Add(BuildColumn("YouTube", _youtubeFlow, Color.FromArgb(255, 70, 70)), 0, 0);
        root.Controls.Add(BuildColumn("Twitch", _twitchFlow, Color.FromArgb(160, 100, 255)), 1, 0);
    }

    public void AddMessage(ChatMessage msg, bool showName, float fontSize, string fontFamilyName, bool showIcon, int iconSize)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => AddMessage(msg, showName, fontSize, fontFamilyName, showIcon, iconSize));
            return;
        }

        CaptureShowName = showName;
        CaptureFontSize = fontSize;
        CaptureFontFamilyName = string.IsNullOrWhiteSpace(fontFamilyName) ? "Meiryo UI" : fontFamilyName;
        CaptureShowIcon = showIcon;
        CaptureIconSize = iconSize;

        var flow = msg.Platform == "YouTube" ? _youtubeFlow : _twitchFlow;
        var bubble = MainForm.CreateCaptureBubble(msg, flow.ClientSize.Width, CaptureFontFamilyName, CaptureFontSize, CaptureShowName, CaptureShowIcon, CaptureIconSize, _avatarCache);
        flow.Controls.Add(bubble);
        while (flow.Controls.Count > 30)
        {
            var first = flow.Controls[0];
            flow.Controls.RemoveAt(0);
            first.Dispose();
        }
        ScrollFlowToBottom(flow);
    }

    public void ApplyFontToExistingBubbles()
    {
        if (IsDisposed) return;
        ApplyFontToExistingBubbles(_youtubeFlow);
        ApplyFontToExistingBubbles(_twitchFlow);
    }

    private void ApplyFontToExistingBubbles(FlowLayoutPanel flow)
    {
        foreach (Control c in flow.Controls)
        {
            foreach (var label in c.Controls.OfType<Label>().Where(x => x.Name == "CommentText"))
            {
                label.Font = new Font(CaptureFontFamilyName, CaptureFontSize, FontStyle.Bold);
            }
            RecalculateBubbleLayout(c);
        }
        ScrollFlowToBottom(flow);
    }

    public void ApplyIconSettingsToExistingBubbles()
    {
        if (IsDisposed) return;
        ApplyIconSettingsToExistingBubbles(_youtubeFlow);
        ApplyIconSettingsToExistingBubbles(_twitchFlow);
    }

    private void ApplyIconSettingsToExistingBubbles(FlowLayoutPanel flow)
    {
        foreach (Control c in flow.Controls)
        {
            RecalculateBubbleLayout(c);
        }
        ScrollFlowToBottom(flow);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ResizeBubbles(_youtubeFlow);
        ResizeBubbles(_twitchFlow);
    }

    private void ResizeBubbles(FlowLayoutPanel flow)
    {
        var width = Math.Max(240, flow.ClientSize.Width - 30);
        foreach (Control c in flow.Controls)
        {
            c.Width = width;
            RecalculateBubbleLayout(c);
        }
        ScrollFlowToBottom(flow);
    }

    private void RecalculateBubbleLayout(Control control)
    {
        if (control is not Panel panel) return;

        var padding = 10;
        var gap = CaptureShowIcon ? 10 : 0;
        var visibleIconSize = CaptureShowIcon ? CaptureIconSize : 0;
        panel.Width = Math.Max(240, panel.Parent?.ClientSize.Width - 30 ?? panel.Width);

        var icon = panel.Controls.OfType<PictureBox>().FirstOrDefault(x => x.Name == "AvatarIcon");
        var label = panel.Controls.OfType<Label>().FirstOrDefault(x => x.Name == "CommentText");
        if (label == null) return;

        if (icon != null)
        {
            icon.Visible = CaptureShowIcon;
            icon.Width = CaptureIconSize;
            icon.Height = CaptureIconSize;
            icon.Left = padding;
            icon.Top = padding;
        }

        var textX = padding + visibleIconSize + gap;
        var textWidth = Math.Max(80, panel.Width - textX - padding);
        label.Left = textX;
        label.Top = padding;
        label.Width = textWidth;
        label.Font = new Font(CaptureFontFamilyName, CaptureFontSize, FontStyle.Bold);

        var measured = TextRenderer.MeasureText(
            label.Text,
            label.Font,
            new Size(textWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl
        );
        label.Height = measured.Height + 4;

        var contentHeight = Math.Max(CaptureShowIcon ? CaptureIconSize : 0, label.Height);
        panel.Height = Math.Max(44, contentHeight + padding * 2);
    }

    private static FlowLayoutPanel CreateFlow()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.FromArgb(18, 18, 22),
            Padding = new Padding(10),
        };
    }

    private static void ScrollFlowToBottom(FlowLayoutPanel flow)
    {
        if (flow.Controls.Count <= 0) return;
        var last = flow.Controls[flow.Controls.Count - 1];
        flow.ScrollControlIntoView(last);
        flow.VerticalScroll.Value = flow.VerticalScroll.Maximum;
        flow.PerformLayout();
    }

    private static Control BuildColumn(string title, FlowLayoutPanel flow, Color accent)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.FromArgb(18, 18, 22),
            Margin = new Padding(8),
            Padding = new Padding(0),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Height = 46,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Meiryo UI", 18F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = accent,
        };

        panel.Controls.Add(header, 0, 0);
        panel.Controls.Add(flow, 0, 1);
        return panel;
    }
}

public sealed class OverlayServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Func<OverlayAppearance> _getAppearance;
    private readonly HttpClient _http = new();
    private readonly ConcurrentDictionary<string, string> _twitchAvatarCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<StreamWriter> _clients = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _internalCts;

    public int Port { get; }
    public string YoutubeOverlayUrl => $"http://127.0.0.1:{Port}/overlay/youtube";
    public string TwitchOverlayUrl => $"http://127.0.0.1:{Port}/overlay/twitch";

    public OverlayServer(int port, Func<OverlayAppearance> getAppearance)
    {
        Port = port;
        _getAppearance = getAppearance;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public void Start(CancellationToken externalToken)
    {
        _internalCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        _listener.Start();
        _ = Task.Run(() => AcceptLoopAsync(_internalCts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch
            {
                if (token.IsCancellationRequested) break;
            }

            if (ctx == null) continue;
            _ = Task.Run(() => HandleRequestAsync(ctx, token), token);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken token)
    {
        var path = ctx.Request.Url?.AbsolutePath.Trim('/').ToLowerInvariant() ?? "overlay";

        if (path == "events")
        {
            await HandleEventsAsync(ctx, token);
            return;
        }

        if (path.StartsWith("avatar/twitch/", StringComparison.OrdinalIgnoreCase))
        {
            await HandleTwitchAvatarAsync(ctx, path["avatar/twitch/".Length..]);
            return;
        }

        var mode = path switch
        {
            "overlay/youtube" => "youtube",
            "overlay/twitch" => "twitch",
            "overlay/columns" => "columns",
            _ => "all",
        };

        await HandleOverlayAsync(ctx, mode);
    }

    public void Broadcast(ChatMessage message)
    {
        var json = JsonSerializer.Serialize(new
        {
            platform = message.Platform,
            userName = message.UserName,
            text = message.Text,
            avatarUrl = ResolveOverlayAvatarUrl(message),
            html = string.IsNullOrWhiteSpace(message.MessageHtml) ? WebUtility.HtmlEncode(message.Text) : message.MessageHtml,
            time = message.Time.ToString("O"),
        }, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        lock (_lock)
        {
            foreach (var client in _clients.ToArray())
            {
                try
                {
                    client.WriteLine("data: " + json);
                    client.WriteLine();
                    client.Flush();
                }
                catch
                {
                    _clients.Remove(client);
                    try { client.Dispose(); } catch { }
                }
            }
        }
    }

    private string ResolveOverlayAvatarUrl(ChatMessage message)
    {
        if (string.Equals(message.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
        {
            var user = Regex.Replace((message.UserName ?? "").Trim(), @"[^a-zA-Z0-9_]", "");
            if (string.IsNullOrWhiteSpace(user)) return "";
            return "/avatar/twitch/" + Uri.EscapeDataString(user);
        }

        return string.IsNullOrWhiteSpace(message.AvatarUrl) ? "" : message.AvatarUrl;
    }

    private async Task HandleTwitchAvatarAsync(HttpListenerContext ctx, string rawUser)
    {
        var user = Regex.Replace(Uri.UnescapeDataString(rawUser ?? "").Trim(), @"[^a-zA-Z0-9_]", "");
        if (string.IsNullOrWhiteSpace(user))
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        try
        {
            var imageUrl = await ResolveTwitchAvatarImageUrlAsync(user);
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            var bytes = await _http.GetByteArrayAsync(imageUrl);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = GuessImageContentType(imageUrl);
            ctx.Response.Headers.Add("Cache-Control", "public, max-age=3600");
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }
        catch
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
        }
    }

    private async Task<string> ResolveTwitchAvatarImageUrlAsync(string user)
    {
        if (_twitchAvatarCache.TryGetValue(user, out var cached)) return cached;

        var decapiUrl = "https://decapi.me/twitch/avatar/" + Uri.EscapeDataString(user);
        var resolved = "";

        try
        {
            resolved = (await _http.GetStringAsync(decapiUrl)).Trim();
        }
        catch
        {
        }

        if (!resolved.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !resolved.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            resolved = "https://unavatar.io/twitch/" + Uri.EscapeDataString(user);
        }

        _twitchAvatarCache[user] = resolved;
        return resolved;
    }

    private static string GuessImageContentType(string url)
    {
        var lower = url.ToLowerInvariant();
        if (lower.Contains(".jpg") || lower.Contains(".jpeg")) return "image/jpeg";
        if (lower.Contains(".webp")) return "image/webp";
        if (lower.Contains(".gif")) return "image/gif";
        return "image/png";
    }

    private async Task HandleEventsAsync(HttpListenerContext ctx, CancellationToken token)
    {
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "text/event-stream; charset=utf-8";
        ctx.Response.Headers.Add("Cache-Control", "no-cache");
        ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        ctx.Response.SendChunked = true;

        var writer = new StreamWriter(ctx.Response.OutputStream, new UTF8Encoding(false)) { AutoFlush = true };
        lock (_lock) _clients.Add(writer);

        try
        {
            while (!token.IsCancellationRequested)
            {
                await writer.WriteLineAsync(": keepalive");
                await writer.WriteLineAsync();
                await writer.FlushAsync();
                await Task.Delay(15000, token);
            }
        }
        catch
        {
        }
        finally
        {
            lock (_lock) _clients.Remove(writer);
            try { writer.Dispose(); } catch { }
        }
    }

    private async Task HandleOverlayAsync(HttpListenerContext ctx, string mode)
    {
        var html = OverlayHtmlProvider.GetHtml(mode, _getAppearance());
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        try { _internalCts?.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        lock (_lock)
        {
            foreach (var client in _clients.ToArray())
            {
                try { client.Dispose(); } catch { }
            }
            _clients.Clear();
        }
        try { _internalCts?.Dispose(); } catch { }
        try { _http.Dispose(); } catch { }
    }
}

public static class OverlayHtmlProvider
{
    public static string GetHtml(string mode, OverlayAppearance appearance)
    {
        return """
<!doctype html>
<html lang="ja">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>IzCommentViewer Overlay</title>
<style>
:root {
  --font-size: __FONT_SIZE__px;
  --name-size: __NAME_SIZE__px;
  --avatar-size: __AVATAR_SIZE__px;
  --max-width: 900px;
}
* { box-sizing: border-box; }
html, body {
  margin: 0;
  width: 100%;
  height: 100%;
  overflow: hidden;
  background: transparent;
  font-family: "__FONT_FAMILY__", "Meiryo UI", "Yu Gothic", sans-serif;
}
#chat {
  position: fixed;
  left: 20px;
  right: 20px;
  bottom: 20px;
  display: flex;
  flex-direction: column;
  justify-content: flex-end;
  align-items: flex-start;
  gap: 10px;
  height: calc(100vh - 40px);
  overflow: hidden;
  pointer-events: none;
}
#columns {
  position: fixed;
  inset: 20px;
  display: none;
  grid-template-columns: 1fr 1fr;
  gap: 20px;
  pointer-events: none;
}
.column {
  min-height: 0;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  justify-content: flex-end;
  gap: 10px;
}
.column-title {
  color: #fff;
  font-weight: 900;
  text-align: center;
  font-size: 24px;
  padding: 10px 16px;
  border-radius: 16px;
  background: rgba(20,20,26,.7);
  text-shadow: 0 3px 8px rgba(0,0,0,.8);
}
.msg {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  width: fit-content;
  max-width: var(--max-width);
  padding: 12px 16px;
  border-radius: 18px;
  color: white;
  background: rgba(20, 20, 26, 0.84);
  box-shadow: 0 10px 28px rgba(0,0,0,0.36);
  animation: popIn 230ms cubic-bezier(.2,1.3,.25,1);
  will-change: transform, opacity;
}
.msg.youtube { border-left: 6px solid #ff4040; }
.msg.twitch { border-left: 6px solid #a060ff; }
.avatar, .avatar-fallback {
  width: var(--avatar-size);
  height: var(--avatar-size);
  border-radius: 999px;
  object-fit: cover;
  flex: 0 0 auto;
  background: #555;
  display: grid;
  place-items: center;
  color: white;
  font-weight: 900;
  font-size: calc(var(--avatar-size) * .42);
  box-shadow: 0 3px 10px rgba(0,0,0,.35);
}
.avatar-fallback.youtube { background: #b02b2b; }
.avatar-fallback.twitch { background: #6a3bd1; }
.body { min-width: 0; }
.name {
  font-weight: 900;
  font-size: var(--name-size);
  line-height: 1.15;
  margin-bottom: 3px;
  text-shadow: 0 2px 6px rgba(0,0,0,.8);
}
.text {
  font-weight: 800;
  font-size: var(--font-size);
  line-height: 1.35;
  word-break: break-word;
  overflow-wrap: anywhere;
  text-shadow: 0 2px 7px rgba(0,0,0,.9);
}
.platform {
  display: inline-block;
  margin-right: 8px;
  font-size: .78em;
  opacity: .85;
}
.emote {
  height: 1.45em;
  width: auto;
  vertical-align: -0.32em;
  display: inline-block;
  margin: 0 2px;
}
@keyframes popIn {
  from { opacity: 0; transform: translateY(18px) scale(.96); }
  to { opacity: 1; transform: translateY(0) scale(1); }
}
</style>
</head>
<body>
<div id="chat"></div>
<div id="columns">
  <div class="column" id="ytCol"><div class="column-title">YouTube</div></div>
  <div class="column" id="twCol"><div class="column-title">Twitch</div></div>
</div>
<script>
const MODE = "__MODE__";
const SHOW_ICON = __SHOW_ICON__;
const SHOW_NAME = __SHOW_NAME__;
const maxMessages = 80;
const chat = document.getElementById('chat');
const columns = document.getElementById('columns');
const ytCol = document.getElementById('ytCol');
const twCol = document.getElementById('twCol');

if (MODE === 'columns') {
  chat.style.display = 'none';
  columns.style.display = 'grid';
}

const es = new EventSource('/events');
es.onmessage = (ev) => {
  try {
    const msg = JSON.parse(ev.data);
    if (!accept(msg)) return;
    addMessage(msg);
  } catch (e) {}
};

function accept(msg) {
  const p = (msg.platform || '').toLowerCase();
  if (MODE === 'youtube') return p === 'youtube';
  if (MODE === 'twitch') return p === 'twitch';
  return true;
}

function addMessage(msg) {
  const p = (msg.platform || '').toLowerCase();
  const el = document.createElement('div');
  el.className = 'msg ' + p;

  const avatar = SHOW_ICON ? createAvatar(msg, p) : null;
  const body = document.createElement('div');
  body.className = 'body';

  const name = document.createElement('div');
  name.className = 'name';
  const badge = document.createElement('span');
  badge.className = 'platform';
  badge.textContent = p === 'youtube' ? '[YT]' : '[TW]';
  name.appendChild(badge);
  name.appendChild(document.createTextNode(SHOW_NAME ? (msg.userName || 'unknown') : ''));
  if (!SHOW_NAME) name.style.display = 'none';

  const text = document.createElement('div');
  text.className = 'text';
  if (msg.html) {
    text.innerHTML = sanitizeMessageHtml(msg.html);
  } else {
    text.textContent = msg.text || '';
  }

  body.appendChild(name);
  body.appendChild(text);
  if (avatar) el.appendChild(avatar);
  el.appendChild(body);

  const parent = MODE === 'columns'
    ? (p === 'youtube' ? ytCol : twCol)
    : chat;

  parent.appendChild(el);
  trim(parent, MODE === 'columns' ? 60 : maxMessages);

  // 新しいコメントが常に見えるように下端へ寄せる
  parent.scrollTop = parent.scrollHeight;

}

function createAvatar(msg, platform) {
  if (msg.avatarUrl) {
    const img = document.createElement('img');
    img.className = 'avatar';
    img.src = msg.avatarUrl;
    img.onerror = () => {
      const fallback = createFallback(msg, platform);
      img.replaceWith(fallback);
    };
    return img;
  }
  return createFallback(msg, platform);
}

function createFallback(msg, platform) {
  const div = document.createElement('div');
  div.className = 'avatar-fallback ' + platform;
  div.textContent = ((msg.userName || '?').trim().slice(0,1) || '?').toUpperCase();
  return div;
}

function sanitizeMessageHtml(html) {
  const template = document.createElement('template');
  template.innerHTML = html;
  const allowedTags = new Set(['IMG', 'SPAN', 'B', 'I', 'STRONG', 'EM', 'BR']);
  const walk = document.createTreeWalker(template.content, NodeFilter.SHOW_ELEMENT);
  const nodes = [];
  while (walk.nextNode()) nodes.push(walk.currentNode);

  for (const el of nodes) {
    if (!allowedTags.has(el.tagName)) {
      el.replaceWith(document.createTextNode(el.textContent || ''));
      continue;
    }

    for (const attr of Array.from(el.attributes)) {
      const name = attr.name.toLowerCase();
      if (el.tagName === 'IMG' && (name === 'src' || name === 'alt' || name === 'title')) continue;
      if (name === 'class') continue;
      el.removeAttribute(attr.name);
    }

    if (el.tagName === 'IMG') {
      const src = el.getAttribute('src') || '';
      if (!/^https:\/\//i.test(src)) {
        el.replaceWith(document.createTextNode(el.getAttribute('alt') || ''));
        continue;
      }
      el.classList.add('emote');
      el.loading = 'eager';
      el.decoding = 'async';
    }
  }
  return template.innerHTML;
}

function trim(parent, max) {
  const messages = Array.from(parent.querySelectorAll('.msg'));
  while (messages.length > max) {
    const first = messages.shift();
    if (first) first.remove();
  }
}
</script>
</body>
</html>
"""
        .Replace("__MODE__", JavaScriptEncoder.Default.Encode(mode).Trim('"'))
        .Replace("__FONT_FAMILY__", CssEscape(appearance.FontFamily))
        .Replace("__FONT_SIZE__", Math.Max(10, Math.Min(96, appearance.FontSize)).ToString())
        .Replace("__NAME_SIZE__", Math.Max(8, Math.Min(72, (int)Math.Round(appearance.FontSize * 0.75))).ToString())
        .Replace("__AVATAR_SIZE__", Math.Max(16, Math.Min(128, appearance.IconSize)).ToString())
        .Replace("__SHOW_ICON__", appearance.ShowIcon ? "true" : "false")
        .Replace("__SHOW_NAME__", appearance.ShowName ? "true" : "false");
    }

    private static string CssEscape(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Meiryo UI";
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

public sealed class VoicevoxClient : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly SemaphoreSlim _speakLock = new(1, 1);
    private readonly ConcurrentQueue<string> _queue = new();
    private int _processing;

    public string BaseUrl { get; set; } = "http://127.0.0.1:50021";
    public int Speaker { get; set; } = 3;
    public int PendingCount => _queue.Count;
    public int MaxPendingCount { get; set; } = 20;

    public Task EnqueueAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        if (_queue.Count >= MaxPendingCount) return Task.CompletedTask;
        _queue.Enqueue(text);
        _ = ProcessQueueAsync();
        return Task.CompletedTask;
    }

    private async Task ProcessQueueAsync()
    {
        if (Interlocked.Exchange(ref _processing, 1) == 1) return;

        try
        {
            while (_queue.TryDequeue(out var text)) await SpeakOnceAsync(text);
        }
        finally
        {
            Interlocked.Exchange(ref _processing, 0);
            if (!_queue.IsEmpty) _ = ProcessQueueAsync();
        }
    }

    private async Task SpeakOnceAsync(string text)
    {
        await _speakLock.WaitAsync();
        try
        {
            var queryUrl = $"{BaseUrl}/audio_query?text={Uri.EscapeDataString(text)}&speaker={Speaker}";
            using var queryRes = await _http.PostAsync(queryUrl, content: null);
            queryRes.EnsureSuccessStatusCode();
            var queryJson = await queryRes.Content.ReadAsStringAsync();

            var synthUrl = $"{BaseUrl}/synthesis?speaker={Speaker}";
            using var content = new StringContent(queryJson, Encoding.UTF8, "application/json");
            using var synthRes = await _http.PostAsync(synthUrl, content);
            synthRes.EnsureSuccessStatusCode();
            var wav = await synthRes.Content.ReadAsByteArrayAsync();

            var path = Path.Combine(Path.GetTempPath(), "voicevox_chat_" + Guid.NewGuid().ToString("N") + ".wav");
            await File.WriteAllBytesAsync(path, wav);

            try
            {
                using var player = new SoundPlayer(path);
                player.PlaySync();
            }
            finally
            {
                TryDelete(path);
            }
        }
        finally
        {
            _speakLock.Release();
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public void Dispose()
    {
        _http.Dispose();
        _speakLock.Dispose();
    }
}

public static class TwitchAvatarResolver
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string GetAvatarUrl(string userName)
    {
        // Twitch IRCにはアイコンURLが含まれない。
        // OBSブラウザソース側では OverlayServer の /avatar/twitch/{user} で解決するため、ここでは空にしておく。
        return "";
    }
}

public sealed class TwitchIrcChatSource
{
    private readonly string _channel;
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _internalCts;

    public event Action<ChatMessage>? MessageReceived;
    public event Action<string>? StatusChanged;

    public TwitchIrcChatSource(string channel)
    {
        _channel = channel;
    }

    public async Task StartAsync(CancellationToken token)
    {
        _internalCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var ct = _internalCts.Token;

        _client = new TcpClient();
        await _client.ConnectAsync("irc.chat.twitch.tv", 6667, ct);
        var stream = _client.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };

        var nick = "justinfan" + Random.Shared.Next(10000, 99999);
        await _writer.WriteLineAsync("CAP REQ :twitch.tv/tags twitch.tv/commands");
        await _writer.WriteLineAsync("PASS SCHMOOPIIE");
        await _writer.WriteLineAsync("NICK " + nick);
        await _writer.WriteLineAsync("JOIN #" + _channel);

        _ = Task.Run(() => ReadLoopAsync(ct), ct);
    }

    public void Stop()
    {
        try { _internalCts?.Cancel(); } catch { }
        try { _writer?.WriteLine("PART #" + _channel); } catch { }
        try { _client?.Close(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _internalCts?.Dispose(); } catch { }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _reader != null)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null) break;

                if (line.StartsWith("PING", StringComparison.OrdinalIgnoreCase))
                {
                    await _writer!.WriteLineAsync(line.Replace("PING", "PONG"));
                    continue;
                }

                var msg = ParsePrivMsg(line);
                if (msg != null) MessageReceived?.Invoke(msg);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("Twitch受信エラー: " + ex.Message);
        }
    }

    private static ChatMessage? ParsePrivMsg(string line)
    {
        if (!line.Contains(" PRIVMSG ")) return null;

        string user = "unknown";
        string text = "";
        string emotesTag = "";

        var emotesMatch = Regex.Match(line, @"(?:^|;)emotes=([^;]*)");
        if (emotesMatch.Success) emotesTag = emotesMatch.Groups[1].Value;

        var displayNameMatch = Regex.Match(line, @"(?:^|;)display-name=([^;]*)");
        if (displayNameMatch.Success && !string.IsNullOrWhiteSpace(displayNameMatch.Groups[1].Value))
        {
            user = UnescapeTwitchTag(displayNameMatch.Groups[1].Value);
        }
        else
        {
            var userMatch = Regex.Match(line, @":([^!]+)!");
            if (userMatch.Success) user = userMatch.Groups[1].Value;
        }

        var textIndex = line.IndexOf(" :", line.IndexOf(" PRIVMSG ", StringComparison.Ordinal), StringComparison.Ordinal);
        if (textIndex >= 0) text = line[(textIndex + 2)..];

        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;

        return new ChatMessage
        {
            Platform = "Twitch",
            UserName = user,
            Text = text,
            MessageHtml = BuildTwitchMessageHtml(text, emotesTag),
            AvatarUrl = TwitchAvatarResolver.GetAvatarUrl(user),
            Time = DateTime.Now,
        };
    }

    private static string BuildTwitchMessageHtml(string text, string emotesTag)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (string.IsNullOrWhiteSpace(emotesTag)) return WebUtility.HtmlEncode(text);

        var replacements = new List<(int Start, int End, string Html)>();
        foreach (var part in emotesTag.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.Split(':', 2);
            if (split.Length != 2) continue;
            var emoteId = split[0];
            foreach (var range in split[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var se = range.Split('-');
                if (se.Length != 2) continue;
                if (!int.TryParse(se[0], out var start)) continue;
                if (!int.TryParse(se[1], out var end)) continue;
                if (start < 0 || end < start || end >= text.Length) continue;
                var alt = text.Substring(start, end - start + 1);
                var src = "https://static-cdn.jtvnw.net/emoticons/v2/" + Uri.EscapeDataString(emoteId) + "/default/dark/2.0";
                var html = "<img class=\"emote twitch-emote\" src=\"" + WebUtility.HtmlEncode(src) + "\" alt=\"" + WebUtility.HtmlEncode(alt) + "\" title=\"" + WebUtility.HtmlEncode(alt) + "\">";
                replacements.Add((start, end, html));
            }
        }

        if (replacements.Count == 0) return WebUtility.HtmlEncode(text);

        var sb = new StringBuilder();
        var pos = 0;
        foreach (var r in replacements.OrderBy(x => x.Start))
        {
            if (r.Start < pos) continue;
            if (r.Start > pos) sb.Append(WebUtility.HtmlEncode(text.Substring(pos, r.Start - pos)));
            sb.Append(r.Html);
            pos = r.End + 1;
        }
        if (pos < text.Length) sb.Append(WebUtility.HtmlEncode(text[pos..]));
        return sb.ToString();
    }

    private static string UnescapeTwitchTag(string value)
    {
        return value.Replace("\\s", " ")
                    .Replace("\\:", ";")
                    .Replace("\\r", "\r")
                    .Replace("\\n", "\n")
                    .Replace("\\\\", "\\");
    }
}

public sealed class YoutubeWebViewChatSource
{
    private readonly WebView2 _webView;
    private readonly string _input;
    private System.Windows.Forms.Timer? _pollTimer;
    private bool _initialized;

    public event Action<ChatMessage>? MessageReceived;
    public event Action<string>? StatusChanged;

    public YoutubeWebViewChatSource(WebView2 webView, string input)
    {
        _webView = webView;
        _input = input.Trim();
    }

    public async Task StartAsync(CancellationToken token)
    {
        var videoId = ExtractYoutubeVideoId(_input);
        if (string.IsNullOrWhiteSpace(videoId))
        {
            throw new InvalidOperationException("YouTubeのURLまたはvideoIdを解析できませんでした。");
        }

        if (_webView.CoreWebView2 == null)
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IzCommentViewer",
                "WebView2"
            );
            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);
        }

        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
        _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _webView.NavigationCompleted -= OnNavigationCompleted;
        _webView.NavigationCompleted += OnNavigationCompleted;

        _initialized = false;
        var chatUrl = $"https://www.youtube.com/live_chat?v={videoId}&is_popout=1";
        _webView.Source = new Uri(chatUrl);

        _pollTimer = new System.Windows.Forms.Timer { Interval = 1200 };
        _pollTimer.Tick += async (_, _) => await InjectAndPollAsync();
        _pollTimer.Start();

        token.Register(Stop);
    }

    public void Stop()
    {
        try
        {
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer.Dispose();
                _pollTimer = null;
            }
            if (_webView.CoreWebView2 != null) _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }
        catch { }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            StatusChanged?.Invoke("YouTubeチャット読み込み失敗");
            return;
        }

        StatusChanged?.Invoke("YouTubeチャット読み込み完了");
        await InjectAndPollAsync();
    }

    private async Task InjectAndPollAsync()
    {
        if (_webView.CoreWebView2 == null) return;

        var script = @"
(() => {
  try {
    if (!window.__vv_seen) window.__vv_seen = new Set();
    const items = [];

    function escapeText(value) {
      const span = document.createElement('span');
      span.textContent = String(value || '');
      return span.innerHTML;
    }

    function escapeAttr(value) {
      return String(value || '')
        .replaceAll('&', '&amp;')
        .replaceAll(String.fromCharCode(34), '&quot;')
        .replaceAll(String.fromCharCode(39), '&#39;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;');
    }

    function nodeToHtml(node) {
      if (node.nodeType === Node.TEXT_NODE) {
        return escapeText(node.textContent || '');
      }

      if (node.nodeType !== Node.ELEMENT_NODE) return '';

      const el2 = node;

      // YouTube custom emoji often appears as an img inside yt-formatted-string / span.
      const img = el2.tagName === 'IMG'
        ? el2
        : (el2.querySelector ? el2.querySelector('img') : null);

      if (img && img.src) {
        const alt = img.alt
          || img.getAttribute('shared-tooltip-text')
          || img.getAttribute('aria-label')
          || img.title
          || '';
        return '<img class=\'emote youtube-emote\' src=\'' + escapeAttr(img.src) + '\' alt=\'' + escapeAttr(alt) + '\' title=\'' + escapeAttr(alt) + '\'>';
      }

      // Some YouTube emoji nodes keep their label in attributes without direct image hit.
      const aria = el2.getAttribute ? (el2.getAttribute('aria-label') || el2.getAttribute('title') || '') : '';
      if (aria) return escapeText(aria);

      return escapeText(el2.innerText || el2.textContent || '');
    }

    function messageToHtml(messageEl) {
      if (!messageEl) return '';
      return Array.from(messageEl.childNodes).map(nodeToHtml).join('');
    }

    const renderers = Array.from(document.querySelectorAll('yt-live-chat-text-message-renderer, yt-live-chat-paid-message-renderer, yt-live-chat-membership-item-renderer'));

    for (const el of renderers) {
      const id = el.id || el.getAttribute('id') || '';
      const authorEl = el.querySelector('#author-name');
      const messageEl = el.querySelector('#message');
      const photoEl = el.querySelector('#author-photo img, yt-img-shadow#author-photo img, img#img');

      const author = (authorEl && (authorEl.innerText || authorEl.textContent) || '').trim();
      const message = (messageEl && (messageEl.innerText || messageEl.textContent) || '').trim();
      const messageHtml = messageToHtml(messageEl);
      const avatarUrl = (photoEl && photoEl.src || '').trim();
      const index = el.parentNode && el.parentNode.children ? Array.prototype.indexOf.call(el.parentNode.children, el) : 0;
      const key = id || (author + ':' + message + ':' + index);

      if (!message || window.__vv_seen.has(key)) continue;

      window.__vv_seen.add(key);
      items.push({
        platform: 'YouTube',
        userName: author || 'unknown',
        text: message,
        html: messageHtml,
        avatarUrl: avatarUrl
      });
    }

    if (items.length > 0) {
      chrome.webview.postMessage(JSON.stringify({ type: 'messages', items: items }));
    }

    const statusText = document.body && document.body.innerText || '';
    if (statusText.includes('チャットが無効') || statusText.includes('Chat is disabled')) {
      chrome.webview.postMessage(JSON.stringify({ type: 'status', text: 'YouTube: チャットが無効です' }));
    }
  } catch (e) {
    chrome.webview.postMessage(JSON.stringify({ type: 'status', text: 'YouTube JS error: ' + e.message }));
  }
})();";

        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(script);
            if (!_initialized)
            {
                _initialized = true;
                StatusChanged?.Invoke("YouTubeコメント監視中");
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("YouTube監視エラー: " + ex.Message);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var raw = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(raw)) return;

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "";

            if (type == "status")
            {
                var text = root.TryGetProperty("text", out var textEl) ? textEl.GetString() : "";
                if (!string.IsNullOrWhiteSpace(text)) StatusChanged?.Invoke(text!);
                return;
            }

            if (type != "messages") return;
            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return;

            foreach (var item in items.EnumerateArray())
            {
                var user = item.TryGetProperty("userName", out var userEl) ? userEl.GetString() ?? "unknown" : "unknown";
                var text = item.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";
                var avatarUrl = item.TryGetProperty("avatarUrl", out var avatarEl) ? avatarEl.GetString() ?? "" : "";
                var messageHtml = item.TryGetProperty("html", out var htmlEl) ? htmlEl.GetString() ?? "" : "";
                text = text.Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                MessageReceived?.Invoke(new ChatMessage
                {
                    Platform = "YouTube",
                    UserName = user.Trim(),
                    Text = text,
                    MessageHtml = messageHtml.Trim(),
                    AvatarUrl = avatarUrl.Trim(),
                    Time = DateTime.Now,
                });
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("YouTubeメッセージ解析エラー: " + ex.Message);
        }
    }

    private static string ExtractYoutubeVideoId(string input)
    {
        input = input.Trim();
        if (Regex.IsMatch(input, @"^[a-zA-Z0-9_-]{11}$")) return input;

        var patterns = new[]
        {
            @"[?&]v=([a-zA-Z0-9_-]{11})",
            @"youtu\.be/([a-zA-Z0-9_-]{11})",
            @"youtube\.com/live/([a-zA-Z0-9_-]{11})",
            @"youtube\.com/embed/([a-zA-Z0-9_-]{11})",
            @"/shorts/([a-zA-Z0-9_-]{11})",
        };

        foreach (var p in patterns)
        {
            var m = Regex.Match(input, p, RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value;
        }

        return "";
    }
}
