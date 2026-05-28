using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Quality;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace LocalCoop.Mod.Runtime;

public sealed class BrokerNetGameService : INetHostGameService, IDisposable
{
    private readonly BrokerBackedNetService _inner;
    private readonly NetGameType _type;
    private readonly CancellationTokenSource _receiveLoopCancellation = new();
    private readonly Task _receiveLoop;
    private readonly Dictionary<Delegate, Delegate> _registeredHandlers = new();
    private Action<NetErrorInfo>? _disconnected;
    private Action<ulong>? _clientConnected;
    private Action<ulong, NetErrorInfo>? _clientDisconnected;
    private int _disposed;

    public BrokerNetGameService(BrokerBackedNetService inner, NetGameType type)
    {
        _inner = inner;
        _type = type;
        _receiveLoop = _inner.RunReceiveLoopAsync(_receiveLoopCancellation.Token);
    }

    public ulong NetId => _inner.NetId;

    public bool IsConnected => _inner.IsConnected;

    public bool IsGameLoading => _inner.IsGameLoading;

    public NetGameType Type => _type;

    public PlatformType Platform => PlatformType.None;

    public IReadOnlyList<NetClientData> ConnectedPeers { get; } = [];

    public NetHost NetHost => null!;

    public event Action<NetErrorInfo>? Disconnected
    {
        add => _disconnected += value;
        remove => _disconnected -= value;
    }

    public event Action<ulong>? ClientConnected
    {
        add => _clientConnected += value;
        remove => _clientConnected -= value;
    }

    public event Action<ulong, NetErrorInfo>? ClientDisconnected
    {
        add => _clientDisconnected += value;
        remove => _clientDisconnected -= value;
    }

    public void SendMessage<T>(T message, ulong playerId)
        where T : INetMessage
    {
        _inner.SendMessage(message, playerId);
    }

    public void SendMessage<T>(T message)
        where T : INetMessage
    {
        _inner.SendMessage(message);
    }

    public void RegisterMessageHandler<T>(MessageHandlerDelegate<T> messageHandlerDelegate)
        where T : INetMessage
    {
        Action<T, ulong> adapter = (message, senderId) => messageHandlerDelegate(message, senderId);
        _registeredHandlers[messageHandlerDelegate] = adapter;
        _inner.RegisterMessageHandler(adapter);
    }

    public void UnregisterMessageHandler<T>(MessageHandlerDelegate<T> messageHandlerDelegate)
        where T : INetMessage
    {
        if (!_registeredHandlers.Remove(messageHandlerDelegate, out var adapter))
        {
            return;
        }

        _inner.UnregisterMessageHandler((Action<T, ulong>)adapter);
    }

    public void Update()
    {
        _inner.Update();
    }

    public void Disconnect(NetError reason, bool now)
    {
        _inner.Disconnect();
        Dispose();
    }

    public void DisconnectClient(ulong peerId, NetError reason, bool now)
    {
    }

    public void SetPeerReadyForBroadcasting(ulong peerId)
    {
    }

    public ConnectionStats GetStatsForPeer(ulong peerId)
    {
        return new ConnectionStats(peerId);
    }

    public void SetGameLoading(bool isLoading)
    {
        _inner.SetGameLoading(isLoading);
    }

    public string GetRawLobbyIdentifier()
    {
        return _inner.GetRawLobbyIdentifier();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _receiveLoopCancellation.Cancel();
        _receiveLoopCancellation.Dispose();
    }
}
