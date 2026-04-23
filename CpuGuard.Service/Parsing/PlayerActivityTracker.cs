namespace CpuGuard.Service.Parsing;

public sealed class PlayerCountChangedEventArgs : EventArgs
{
    public PlayerCountChangedEventArgs(int previousCount, int currentCount, string trigger)
    {
        PreviousCount = previousCount;
        CurrentCount = currentCount;
        Trigger = trigger;
    }

    public int PreviousCount { get; }

    public int CurrentCount { get; }

    public string Trigger { get; }
}

public sealed class PlayerActivityTracker
{
    private const string JoinToken = "Join succeeded:";
    private const string DisconnectToken = "PlayerDisconnected. State Connected";
    private const string ProcessAddPlayerToken = "ProcessAddPlayer";

    private readonly object _sync = new();
    private int _playerCount;

    public event EventHandler<PlayerCountChangedEventArgs>? PlayerCountChanged;
    public event Action? ProcessAddPlayerDetected;

    public int CurrentPlayerCount
    {
        get
        {
            lock (_sync)
            {
                return _playerCount;
            }
        }
    }

    public void ProcessLogLine(string line)
    {
        var hasJoin = line.Contains(JoinToken, StringComparison.OrdinalIgnoreCase);
        var hasDisconnect = line.Contains(DisconnectToken, StringComparison.OrdinalIgnoreCase);
        var hasProcessAddPlayer = line.Contains(ProcessAddPlayerToken, StringComparison.OrdinalIgnoreCase);

        if (hasProcessAddPlayer)
        {
            ProcessAddPlayerDetected?.Invoke();
        }

        if (!hasJoin && !hasDisconnect)
        {
            return;
        }

        int previous;
        int current;
        string trigger;

        lock (_sync)
        {
            previous = _playerCount;
            if (hasJoin && !hasDisconnect)
            {
                _playerCount++;
                trigger = "join";
            }
            else if (hasDisconnect && !hasJoin)
            {
                _playerCount = Math.Max(0, _playerCount - 1);
                trigger = "disconnect";
            }
            else
            {
                trigger = "mixed";
            }

            current = _playerCount;
        }

        if (current != previous)
        {
            PlayerCountChanged?.Invoke(this, new PlayerCountChangedEventArgs(previous, current, trigger));
        }
    }
}
