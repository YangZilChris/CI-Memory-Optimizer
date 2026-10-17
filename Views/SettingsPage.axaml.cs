using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;

namespace CI更好的内存优化.Views;

[SettingsPageInfo("ci.better-memory-optimizer.settings", "CI更好的内存优化")]
public partial class SettingsPage : SettingsPageBase
{
    private readonly Plugin _plugin;
    private bool _isInitializing;
    private bool _agreementPromptInProgress;

    public SettingsPage() : this(IAppHost.GetService<Plugin>())
    {
    }

    public SettingsPage(Plugin plugin)
    {
        _plugin = plugin;
        InitializeComponent();
        _isInitializing = true;
        OptimizationEnabledCheckBox.IsChecked = plugin.Settings.OptimizationEnabled;
        TrimWorkingSetCheckBox.IsChecked = plugin.Settings.TrimWorkingSet;
        IntervalComboBox.SelectedIndex = plugin.Settings.IntervalIndex;
        ThresholdComboBox.SelectedIndex = plugin.Settings.ThresholdIndex;
        AggressiveModeCheckBox.IsChecked = plugin.AggressiveModeEnabled;
        _isInitializing = false;
        RefreshStatus();
        Loaded += SettingsPage_OnLoaded;
    }

    private void OptimizationEnabled_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_isInitializing)
        {
            _plugin.Settings.OptimizationEnabled = OptimizationEnabledCheckBox.IsChecked == true;
        }
    }

    private void TrimWorkingSet_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_isInitializing)
        {
            _plugin.Settings.TrimWorkingSet = TrimWorkingSetCheckBox.IsChecked == true;
        }
    }

    private void IntervalComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializing && IntervalComboBox.SelectedIndex >= 0)
        {
            _plugin.Settings.IntervalIndex = IntervalComboBox.SelectedIndex;
        }
    }

    private void ThresholdComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializing && ThresholdComboBox.SelectedIndex >= 0)
        {
            _plugin.Settings.ThresholdIndex = ThresholdComboBox.SelectedIndex;
        }
    }

    private async void AggressiveMode_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        if (AggressiveModeCheckBox.IsChecked == true)
        {
            var enabled = await _plugin.ConfirmAndEnableAggressiveModeAsync(TopLevel.GetTopLevel(this));
            if (!enabled)
            {
                _isInitializing = true;
                AggressiveModeCheckBox.IsChecked = false;
                _isInitializing = false;
            }
        }
        else
        {
            _plugin.DisableAggressiveMode();
        }

        RefreshStatus();
    }

    private async void AgreementsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _plugin.PromptAgreementsAsync(TopLevel.GetTopLevel(this));
        RefreshStatus();
    }

    private async void SettingsPage_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_agreementPromptInProgress || _plugin.Settings.HasAcceptedCurrentAgreement)
        {
            return;
        }

        _agreementPromptInProgress = true;
        try
        {
            await _plugin.PromptAgreementsAsync(TopLevel.GetTopLevel(this));
            RefreshStatus();
        }
        finally
        {
            _agreementPromptInProgress = false;
        }
    }

    private void RefreshStatus()
    {
        AgreementStatusText.Text = _plugin.GetStatusMessage();
        StatusText.Text = _plugin.AggressiveModeEnabled
            ? "激进模式已开启（仅本次运行有效）。"
            : "激进模式当前关闭。";
    }
}
