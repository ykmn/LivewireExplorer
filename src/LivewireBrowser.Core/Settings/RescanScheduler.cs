namespace LivewireBrowser.Core.Settings;

public class RescanScheduler : IDisposable
{
    private Timer? _timer;
    private readonly Func<Task> _onTriggered;

    public RescanScheduler(Func<Task> onTriggered)
    {
        _onTriggered = onTriggered;
    }

    public void Configure(int periodMinutes)
    {
        _timer?.Dispose();
        _timer = null;

        if (periodMinutes <= 0)
            return;

        var period = TimeSpan.FromMinutes(periodMinutes);
        _timer = new Timer(_ => _ = _onTriggered(), null, period, period);
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
