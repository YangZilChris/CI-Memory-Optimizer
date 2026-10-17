// 你好呀awa
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Core.Helpers.UI;
using ClassIsland.Shared;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CI更好的内存优化.Models;
using CI更好的内存优化.Views;

namespace CI更好的内存优化;

[PluginEntrance]
public sealed class Plugin : PluginBase
{
    private const string AgreementVersion = "1.2";
    private readonly object _saveLock = new();
    private readonly string[] _agreementTitles = ["用户协议", "隐私协议", "免责声明"];
    private readonly string[] _agreementFiles = ["UserAgreement.md", "PrivacyAgreement.md", "Disclaimer.md"];
    private Timer? _timer;
    private DateTime _lastCheckUtc = DateTime.MinValue;
    private int _isOptimizing;
    private string? _configurationIssue;
    private string? _agreementIssue;
    private string[] _agreementContents = [];
    private ILogger<Plugin>? _logger;

    public PluginSettings Settings { get; private set; } = new();
    public bool AggressiveModeEnabled { get; private set; }

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        try
        {
            Directory.CreateDirectory(PluginConfigFolder);
            Settings = LoadSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _configurationIssue = $"无法访问插件配置目录，优化功能不会启用。请检查目录权限。详情：{ex.Message}";
            LogError(_configurationIssue, ex);
            Settings = new PluginSettings();
        }

        Settings.PropertyChanged += (_, _) => SaveSettings();
        _agreementContents = LoadAgreements();

        services.AddSingleton(Settings);
        services.AddSettingsPage<SettingsPage>();

