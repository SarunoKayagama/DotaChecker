using CommunityToolkit.Mvvm.ComponentModel;
using DotaChecker.Core.Models;

namespace DotaChecker.App.ViewModels;

public sealed class ConnectionRowViewModel : ObservableObject
{
    public ConnectionRowViewModel(ConnectionInfo info) => Info = info;

    public ConnectionInfo Info { get; private set; }

    public string Protocol => Info.Protocol.ToString();

    public string LocalEndPoint => $"{Info.LocalAddress}:{Info.LocalPort}";

    public string RemoteEndPoint => Info.HasRemoteEndpoint
        ? $"{Info.RemoteAddress}:{Info.RemotePort}"
        : "—";

    public string RemoteIp => Info.HasRemoteEndpoint ? Info.DiagnosticAddress.ToString() : "—";

    public string RemoteIpPort => Info.HasRemoteEndpoint
        ? $"{Info.DiagnosticAddress}:{Info.RemotePort}"
        : "—";

    public int ProcessId => Info.ProcessId;

    public string ProcessName => Info.ProcessName;

    public string ProcessPath => Info.ProcessPath;

    public string State => Info.TcpState;

    public string CreatedAt => Info.HasCreationTime
        ? Info.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
        : "н/д";

    public bool CanDiagnose => Info.HasRemoteEndpoint;

    public void Update(ConnectionInfo info)
    {
        Info = info;
        OnPropertyChanged(string.Empty);
    }
}
