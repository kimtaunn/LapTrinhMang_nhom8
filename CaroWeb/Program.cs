using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ??
    builder.Configuration["Redis:ConnectionString"];
var signalRBuilder = builder.Services.AddSignalR();
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(
        _ => StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnectionString));
    builder.Services.AddSingleton<RedisRoomStateCoordinator>();
    signalRBuilder.AddStackExchangeRedis(redisConnectionString);
    signalRBuilder.AddHubOptions<GameHub>(options =>
        options.AddFilter<RedisRoomStateFilter>());
}

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<GameHub>("/game");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

public sealed class GameHub : Hub
{
    private const int BoardSize = CaroRules.BoardSize;
    private const int ChatLimit = 250;
    private const string RoomCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly TimeSpan ReconnectTimeout = TimeSpan.FromSeconds(45);
    private static readonly ConcurrentDictionary<string, GameRoom> Rooms = new();
    private static readonly ConcurrentDictionary<string, PlayerBinding> PlayerBindings = new();
    private readonly ILogger<GameHub> _logger;
    private readonly RedisRoomStateCoordinator? _stateCoordinator;

    public GameHub(
        ILogger<GameHub> logger,
        RedisRoomStateCoordinator? stateCoordinator = null)
    {
        _logger = logger;
        _stateCoordinator = stateCoordinator;
    }

    internal static string ExportSharedState()
    {
        var state = new SharedRoomState(
            Rooms.ToDictionary(entry => entry.Key, entry => entry.Value.ToPersisted()),
            PlayerBindings.ToDictionary(entry => entry.Key, entry => entry.Value));
        return JsonSerializer.Serialize(state);
    }

    internal static void RestoreSharedState(string? serializedState)
    {
        SharedRoomState state;
        try
        {
            state = string.IsNullOrWhiteSpace(serializedState)
                ? new SharedRoomState([], [])
                : JsonSerializer.Deserialize<SharedRoomState>(serializedState) ??
                    throw new InvalidOperationException("Redis returned an invalid room state.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Redis returned an invalid room state.", exception);
        }

        foreach (var (roomCode, persistedRoom) in state.Rooms.ToArray())
        {
            if (IsDisconnectExpired(persistedRoom.HostDisconnectedAt))
            {
                state.Rooms.Remove(roomCode);
                foreach (var token in state.PlayerBindings
                    .Where(entry => entry.Value.RoomCode == roomCode)
                    .Select(entry => entry.Key)
                    .ToArray())
                    state.PlayerBindings.Remove(token);
                continue;
            }

            if (IsDisconnectExpired(persistedRoom.GuestDisconnectedAt))
            {
                if (persistedRoom.GuestToken is not null)
                    state.PlayerBindings.Remove(persistedRoom.GuestToken);
                state.Rooms[roomCode] = persistedRoom with
                {
                    GuestToken = null,
                    GuestConnectionId = null,
                    GuestDisconnectedAt = null,
                    GuestDisconnectGeneration = null,
                    RestartRequester = null,
                    RestartRequesterSide = null,
                    UndoRequester = null,
                    UndoRequesterSide = null
                };
            }
        }

        foreach (var roomCode in Rooms.Keys)
            if (!state.Rooms.ContainsKey(roomCode))
                Rooms.TryRemove(roomCode, out _);

        foreach (var (roomCode, persistedRoom) in state.Rooms)
        {
            var room = Rooms.GetOrAdd(roomCode, _ => GameRoom.Restore(persistedRoom));
            lock (room.Sync)
                room.ApplyPersisted(persistedRoom);
        }

        PlayerBindings.Clear();
        foreach (var (token, binding) in state.PlayerBindings)
            PlayerBindings[token] = binding;
    }

    private static bool IsDisconnectExpired(DateTimeOffset? disconnectedAt) =>
        disconnectedAt is { } time && DateTimeOffset.UtcNow - time > ReconnectTimeout;

    private sealed record SharedRoomState(
        Dictionary<string, PersistedGameRoom> Rooms,
        Dictionary<string, PlayerBinding> PlayerBindings);

    private sealed record PersistedGameRoom(
        string? HostConnectionId,
        string HostToken,
        string? GuestToken,
        bool IsBotGame,
        string BotDifficulty,
        string? GuestConnectionId,
        string[] Board,
        string? Winner,
        string NextTurn,
        int Round,
        int ScoreX,
        int ScoreO,
        DateTimeOffset TurnStartedAt,
        DateTimeOffset? TurnFinishedAt,
        int? LastRow,
        int? LastColumn,
        string? RestartRequester,
        string? RestartRequesterSide,
        DateTimeOffset? HostDisconnectedAt,
        DateTimeOffset? GuestDisconnectedAt,
        string? HostDisconnectGeneration,
        string? GuestDisconnectGeneration,
        string? UndoRequester,
        string? UndoRequesterSide,
        List<MoveRecord>? MoveHistory);

