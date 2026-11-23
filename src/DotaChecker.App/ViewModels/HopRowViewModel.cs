using CommunityToolkit.Mvvm.ComponentModel;
using DotaChecker.Core.Models;

namespace DotaChecker.App.ViewModels;

public sealed class HopRowViewModel : ObservableObject
{
    public void Update(MonitorHopView view)
    {
        HopIndex = view.HopIndex;
        Address = view.AddressText;
        Hostname = view.Hostname;
        IsDestination = view.IsDestination;
        Sent = view.Statistics.Sent;
        Received = view.Statistics.Received;
        LossPct = view.Statistics.LossPct;
        LastMs = view.Statistics.LastMs;
        AverageMs = view.Statistics.AverageMs;
        BestMs = view.Statistics.BestMs;
        WorstMs = view.Statistics.WorstMs;
        JitterMs = view.Statistics.JitterMs;
        RttHistory = view.Statistics.RttHistory;
        SuccessHistory = view.Statistics.SuccessHistory;

        OnPropertyChanged(string.Empty);
    }

    public int HopIndex { get; private set; }

    public string Address { get; private set; } = "—";

    public string Hostname { get; private set; } = "—";

    public bool IsDestination { get; private set; }

    public long Sent { get; private set; }

    public long Received { get; private set; }

    public double LossPct { get; private set; }

    public double LastMs { get; private set; }

    public double AverageMs { get; private set; }

    public double BestMs { get; private set; }

    public double WorstMs { get; private set; }

    public double JitterMs { get; private set; }

    public IReadOnlyList<double> RttHistory { get; private set; } = Array.Empty<double>();

    public IReadOnlyList<bool> SuccessHistory { get; private set; } = Array.Empty<bool>();

    public string HopLabel => HopIndex == 0 ? "e2e" : HopIndex.ToString();

    public string StatusText
    {
        get
        {
            if (Sent == 0)
            {
                return "ожидание";
            }

            if (Received == 0)
            {
                return "нет ответа";
            }

            if (LossPct >= 20)
            {
                return "высокие потери";
            }

            if (LossPct >= 5)
            {
                return "потери";
            }

            return "норма";
        }
    }

    public string LossText => $"{LossPct:F1}%";

    // Windows ICMP API сообщает RTT целыми миллисекундами: 0 мс означает "<1 мс".
    private static string FormatMs(double value) => value == 0 ? "<1 мс" : $"{value:F0} мс";

    public string LastText => Sent == 0 || Received == 0 ? "—" : FormatMs(LastMs);

    public string AverageText => Received == 0 ? "—" : FormatMs(AverageMs);

    public string BestText => Received == 0 ? "—" : FormatMs(BestMs);

    public string WorstText => Received == 0 ? "—" : FormatMs(WorstMs);

    public string JitterText => Received < 2 ? "—" : FormatMs(JitterMs);
}
