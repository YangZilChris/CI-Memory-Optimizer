using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace CI更好的内存优化.Models;

public sealed class PluginSettings : INotifyPropertyChanged
{
    private bool _optimizationEnabled = true;
    private bool _trimWorkingSet = true;
    private int _intervalIndex = 2;
    private int _thresholdIndex = 2;
    private string _acceptedAgreementVersion = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool OptimizationEnabled
    {
        get => _optimizationEnabled;
        set => SetField(ref _optimizationEnabled, value);
    }

    public bool TrimWorkingSet
    {
        get => _trimWorkingSet;
        set => SetField(ref _trimWorkingSet, value);
    }

    public int IntervalIndex
    {
        get => _intervalIndex;
        set => SetField(ref _intervalIndex, Math.Clamp(value, 0, 3));
    }

    public int ThresholdIndex
    {
        get => _thresholdIndex;
        set => SetField(ref _thresholdIndex, Math.Clamp(value, 0, 3));
    }

    public string AcceptedAgreementVersion
    {
        get => _acceptedAgreementVersion;
        set => SetField(ref _acceptedAgreementVersion, value ?? "");
    }

    [JsonIgnore]
    public bool HasAcceptedCurrentAgreement => AcceptedAgreementVersion == "1.2";

    [JsonIgnore]
    public int IntervalMinutes => IntervalIndex switch
    {
        0 => 1,
        1 => 5,
        2 => 15,
        _ => 30
    };

    [JsonIgnore]
    public long ManagedHeapThresholdBytes => (long)(ThresholdIndex switch
    {
        0 => 128,
        1 => 256,
        2 => 512,
        _ => 1024
    }) * 1024 * 1024;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