    private sealed record MoveRecord(
        int Row,
        int Column,
        string Side,
        string PreviousNextTurn,
        string? PreviousWinner,
        int PreviousScoreX,
        int PreviousScoreO,
        DateTimeOffset PreviousTurnStartedAt,
        DateTimeOffset? PreviousTurnFinishedAt,
        int? PreviousLastRow,
        int? PreviousLastColumn);

    public async Task CreateRoom()
    {
        var code = NewRoomCode();
        var token = NewPlayerToken();
        var room = new GameRoom(Context.ConnectionId, token);
        if (!Rooms.TryAdd(code, room))
        {
            await Clients.Caller.SendAsync("ErrorMessage", "Không tạo được phòng, vui lòng thử lại.");
            return;
        }

        PlayerBindings[token] = new PlayerBinding(code, "X");
        Context.Items["room"] = code;
        Context.Items["side"] = "X";
        await Groups.AddToGroupAsync(Context.ConnectionId, code);
        await Clients.Caller.SendAsync("RoomCreated", code, "X", token);
        await SendState(code, room);
    }

    public async Task StartBotGame(string difficulty)
    {
        if (!BotService.IsValidDifficulty(difficulty))
        {
            await Clients.Caller.SendAsync("ErrorMessage", "Mức độ bot không hợp lệ.");
            return;
        }

        var code = NewRoomCode();
        var token = NewPlayerToken();
        var room = new GameRoom(Context.ConnectionId, token, isBotGame: true, botDifficulty: difficulty);
        if (!Rooms.TryAdd(code, room))
        {
            await Clients.Caller.SendAsync("ErrorMessage", "Không tạo được ván chơi, vui lòng thử lại.");
            return;
        }

        PlayerBindings[token] = new PlayerBinding(code, "X");
        Context.Items["room"] = code;
        Context.Items["side"] = "X";
        await Groups.AddToGroupAsync(Context.ConnectionId, code);
        await Clients.Caller.SendAsync("BotGameStarted", code, "X", difficulty, token);
        await SendState(code, room);
    }

    public async Task SetBotDifficulty(string difficulty)
    {
        if (!BotService.IsValidDifficulty(difficulty))
        {
            await Clients.Caller.SendAsync("ErrorMessage", "Mức độ bot không hợp lệ.");
            return;
        }

        if (!TryGetRoom(out var code, out var room, out var side) || side != "X")
        {
            await Clients.Caller.SendAsync("ErrorMessage", "Chỉ người chơi có thể đổi độ khó trong ván với máy.");
            return;
        }

        string? error = null;
        lock (room.Sync)
        {
            if (!room.IsBotGame)
                error = "Độ khó chỉ có thể đổi trong ván với máy.";
            else
                room.BotDifficulty = difficulty;
        }

        if (error is not null)
        {
            await Clients.Caller.SendAsync("ErrorMessage", error);
            return;
        }

        await Clients.Caller.SendAsync("BotDifficultyChanged", code, difficulty);
    }

    public async Task JoinRoom(string roomCode)
    {
        var code = (roomCode ?? "").Trim().ToUpperInvariant();
        if (code.Length != 6 || code.Any(character => !RoomCodeAlphabet.Contains(character)))
        {
            await Clients.Caller.SendAsync("ErrorMessage", "Mã phòng không hợp lệ.");
            return;
        }

        if (!Rooms.TryGetValue(code, out var room))
        {
            await Clients.Caller.SendAsync("ErrorMessage", "Không tìm thấy phòng. Hãy kiểm tra mã phòng.");
            return;
        }

        var token = NewPlayerToken();
        string? joinError;
        lock (room.Sync)
        {
            if (room.HostConnectionId is null)
                joinError = "Chủ phòng đang kết nối lại. Vui lòng thử lại sau.";
            else if (room.IsBotGame || room.GuestToken is not null)
                joinError = "Phòng đã có đủ người chơi.";
            else
            {
                room.GuestConnectionId = Context.ConnectionId;
                room.GuestToken = token;
                room.TurnStartedAt = DateTimeOffset.UtcNow;
                joinError = null;
            }
        }

        if (joinError is not null)
        {
            await Clients.Caller.SendAsync("ErrorMessage", joinError);
            return;
        }

        PlayerBindings[token] = new PlayerBinding(code, "O");
        Context.Items["room"] = code;
        Context.Items["side"] = "O";
        await Groups.AddToGroupAsync(Context.ConnectionId, code);
        await Clients.Caller.SendAsync("JoinedRoom", code, "O", token);
        await SendState(code, room);
    }

