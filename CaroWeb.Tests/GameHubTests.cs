using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class GameHubTests
{
    [Fact]
    public async Task JoinRoomRejectsMalformedRoomCode()
    {
        var caller = new RecordingClientProxy();
        var hub = CreateHub("invalid-code-caller", caller);

        await hub.JoinRoom("bad!");

        Assert.Contains(caller.Messages, message =>
            message.Method == "ErrorMessage" &&
            (string)message.Arguments[0]! == "Mã phòng không hợp lệ.");
    }

    [Fact]
    public async Task NicknamesAreIncludedInRoomStateForBothPlayers()
    {
        var hostClient = new RecordingClientProxy();
        var host = CreateHub("nickname-host", hostClient);
        await host.SetNickname("Minh Anh");
        await host.CreateRoom();
        var roomCode = (string)hostClient.Single("RoomCreated").Arguments[0]!;

        var guestClient = new RecordingClientProxy();
        var guest = CreateHub("nickname-guest", guestClient);
        await guest.SetNickname("Gia Huy");
        await guest.JoinRoom(roomCode);

        var state = (GameHub.RoomSnapshot)guestClient.Messages
            .Last(message => message.Method == "GameState").Arguments[0]!;
        Assert.Equal("Minh Anh", state.NicknameX);
        Assert.Equal("Gia Huy", state.NicknameO);

        await host.SetNickname("Minh");
        var updatedState = (GameHub.RoomSnapshot)hostClient.Messages
            .Last(message => message.Method == "GameState").Arguments[0]!;
        Assert.Equal("Minh", updatedState.NicknameX);
    }

    [Fact]
    public async Task NicknameLongerThanLimitIsRejected()
    {
        var caller = new RecordingClientProxy();
        var hub = CreateHub("nickname-invalid", caller);

        await hub.SetNickname(new string('a', 21));

        Assert.Contains(caller.Messages, message =>
            message.Method == "ErrorMessage" &&
            ((string)message.Arguments[0]!).Contains("20"));
    }

    [Fact]
    public async Task PlayerCannotPlayTwiceWhenTurnHasPassed()
    {
        var hostClient = new RecordingClientProxy();
        var host = CreateHub("turn-host", hostClient);
        await host.CreateRoom();
        var roomCode = (string)hostClient.Single("RoomCreated").Arguments[0]!;

        var guestClient = new RecordingClientProxy();
        var guest = CreateHub("turn-guest", guestClient);
        await guest.JoinRoom(roomCode);
        await host.MakeMove(7, 7);
        await host.MakeMove(7, 8);

        Assert.Contains(hostClient.Messages, message => message.Method == "MoveRejected");
    }

    [Fact]
    public async Task AbruptDisconnectCanRejoinAndRestoreTheBoard()
    {
        var hostClient = new RecordingClientProxy();
        var host = CreateHub("disconnect-host", hostClient);
        await host.SetNickname("Tuan");
        await host.CreateRoom();
        var created = hostClient.Single("RoomCreated");
        var roomCode = (string)created.Arguments[0]!;
        var playerToken = (string)created.Arguments[2]!;

        var guestClient = new RecordingClientProxy();
        var guest = CreateHub("disconnect-guest", guestClient);
        await guest.JoinRoom(roomCode);
        await host.MakeMove(7, 7);
        await host.OnDisconnectedAsync(null);

        var reconnectClient = new RecordingClientProxy();
        var reconnectingHost = CreateHub("disconnect-host-reconnected", reconnectClient);
        await reconnectingHost.Rejoin(playerToken);

        Assert.Contains(reconnectClient.Messages, message => message.Method == "RoomRejoined");
        var restoredState = reconnectClient.Single("GameState");
        var snapshot = (GameHub.RoomSnapshot)restoredState.Arguments[0]!;
        Assert.Equal('X', snapshot.Board[7][7]);
        Assert.Equal("Tuan", snapshot.NicknameX);
    }

    [Fact]
    public async Task UndoChangesBoardOnlyAfterOpponentAccepts()
    {
        var hostClient = new RecordingClientProxy();
        var host = CreateHub("undo-host", hostClient);
        await host.CreateRoom();
        var roomCode = (string)hostClient.Single("RoomCreated").Arguments[0]!;

        var guestClient = new RecordingClientProxy();
        var guest = CreateHub("undo-guest", guestClient);
        await guest.JoinRoom(roomCode);
        await host.MakeMove(7, 7);
        await guest.MakeMove(7, 8);
        await host.RequestUndo();
        var waitingState = (GameHub.RoomSnapshot)hostClient.Messages
            .Last(message => message.Method == "GameState").Arguments[0]!;
        Assert.Equal('O', waitingState.Board[7][8]);
        Assert.True(waitingState.HasPendingRequest);

        await guest.RespondUndo(true);

        var state = (GameHub.RoomSnapshot)guestClient.Messages
            .Last(message => message.Method == "GameState").Arguments[0]!;
        Assert.Equal('X', state.Board[7][7]);
        Assert.Equal('.', state.Board[7][8]);
        Assert.Equal("O", state.NextTurn);
        Assert.False(state.HasPendingRequest);
    }

    [Fact]
    public async Task RestartRequiresAcceptanceAndThenClearsBoard()
    {
        var hostClient = new RecordingClientProxy();
        var host = CreateHub("restart-host", hostClient);
        await host.CreateRoom();
        var roomCode = (string)hostClient.Single("RoomCreated").Arguments[0]!;

        var guestClient = new RecordingClientProxy();
        var guest = CreateHub("restart-guest", guestClient);
        await guest.JoinRoom(roomCode);
        await host.MakeMove(7, 7);
        await host.RequestRestart();

        var pendingState = (GameHub.RoomSnapshot)hostClient.Messages
            .Last(message => message.Method == "GameState").Arguments[0]!;
        Assert.Equal('X', pendingState.Board[7][7]);
        Assert.True(pendingState.HasPendingRequest);

        await guest.RespondRestart(true);

        var restartedState = (GameHub.RoomSnapshot)guestClient.Messages
            .Last(message => message.Method == "GameState").Arguments[0]!;
        Assert.Equal('.', restartedState.Board[7][7]);
        Assert.False(restartedState.HasPendingRequest);
    }

    private static GameHub CreateHub(string connectionId, RecordingClientProxy caller)
    {
        var hub = new GameHub(
            NullLogger<GameHub>.Instance);
        hub.Context = new TestHubCallerContext(connectionId);
        hub.Clients = new RecordingHubCallerClients(caller);
        hub.Groups = new TestGroupManager();
        return hub;
    }

    private sealed class RecordingClientProxy : IClientProxy
    {
        public List<ClientMessage> Messages { get; } = [];

        public Task SendCoreAsync(
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
        {
            Messages.Add(new ClientMessage(method, args));
            return Task.CompletedTask;
        }

        public ClientMessage Single(string method) =>
            Assert.Single(Messages, message => message.Method == method);
    }

    private sealed record ClientMessage(string Method, object?[] Arguments);

    private sealed class RecordingHubCallerClients(RecordingClientProxy caller) : IHubCallerClients
    {
        private readonly Dictionary<string, RecordingClientProxy> _clients = new();
        public IClientProxy Caller => caller;
        public IClientProxy Others => caller;
        public IClientProxy All => caller;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => caller;
        public IClientProxy Client(string connectionId) => GetClient(connectionId);
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => caller;
        public IClientProxy Group(string groupName) => caller;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => caller;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => caller;
        public IClientProxy User(string userId) => caller;
        public IClientProxy Users(IReadOnlyList<string> userIds) => caller;
        public IClientProxy OthersInGroup(string groupName) => caller;

        private RecordingClientProxy GetClient(string connectionId)
        {
            if (!_clients.TryGetValue(connectionId, out var client))
            {
                client = new RecordingClientProxy();
                _clients[connectionId] = client;
            }
            return client;
        }
    }

    private sealed class TestHubCallerContext(string connectionId) : HubCallerContext
    {
        public override string ConnectionId { get; } = connectionId;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class TestGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveFromGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
