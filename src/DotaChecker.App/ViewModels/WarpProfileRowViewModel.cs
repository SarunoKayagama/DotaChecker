using CommunityToolkit.Mvvm.ComponentModel;
using DotaChecker.Core.Warp;

namespace DotaChecker.App.ViewModels;

public sealed partial class WarpProfileRowViewModel : ObservableObject
{
    public WarpProfileRowViewModel(AmneziaWgProfileInfo profile)
    {
        Name = profile.Name;
        Path = profile.FilePath;
        FileStatus = profile.FileExists ? "найден" : "отсутствует";
        Valid = profile.IsStructurallyValid ? "структура OK" : "структура неполная";
        Endpoint = profile.PublicEndpoint ?? "—";
        TunnelStatus = "не проверялся";
        Warnings = string.Join(Environment.NewLine, profile.Warnings);
        SecretNotice = profile.SecretSummary;
    }

    public string Name { get; }
    public string Path { get; }
    public string FileStatus { get; }
    public string Valid { get; }
    public string Endpoint { get; }
    public string Warnings { get; }
    public string SecretNotice { get; }

    [ObservableProperty]
    private string _tunnelStatus;

    public void SetTunnel(AmneziaWgTunnelStatus status) =>
        TunnelStatus = $"{status.State}: {status.ServiceStatus} {status.LastHandshake}";
}