    public async Task Rejoin(string playerToken)
    {
        if (string.IsNullOrWhiteSpace(playerToken) ||
            !PlayerBindings.TryGetValue(playerToken, out var binding) ||
            !Rooms.TryGetValue(binding.RoomCode, out var room))
        {
            await Clients.Caller.SendAsync("RejoinFailed", "Phiên chơi không còn hiệu lực. Hãy tạo hoặc tham gia phòng mới.");
            return;
        }

        RoomSnapshot? snapshot = null;
        string? error = null;
        string? previousConnectionId = null;
        lock (room.Sync)
        {
            if (room.GetPlayerToken(binding.Side) != playerToken)
                error = "Mã khôi phục không hợp lệ.";
            else if (room.GetConnectionId(binding.Side) is null &&
                room.GetDisconnectedAt(binding.Side) is null)
                error = "Phòng không ở trạng thái chờ kết nối lại.";
            else if (room.GetDisconnectedAt(binding.Side) is { } disconnectedAt &&
                DateTimeOffset.UtcNow - disconnectedAt > ReconnectTimeout)
                error = "Đã hết thời gian khôi phục phòng.";
            else
            {
                previousConnectionId = room.GetConnectionId(binding.Side);
                room.CancelDisconnectTimeout(binding.Side);
                room.SetConnectionId(binding.Side, Context.ConnectionId);
                snapshot = room.Snapshot(binding.RoomCode);
            }
        }

        if (error is not null || snapshot is null)
        {
            await Clients.Caller.SendAsync("RejoinFailed", error ?? "Không thể khôi phục phòng.");
            return;
        }

        Context.Items["room"] = binding.RoomCode;
        Context.Items["side"] = binding.Side;
        await Groups.AddToGroupAsync(Context.ConnectionId, binding.RoomCode);
        if (previousConnectionId is not null && previousConnectionId != Context.ConnectionId)
        {
            await Groups.RemoveFromGroupAsync(previousConnectionId, binding.RoomCode);
            await Clients.Client(previousConnectionId).SendAsync("SessionReplaced");
        }
        await Clients.Caller.SendAsync(
            "RoomRejoined", binding.RoomCode, binding.Side, room.IsBotGame, room.BotDifficulty, playerToken);
        await Clients.Group(binding.RoomCode).SendAsync("PlayerRejoined", binding.Side);
        await Clients.Group(binding.RoomCode).SendAsync("GameState", snapshot);
    }

    public async Task MakeMove(int row, int column)
    {
        if (!TryGetRoom(out var code, out var room, out var side))
            return;

        RoomSnapshot? snapshot;
        var botTurn = false;
        lock (room.Sync)
        {
            if (row < 0 || row >= BoardSize || column < 0 || column >= BoardSize ||
                !room.HasOpponent || !room.ArePlayersConnected || room.Winner is not null ||
                room.RestartRequester is not null || room.UndoRequester is not null ||
                room.Board[row, column] != '\0' || room.NextTurn != side)
            {
                snapshot = null;
            }
            else
            {
                room.RecordMove(row, column, side);
                room.Board[row, column] = side[0];
                room.LastRow = row;
                room.LastColumn = column;
                ApplyMoveResult(room, row, column, side);
                botTurn = room.IsBotGame && room.Winner is null && room.NextTurn == "O";
                snapshot = room.Snapshot(code);
            }
        }

        if (snapshot is null)
        {
            await Clients.Caller.SendAsync(
                "MoveRejected", row, column, "Nước đi không hợp lệ hoặc chưa đến lượt.");
            return;
        }

        await Clients.Group(code).SendAsync("GameState", snapshot);
        if (botTurn)
            await MakeBotMove(code, room);
    }

    public async Task Forfeit()
    {
        if (!TryGetRoom(out var code, out var room, out var side))
            return;

        RoomSnapshot? snapshot;
        lock (room.Sync)
        {
            if (!room.HasOpponent || !room.ArePlayersConnected || room.Winner is not null)
                snapshot = null;
            else if (room.RestartRequester is not null || room.UndoRequester is not null)
                snapshot = null;
            else
            {
                var winner = side == "X" ? "O" : "X";
                room.Winner = winner;
                room.AddPoint(winner);
                room.TurnFinishedAt = DateTimeOffset.UtcNow;
                snapshot = room.Snapshot(code);
            }
        }

        if (snapshot is null)
        {
            await Clients.Caller.SendAsync(
                "ErrorMessage",
                "Không thể xin thua khi đối thủ mất kết nối, ván đã kết thúc hoặc đang chờ xác nhận yêu cầu.");
            return;
        }

        await Clients.Group(code).SendAsync("GameState", snapshot);
    }

    public async Task SendChat(string message)
    {
        if (!TryGetRoom(out var code, out _, out var side))
            return;

        var text = (message ?? "").Trim();
        if (text.Length == 0)
            return;
        if (text.Length > ChatLimit)
            text = text[..ChatLimit];

        await Clients.Group(code).SendAsync("ChatMessage", side, text);
    }