        AppBase.Current.AppStarted += async (_, _) => await BeginAfterStartupAsync();
    }

    public async Task<bool> PromptAgreementsAsync(TopLevel? root)
    {
        if (root is null || _agreementIssue is not null)
        {
            return false;
        }

        for (var i = 0; i < _agreementContents.Length; i++)
        {
            var dialog = new ContentDialog
            {
                Title = _agreementTitles[i],
                Content = new ScrollViewer
                {
                    MaxHeight = 420,
                    Content = new TextBlock
                    {
                        Text = _agreementContents[i],
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                },
                PrimaryButtonText = "同意",
                CloseButtonText = "不同意",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync(root) != ContentDialogResult.Primary)
            {
                return false;
            }
        }

        Settings.AcceptedAgreementVersion = AgreementVersion;
        StartOptimizationTimer();
        return true;
    }

    public async Task<bool> ConfirmAndEnableAggressiveModeAsync(TopLevel? root)
    {
        var confirmed = await ContentDialogHelper.ShowConfirmationDialog(
            "确认开启激进模式",
            "此模式会更频繁地强制回收并压缩 ClassIsland 自身的 .NET 堆，还会尝试修剪 ClassIsland 自身的 Windows 工作集。它可能导致卡顿、CPU 突增、重新加载页面、崩溃或退出；内存也可能很快被系统重新调入。不会结束或修改其他应用。\n\n非必要请勿开启。上课使用时建议先开启 ClassIsland 的教学安全模式，但该安全模式不能保证避免故障。",
            root: root,
            positiveText: "我了解风险，仍要开启",
            negativeText: "取消");

        AggressiveModeEnabled = confirmed;
        return confirmed;
    }

    public void DisableAggressiveMode()
    {
        AggressiveModeEnabled = false;
    }

    public string? GetStatusMessage()
    {
        if (_configurationIssue is not null)
        {
            return _configurationIssue;
        }

        if (_agreementIssue is not null)
        {
            return _agreementIssue;
        }

        return Settings.HasAcceptedCurrentAgreement
            ? "协议已同意。优化仅在 ClassIsland 内运行。"
            : "尚未同意协议；优化功能保持关闭。";
    }

    private async Task BeginAfterStartupAsync()
    {
        _logger = IAppHost.TryGetService<ILogger<Plugin>>();

        if (_configurationIssue is not null)
        {
            await ShowNoticeAsync(_configurationIssue);
            return;
        }

        if (_agreementIssue is not null)
        {
            await ShowNoticeAsync(_agreementIssue);
            return;
        }

        if (Settings.HasAcceptedCurrentAgreement)
        {
            StartOptimizationTimer();
        }
    }

    private async Task ShowNoticeAsync(string message)
    {
        var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var root = desktop?.MainWindow;
        if (root is null)
        {
            LogError(message);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "CI更好的内存优化",
            Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            CloseButtonText = "知道了"
        };
        await dialog.ShowAsync(root);
    }

    private PluginSettings LoadSettings()
    {
        var path = Path.Combine(PluginConfigFolder, "Settings.json");
        if (!File.Exists(path))
        {
            return new PluginSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(path)) ?? new PluginSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _configurationIssue = $"无法读取插件设置，已使用默认设置；原文件未被覆盖。请检查文件权限或备份后修复 Settings.json。详情：{ex.Message}";
            LogError(_configurationIssue, ex);
            return new PluginSettings();
        }
    }

    private string[] LoadAgreements()
    {
        var assemblyFolder = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
        if (string.IsNullOrWhiteSpace(assemblyFolder))
        {
            _agreementIssue = "无法定位插件目录，协议无法加载，因此优化功能不会启用。";
            return [];
        }

        var folder = Path.Combine(assemblyFolder, "Agreements");
        var contents = new List<string>(_agreementFiles.Length);
        foreach (var file in _agreementFiles)
        {
            var path = Path.Combine(folder, file);
            if (!File.Exists(path))
            {
                _agreementIssue = $"缺少协议文件：{file}。请重新安装完整插件包；在协议可读前，优化功能不会启用。";
                return [];
            }

            try
            {
                contents.Add(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _agreementIssue = $"无法读取协议文件 {file}：{ex.Message}。优化功能不会启用。";
                return [];
            }
        }

        return [.. contents];
    }

    private void SaveSettings()
    {
        var path = Path.Combine(PluginConfigFolder, "Settings.json");
        var temporaryPath = path + ".tmp";
        try
        {
            lock (_saveLock)
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporaryPath, path, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogError("保存插件设置失败；本次更改可能不会在重启后保留。", ex);
        }
    }

    private void StartOptimizationTimer()
    {
        if (_configurationIssue is not null || _agreementIssue is not null || !Settings.HasAcceptedCurrentAgreement)
        {
            return;
        }

        _timer ??= new Timer(OptimizeIfNeeded, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    private void OptimizeIfNeeded(object? state)
    {
        if (_configurationIssue is not null || _agreementIssue is not null ||
            !Settings.HasAcceptedCurrentAgreement || !Settings.OptimizationEnabled)
        {
            return;
        }

        var interval = AggressiveModeEnabled
            ? TimeSpan.FromMinutes(5)
            : TimeSpan.FromMinutes(Settings.IntervalMinutes);
        var now = DateTime.UtcNow;
        if (now - _lastCheckUtc < interval || Interlocked.Exchange(ref _isOptimizing, 1) != 0)
        {
            return;
        }

        try
        {
            _lastCheckUtc = now;
            var managedHeapBytes = GC.GetTotalMemory(false);
            var shouldCollect = managedHeapBytes >= Settings.ManagedHeapThresholdBytes;
            if (shouldCollect && AggressiveModeEnabled)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
            else if (shouldCollect)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);
            }

            if (Settings.TrimWorkingSet && OperatingSystem.IsWindows() && !TrimCurrentProcessWorkingSet())
            {
                LogError($"修剪 ClassIsland 工作集失败，Windows 错误码：{Marshal.GetLastWin32Error()}。");
            }

        }
        catch (Exception ex)
        {
            LogError("内存优化操作失败；ClassIsland 将继续运行，后续周期仍会重试。", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _isOptimizing, 0);
        }
    }

    private void LogError(string message, Exception? exception = null)
    {
        if (_logger is not null)
        {
            _logger.LogError(exception, "{Message}", message);
            return;
        }

        Console.Error.WriteLine(exception is null ? message : $"{message} {exception}");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimumWorkingSetSize, IntPtr maximumWorkingSetSize);

    private static bool TrimCurrentProcessWorkingSet() =>
        SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
}