    public async Task LeaveRoom()
    {
        if (!Context.Items.TryGetValue("room", out var value) || value is not string code)
            return;

        string? otherPlayer = null;
        var hostLeft = false;
        var hadPendingRequest = false;
        RoomSnapshot? snapshot = null;
        string? playerToken = null;
        if (Rooms.TryGetValue(code, out var room))
        {
            lock (room.Sync)
            {
                if (room.HostConnectionId == Context.ConnectionId)
                {
                    Rooms.TryRemove(code, out _);
                    hostLeft = true;
                    otherPlayer = room.GuestConnectionId;
                    playerToken = room.HostToken;
                    room.CancelDisconnectTimeout("X");
                    room.CancelDisconnectTimeout("O");
                    if (room.GuestToken is not null)
                        PlayerBindings.TryRemove(room.GuestToken, out _);
                }
                else if (room.GuestConnectionId == Context.ConnectionId)
                {
                    hadPendingRequest = room.RestartRequester is not null || room.UndoRequester is not null;
                    room.GuestConnectionId = null;
                    playerToken = room.GuestToken;
                    room.GuestToken = null;
                    room.CancelDisconnectTimeout("O");
                    room.RestartRequester = null;
                    room.RestartRequesterSide = null;
                    room.UndoRequester = null;
                    room.UndoRequesterSide = null;
                    otherPlayer = room.HostConnectionId;
                    snapshot = room.Snapshot(code);
                }
            }
        }

        if (playerToken is not null)
            PlayerBindings.TryRemove(playerToken, out _);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, code);
        Context.Items.Remove("room");
        Context.Items.Remove("side");

        if (otherPlayer is not null)
        {
            if (hadPendingRequest)
                await Clients.Client(otherPlayer).SendAsync("RequestCancelled");
            await Clients.Client(otherPlayer).SendAsync(hostLeft ? "RoomClosed" : "OpponentDisconnected");
            if (!hostLeft && snapshot is not null)
                await Clients.Client(otherPlayer).SendAsync("GameState", snapshot);
        }
    }

    public async Task RequestRestart()
    {
        if (!TryGetRoom(out var code, out var room, out var side))
            return;

        string? recipient = null;
        RoomSnapshot? snapshot = null;
        string? error = null;
        lock (room.Sync)
        {
            if (!room.HasOpponent || !room.ArePlayersConnected)
                error = "Cần có đủ hai người chơi đang kết nối để chơi lại.";
            else if (room.IsBotGame)
            {
                room.StartNextRound();
                room.ResetBoard();
                snapshot = room.Snapshot(code);
            }
            else if (room.RestartRequester is not null || room.UndoRequester is not null)
                error = "Đang chờ đối phương phản hồi yêu cầu hiện tại.";
            else
            {
                room.RestartRequester = Context.ConnectionId;
                room.RestartRequesterSide = side;
                recipient = side == "X" ? room.GuestConnectionId : room.HostConnectionId;
                snapshot = room.Snapshot(code);
            }
        }

        if (error is not null)
        {
            await Clients.Caller.SendAsync("ErrorMessage", error);
            return;
        }
        if (snapshot is not null && room.IsBotGame)
        {
            await Clients.Group(code).SendAsync("GameState", snapshot);
            return;
        }

        if (recipient is not null)
            await Clients.Client(recipient).SendAsync("RestartRequested", side);
        await Clients.Group(code).SendAsync("SystemMessage", "Đã gửi yêu cầu chơi lại.");
        if (snapshot is not null)
            await Clients.Group(code).SendAsync("GameState", snapshot);
    }

    public async Task RespondRestart(bool accept)
    {
        if (!TryGetRoom(out var code, out var room, out var side))
            return;

        RoomSnapshot? snapshot = null;
        string? requesterSide = null;
        string? error = null;
        lock (room.Sync)
        {
            if (room.RestartRequester is null)
                error = "Không có yêu cầu chơi lại cần phản hồi.";
            else if (room.RestartRequesterSide == side)
                error = "Bạn không thể phản hồi yêu cầu của chính mình.";
            else
            {
                requesterSide = room.RestartRequesterSide;
                room.RestartRequester = null;
                room.RestartRequesterSide = null;
                if (accept)
                {
                    room.StartNextRound();
                    room.ResetBoard();
                }
                snapshot = room.Snapshot(code);
            }
        }

        if (error is not null)
        {
            await Clients.Caller.SendAsync("ErrorMessage", error);
            return;
        }

        if (accept && snapshot is not null)
        {
            await Clients.Group(code).SendAsync("RequestAccepted", "restart");
            await Clients.Group(code).SendAsync("GameState", snapshot);
        }
        else if (requesterSide is not null)
        {
            var requesterConnection = room.GetConnectionId(requesterSide);
            if (requesterConnection is not null)
                await Clients.Client(requesterConnection).SendAsync("RestartDeclined");
            if (snapshot is not null)
                await Clients.Group(code).SendAsync("GameState", snapshot);
        }
    }

    public async Task RequestUndo()
    {
        if (!TryGetRoom(out var code, out var room, out var side))
            return;

        string? recipient = null;
        RoomSnapshot? pendingSnapshot = null;
        string? error = null;
        lock (room.Sync)
        {
            if (room.IsBotGame)
                error = "Hoàn tác theo cơ chế đồng thuận chỉ áp dụng cho ván chơi hai người.";
            else if (!room.HasOpponent || !room.ArePlayersConnected)
                error = "Cần có đủ hai người chơi đang kết nối để xin hoàn tác.";
            else if (!room.CanUndo)
                error = "Chưa có nước đi nào để hoàn tác.";
            else if (room.RestartRequester is not null || room.UndoRequester is not null)
                error = "Đang chờ phản hồi một yêu cầu hiện tại.";
            else
            {
                room.UndoRequester = Context.ConnectionId;
                room.UndoRequesterSide = side;
                recipient = side == "X" ? room.GuestConnectionId : room.HostConnectionId;
                pendingSnapshot = room.Snapshot(code);
            }
        }

        if (error is not null)
        {
            await Clients.Caller.SendAsync("ErrorMessage", error);
            return;
        }

        if (recipient is not null)
            await Clients.Client(recipient).SendAsync("UndoRequested", side);
        await Clients.Group(code).SendAsync("SystemMessage", "Đã gửi yêu cầu hoàn tác, đang chờ đối phương xác nhận.");
        if (pendingSnapshot is not null)
            await Clients.Group(code).SendAsync("GameState", pendingSnapshot);
    }

    public async Task RespondUndo(bool accept)
    {
        if (!TryGetRoom(out var code, out var room, out var side))
            return;

        RoomSnapshot? snapshot = null;
        string? requesterSide = null;
        string? error = null;
        lock (room.Sync)
        {
            if (room.UndoRequester is null)
                error = "Không có yêu cầu hoàn tác cần phản hồi.";
            else if (room.UndoRequesterSide == side)
                error = "Bạn không thể phản hồi yêu cầu của chính mình.";
            else
            {
                requesterSide = room.UndoRequesterSide;
                room.UndoRequester = null;
                room.UndoRequesterSide = null;
                if (accept)
                {
                    if (!room.UndoLastMove())
                        error = "Không còn nước đi nào để hoàn tác.";
                }
                if (error is null)
                    snapshot = room.Snapshot(code);
            }
        }

        if (error is not null)
        {
            await Clients.Caller.SendAsync("ErrorMessage", error);
            return;
        }

        if (accept && snapshot is not null)
        {
            await Clients.Group(code).SendAsync("RequestAccepted", "undo");
            await Clients.Group(code).SendAsync("GameState", snapshot);
        }
        else if (requesterSide is not null)
        {
            var requesterConnection = room.GetConnectionId(requesterSide);
            if (requesterConnection is not null)
                await Clients.Client(requesterConnection).SendAsync("UndoDeclined");
            if (snapshot is not null)
                await Clients.Group(code).SendAsync("GameState", snapshot);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("room", out var value) && value is string code &&
            Rooms.TryGetValue(code, out var room))
        {
            string? side = null;
            CancellationTokenSource? timeout = null;
            RoomSnapshot? snapshot = null;
            var hadPendingRequest = false;
            lock (room.Sync)
            {
                if (room.HostConnectionId == Context.ConnectionId)
                {
                    room.HostConnectionId = null;
                    side = "X";
                    timeout = room.StartDisconnectTimeout(side);
                    room.MarkDisconnected(side);
                    snapshot = room.Snapshot(code);
                }
                else if (room.GuestConnectionId == Context.ConnectionId)
                {
                    room.GuestConnectionId = null;
                    side = "O";
                    timeout = room.StartDisconnectTimeout(side);
                    room.MarkDisconnected(side);
                    snapshot = room.Snapshot(code);
                }

                if (side is not null)
                {
                    hadPendingRequest = room.RestartRequester is not null || room.UndoRequester is not null;
                    room.RestartRequester = null;
                    room.RestartRequesterSide = null;
                    room.UndoRequester = null;
                    room.UndoRequesterSide = null;
                    snapshot = room.Snapshot(code);
                }
            }

            if (side is not null && timeout is not null && snapshot is not null)
            {
                if (hadPendingRequest)
                    await Clients.Group(code).SendAsync("RequestCancelled");
                await Clients.Group(code).SendAsync("PlayerDisconnected", side, (int)ReconnectTimeout.TotalSeconds);
                await Clients.Group(code).SendAsync("GameState", snapshot);
                _ = ExpireDisconnectedPlayerAsync(code, side, room.GetDisconnectGeneration(side)!, timeout);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private bool TryGetRoom(out string code, out GameRoom room, out string side)
    {
        code = Context.Items["room"] as string ?? "";
        side = Context.Items["side"] as string ?? "";
        if (code.Length > 0 && side.Length > 0 &&
            Rooms.TryGetValue(code, out room!) &&
            room.GetConnectionId(side) == Context.ConnectionId)
            return true;

        room = null!;
        return false;
    }

    private Task SendState(string code, GameRoom room)
    {
        RoomSnapshot snapshot;
        lock (room.Sync)
            snapshot = room.Snapshot(code);
        return Clients.Group(code).SendAsync("GameState", snapshot);
    }

    private async Task MakeBotMove(string code, GameRoom room)
    {
        await Task.Delay(RandomNumberGenerator.GetInt32(400, 501));

        RoomSnapshot? snapshot = null;
        lock (room.Sync)
        {
            if (Rooms.TryGetValue(code, out var currentRoom) &&
                ReferenceEquals(currentRoom, room) &&
                room.IsBotGame && room.Winner is null && room.NextTurn == "O")
            {
                var (row, column) = BotService.ChooseMove(room.Board, room.BotDifficulty);
                room.RecordMove(row, column, "O");
                room.Board[row, column] = 'O';
                room.LastRow = row;
                room.LastColumn = column;
                ApplyMoveResult(room, row, column, "O");
                snapshot = room.Snapshot(code);
            }
        }

        if (snapshot is not null)
            await Clients.Group(code).SendAsync("GameState", snapshot);
    }

    private static void ApplyMoveResult(GameRoom room, int row, int column, string side)
    {
        if (CaroRules.HasWon(room.Board, row, column, side[0]))
        {
            room.Winner = side;
            room.AddPoint(side);
            room.TurnFinishedAt = DateTimeOffset.UtcNow;
        }
        else if (CaroRules.IsBoardFull(room.Board))
        {
            room.Winner = "draw";
            room.TurnFinishedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            room.NextTurn = side == "X" ? "O" : "X";
            room.TurnStartedAt = DateTimeOffset.UtcNow;
        }
    }

    private static string NewRoomCode()
    {
        return string.Create(6, RoomCodeAlphabet, static (chars, symbols) =>
        {
            for (var i = 0; i < chars.Length; i++)
                chars[i] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];
        });
    }

    private static string NewPlayerToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private async Task ExpireDisconnectedPlayerAsync(
        string code,
        string side,
        string generation,
        CancellationTokenSource timeout)
    {
        try
        {
            await Task.Delay(ReconnectTimeout, timeout.Token);

            async Task ExpireAsync()
            {
                string? otherPlayer = null;
                var hostExpired = side == "X";
                RoomSnapshot? snapshot = null;
                string? expiredToken = null;
                if (!Rooms.TryGetValue(code, out var room))
                    return;

                lock (room.Sync)
                {
                    if (room.GetDisconnectGeneration(side) != generation ||
                        room.GetConnectionId(side) is not null)
                        return;

                    expiredToken = room.GetPlayerToken(side);
                    room.CancelDisconnectTimeout(side);
                    if (hostExpired)
                    {
                        Rooms.TryRemove(code, out _);
                        otherPlayer = room.GuestConnectionId;
                        if (room.GuestToken is not null)
                            PlayerBindings.TryRemove(room.GuestToken, out _);
                    }
                    else
                    {
                        room.GuestToken = null;
                        room.RestartRequester = null;
                        room.RestartRequesterSide = null;
                        otherPlayer = room.HostConnectionId;
                        snapshot = room.Snapshot(code);
                    }
                }

                if (expiredToken is not null)
                    PlayerBindings.TryRemove(expiredToken, out _);

                if (otherPlayer is not null)
                {
                    await Clients.Client(otherPlayer).SendAsync(
                        hostExpired ? "RoomClosed" : "OpponentDisconnected");
                    if (!hostExpired && snapshot is not null)
                        await Clients.Client(otherPlayer).SendAsync("GameState", snapshot);
                }
            }

            if (_stateCoordinator is null)
                await ExpireAsync();
            else
                await _stateCoordinator.ExecuteAsync(ExpireAsync);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to expire disconnected player in room {RoomCode}.", code);
        }
        finally
        {
            timeout.Dispose();
        }
    }

    private sealed class GameRoom(
        string? hostConnectionId,
        string hostToken,
        bool isBotGame = false,
        string botDifficulty = "medium")
    {
        public object Sync { get; } = new();
        public string? HostConnectionId { get; set; } = hostConnectionId;
        public string HostToken { get; } = hostToken;
        public string? GuestToken { get; set; }
        public bool IsBotGame { get; } = isBotGame;
        public string BotDifficulty { get; set; } = botDifficulty;
        public bool HasOpponent => IsBotGame || GuestToken is not null;
        public bool ArePlayersConnected =>
            HostConnectionId is not null && (IsBotGame || GuestConnectionId is not null);
        public string? GuestConnectionId { get; set; }
        public char[,] Board { get; private set; } = new char[BoardSize, BoardSize];
        public string? Winner { get; set; }
        public string NextTurn { get; set; } = "X";
        public int Round { get; private set; } = 1;
        public int ScoreX { get; private set; }
        public int ScoreO { get; private set; }
        public DateTimeOffset TurnStartedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? TurnFinishedAt { get; set; }
        public int? LastRow { get; set; }
        public int? LastColumn { get; set; }
        public string? RestartRequester { get; set; }
        public string? RestartRequesterSide { get; set; }
        public string? UndoRequester { get; set; }
        public string? UndoRequesterSide { get; set; }
        public List<MoveRecord> MoveHistory { get; } = [];
        public bool CanUndo => MoveHistory.Count > 0;
        public DateTimeOffset? HostDisconnectedAt { get; private set; }
        public DateTimeOffset? GuestDisconnectedAt { get; private set; }
        public string? HostDisconnectGeneration { get; private set; }
        public string? GuestDisconnectGeneration { get; private set; }
        private CancellationTokenSource? HostDisconnectTimeout { get; set; }
        private CancellationTokenSource? GuestDisconnectTimeout { get; set; }

        public string? GetPlayerToken(string side) =>
            side == "X" ? HostToken : GuestToken;

        public string? GetConnectionId(string side) =>
            side == "X" ? HostConnectionId : GuestConnectionId;

        public void SetConnectionId(string side, string connectionId)
        {
            if (side == "X")
            {
                HostConnectionId = connectionId;
                HostDisconnectedAt = null;
                HostDisconnectGeneration = null;
            }
            else
            {
                GuestConnectionId = connectionId;
                GuestDisconnectedAt = null;
                GuestDisconnectGeneration = null;
            }
        }

        public DateTimeOffset? GetDisconnectedAt(string side) =>
            side == "X" ? HostDisconnectedAt : GuestDisconnectedAt;

        public string? GetDisconnectGeneration(string side) =>
            side == "X" ? HostDisconnectGeneration : GuestDisconnectGeneration;

        public void MarkDisconnected(string side)
        {
            if (side == "X")
            {
                HostDisconnectedAt = DateTimeOffset.UtcNow;
                HostDisconnectGeneration = Guid.NewGuid().ToString("N");
            }
            else
            {
                GuestDisconnectedAt = DateTimeOffset.UtcNow;
                GuestDisconnectGeneration = Guid.NewGuid().ToString("N");
            }
        }

        public CancellationTokenSource? GetDisconnectTimeout(string side) =>
            side == "X" ? HostDisconnectTimeout : GuestDisconnectTimeout;

        public CancellationTokenSource StartDisconnectTimeout(string side)
        {
            CancelDisconnectTimeout(side);
            var timeout = new CancellationTokenSource();
            if (side == "X")
                HostDisconnectTimeout = timeout;
            else
                GuestDisconnectTimeout = timeout;
            return timeout;
        }

        public void CancelDisconnectTimeout(string side)
        {
            var timeout = GetDisconnectTimeout(side);
            if (side == "X")
                HostDisconnectTimeout = null;
            else
                GuestDisconnectTimeout = null;
            timeout?.Cancel();
        }

        public void AddPoint(string side)
        {
            if (side == "X")
                ScoreX++;
            else
                ScoreO++;
        }

        public void StartNextRound()
        {
            if (Round >= 3)
            {
                Round = 1;
                ScoreX = 0;
                ScoreO = 0;
            }
            else
            {
                Round++;
            }
        }

        public void ResetBoard()
        {
            Board = new char[BoardSize, BoardSize];
            MoveHistory.Clear();
            Winner = null;
            LastRow = null;
            LastColumn = null;
            NextTurn = IsBotGame || Round % 2 != 0 ? "X" : "O";
            TurnStartedAt = DateTimeOffset.UtcNow;
            TurnFinishedAt = null;
            RestartRequester = null;
            RestartRequesterSide = null;
            UndoRequester = null;
            UndoRequesterSide = null;
        }

        public void RecordMove(int row, int column, string side) =>
            MoveHistory.Add(new MoveRecord(
                row, column, side, NextTurn, Winner, ScoreX, ScoreO,
                TurnStartedAt, TurnFinishedAt, LastRow, LastColumn));

        public bool UndoLastMove()
        {
            if (MoveHistory.Count == 0)
                return false;

            var move = MoveHistory[^1];
            if (Board[move.Row, move.Column] != move.Side[0])
                return false;

            MoveHistory.RemoveAt(MoveHistory.Count - 1);
            Board[move.Row, move.Column] = '\0';
            NextTurn = move.PreviousNextTurn;
            Winner = move.PreviousWinner;
            ScoreX = move.PreviousScoreX;
            ScoreO = move.PreviousScoreO;
            TurnStartedAt = move.PreviousTurnStartedAt;
            TurnFinishedAt = move.PreviousTurnFinishedAt;
            LastRow = move.PreviousLastRow;
            LastColumn = move.PreviousLastColumn;
            return true;
        }

        public RoomSnapshot Snapshot(string code)
        {
            var rows = new string[BoardSize];
            for (var row = 0; row < BoardSize; row++)
            {
                var cells = new char[BoardSize];
                for (var column = 0; column < BoardSize; column++)
                    cells[column] = Board[row, column] == '\0' ? '.' : Board[row, column];
                rows[row] = new string(cells);
            }

            return new RoomSnapshot(code, rows, NextTurn, Winner, Round,
                HasOpponent, ScoreX, ScoreO, TurnStartedAt,
                TurnFinishedAt, LastRow, LastColumn, IsBotGame,
                HostConnectionId is not null, IsBotGame || GuestConnectionId is not null,
                CanUndo, RestartRequester is not null || UndoRequester is not null);
        }

        public PersistedGameRoom ToPersisted()
        {
            var rows = new string[BoardSize];
            for (var row = 0; row < BoardSize; row++)
            {
                var cells = new char[BoardSize];
                for (var column = 0; column < BoardSize; column++)
                    cells[column] = Board[row, column];
                rows[row] = new string(cells);
            }

            return new PersistedGameRoom(
                HostConnectionId, HostToken, GuestToken, IsBotGame, BotDifficulty,
                GuestConnectionId, rows, Winner, NextTurn, Round, ScoreX, ScoreO,
                TurnStartedAt, TurnFinishedAt, LastRow, LastColumn, RestartRequester,
                RestartRequesterSide, HostDisconnectedAt, GuestDisconnectedAt,
                HostDisconnectGeneration, GuestDisconnectGeneration,
                UndoRequester, UndoRequesterSide, MoveHistory.ToList());
        }

        public static GameRoom Restore(PersistedGameRoom persisted)
        {
            var room = new GameRoom(
                persisted.HostConnectionId, persisted.HostToken,
                persisted.IsBotGame, persisted.BotDifficulty);
            room.ApplyPersisted(persisted);
            return room;
        }

        public void ApplyPersisted(PersistedGameRoom persisted)
        {
            HostConnectionId = persisted.HostConnectionId;
            GuestToken = persisted.GuestToken;
            GuestConnectionId = persisted.GuestConnectionId;
            Board = new char[BoardSize, BoardSize];
            for (var row = 0; row < BoardSize; row++)
                for (var column = 0; column < BoardSize; column++)
                    Board[row, column] = persisted.Board[row][column];
            Winner = persisted.Winner;
            NextTurn = persisted.NextTurn;
            Round = persisted.Round;
            ScoreX = persisted.ScoreX;
            ScoreO = persisted.ScoreO;
            TurnStartedAt = persisted.TurnStartedAt;
            TurnFinishedAt = persisted.TurnFinishedAt;
            LastRow = persisted.LastRow;
            LastColumn = persisted.LastColumn;
            RestartRequester = persisted.RestartRequester;
            RestartRequesterSide = persisted.RestartRequesterSide;
            UndoRequester = persisted.UndoRequester;
            UndoRequesterSide = persisted.UndoRequesterSide;
            MoveHistory.Clear();
            MoveHistory.AddRange(persisted.MoveHistory ?? []);
            HostDisconnectedAt = persisted.HostDisconnectedAt;
            GuestDisconnectedAt = persisted.GuestDisconnectedAt;
            HostDisconnectGeneration = persisted.HostDisconnectGeneration;
            GuestDisconnectGeneration = persisted.GuestDisconnectGeneration;
        }
    }

    public sealed record RoomSnapshot(
        string RoomCode,
        string[] Board,
        string NextTurn,
        string? Winner,
        int Round,
        bool HasOpponent,
        int ScoreX,
        int ScoreO,
        DateTimeOffset TurnStartedAt,
        DateTimeOffset? TurnFinishedAt,
        int? LastRow,
        int? LastColumn,
        bool IsBotGame,
        bool HostConnected,
        bool GuestConnected,
        bool CanUndo,
        bool HasPendingRequest);

    private sealed record PlayerBinding(string RoomCode, string Side);
}

internal static class CaroRules
{
    public const int BoardSize = 15;

    public static bool IsBoardFull(char[,] board)
    {
        foreach (var cell in board)
            if (cell == '\0')
                return false;
        return true;
    }

    public static bool HasWon(char[,] board, int row, int column, char mark)
    {
        int[][] directions = [[0, 1], [1, 0], [1, 1], [1, -1]];
        foreach (var direction in directions)
        {
            var before = Count(board, row, column, -direction[0], -direction[1], mark);
            var after = Count(board, row, column, direction[0], direction[1], mark);
            if (before + after + 1 < 5)
                continue;

            var blockedBefore = IsOpponent(board,
                row - (before + 1) * direction[0],
                column - (before + 1) * direction[1], mark);
            var blockedAfter = IsOpponent(board,
                row + (after + 1) * direction[0],
                column + (after + 1) * direction[1], mark);
            if (!(blockedBefore && blockedAfter))
                return true;
        }
        return false;
    }

    private static int Count(char[,] board, int row, int column, int dRow, int dColumn, char mark)
    {
        var count = 0;
        var r = row + dRow;
        var c = column + dColumn;
        while (r >= 0 && r < BoardSize && c >= 0 && c < BoardSize && board[r, c] == mark)
        {
            count++;
            r += dRow;
            c += dColumn;
        }
        return count;
    }

    private static bool IsOpponent(char[,] board, int row, int column, char mark) =>
        row >= 0 && row < BoardSize && column >= 0 && column < BoardSize &&
        board[row, column] != '\0' && board[row, column] != mark;
}
