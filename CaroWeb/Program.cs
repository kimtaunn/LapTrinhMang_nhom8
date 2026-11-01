using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<GameHub>("/game");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

public sealed class GameHub : Hub
{
    private const int BoardSize = 15;
    private const int ChatLimit = 250;
    private static readonly ConcurrentDictionary<string, GameRoom> Rooms = new();

    public async Task CreateRoom()
    {
        var code = NewRoomCode();
        var room = new GameRoom(Context.ConnectionId);
        if (!Rooms.TryAdd(code, room))
        {
            await Clients.Caller.SendAsync("ErrorMessage", "Không tạo được phòng, vui lòng thử lại.");
            return;
        }

        Context.Items["room"] = code;
        Context.Items["side"] = "X";
        await Groups.AddToGroupAsync(Context.ConnectionId, code);
        await Clients.Caller.SendAsync("RoomCreated", code, "X");
        await SendState(code, room);
    }

    public async Task JoinRoom(string roomCode)
    {
        var code = (roomCode ?? "").Trim().ToUpperInvariant();
        if (!Rooms.TryGetValue(code, out var room))
        {
            await Clients.Caller.SendAsync("ErrorMessage", "Không tìm thấy phòng. Hãy kiểm tra mã phòng.");
            return;
        }

        string? joinError;
        lock (room.Sync)
        {
            if (room.GuestConnectionId is not null)
                joinError = "Phòng đã có đủ người chơi.";
            else
            {
                room.GuestConnectionId = Context.ConnectionId;
                room.TurnStartedAt = DateTimeOffset.UtcNow;
                joinError = null;
            }
        }

        if (joinError is not null)
        {
            await Clients.Caller.SendAsync("ErrorMessage", joinError);
            return;
        }

        Context.Items["room"] = code;
        Context.Items["side"] = "O";
        await Groups.AddToGroupAsync(Context.ConnectionId, code);
        await Clients.Caller.SendAsync("JoinedRoom", code, "O");
        await SendState(code, room);
    }

    public async Task MakeMove(int row, int column)
    {
        if (!TryGetRoom(out var code, out var room, out var side))
            return;

        RoomSnapshot? snapshot;
        lock (room.Sync)
        {
            if (row < 0 || row >= BoardSize || column < 0 || column >= BoardSize ||
                room.GuestConnectionId is null || room.Winner is not null ||
                room.Board[row, column] != '\0' || room.NextTurn != side)
            {
                snapshot = null;
            }
            else
            {
                room.Board[row, column] = side[0];
                room.LastRow = row;
                room.LastColumn = column;
                if (HasWon(room.Board, row, column, side[0]))
                {
                    room.Winner = side;
                    room.AddPoint(side);
                    room.TurnFinishedAt = DateTimeOffset.UtcNow;
                }
                else if (IsBoardFull(room.Board))
                {
                    room.Winner = "draw";
                    room.TurnFinishedAt = DateTimeOffset.UtcNow;
                }
                else
                {
                    room.NextTurn = side == "X" ? "O" : "X";
                    room.TurnStartedAt = DateTimeOffset.UtcNow;
                }

                snapshot = room.Snapshot(code);
            }
        }

        if (snapshot is null)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "Nước đi không hợp lệ hoặc chưa đến lượt.");
            return;
        }

        await Clients.Group(code).SendAsync("GameState", snapshot);
    }

    public async Task Forfeit()
    {
        if (!TryGetRoom(out var code, out var room, out var side))
            return;

        RoomSnapshot? snapshot;
        lock (room.Sync)
        {
            if (room.GuestConnectionId is null || room.Winner is not null)
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
            await Clients.Caller.SendAsync("ErrorMessage", "Không thể xin thua ở trạng thái hiện tại.");
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
        if (Context.Items.TryGetValue("room", out var value) && value is string code)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, code);
        Context.Items.Remove("room");
        Context.Items.Remove("side");
    }

    public async Task RequestRestart()
    {
        if (!TryGetRoom(out var code, out var room, out var side))
            return;

        string? recipient;
        RoomSnapshot? snapshot = null;
        string? error = null;
        lock (room.Sync)
        {
            if (room.GuestConnectionId is null)
                error = "Đang chờ người chơi thứ hai.";
            else if (room.Winner is not null)
            {
                room.StartNextRound();
                room.ResetBoard();
                snapshot = room.Snapshot(code);
            }
            else if (room.RestartRequester is not null)
                error = "Đang chờ phản hồi yêu cầu chơi lại.";
            else
            {
                room.RestartRequester = Context.ConnectionId;
                room.RestartRequesterSide = side;
            }

            recipient = side == "X" ? room.GuestConnectionId : room.HostConnectionId;
        }

        if (error is not null)
        {
            await Clients.Caller.SendAsync("ErrorMessage", error);
            return;
        }
        if (snapshot is not null)
        {
            await Clients.Group(code).SendAsync("GameState", snapshot);
            return;
        }

        if (recipient is not null)
            await Clients.Client(recipient).SendAsync("RestartRequested", side);
        await Clients.Group(code).SendAsync("SystemMessage", "Đã gửi yêu cầu chơi lại.");
    }

    public async Task RespondRestart(bool accept)
    {
        if (!TryGetRoom(out var code, out var room, out var side))
            return;

        RoomSnapshot? snapshot = null;
        string? requester = null;
        string? error = null;
        lock (room.Sync)
        {
            if (room.RestartRequester is null || room.RestartRequester == Context.ConnectionId)
                error = "Không có yêu cầu chơi lại cần phản hồi.";
            else if (room.RestartRequesterSide == side)
                error = "Bạn không thể phản hồi yêu cầu của chính mình.";
            else
            {
                requester = room.RestartRequester;
                room.RestartRequester = null;
                room.RestartRequesterSide = null;
                if (accept)
                {
                    room.StartNextRound();
                    room.ResetBoard();
                    snapshot = room.Snapshot(code);
                }
            }
        }

        if (error is not null)
        {
            await Clients.Caller.SendAsync("ErrorMessage", error);
            return;
        }

        if (accept && snapshot is not null)
            await Clients.Group(code).SendAsync("GameState", snapshot);
        else if (requester is not null)
            await Clients.Client(requester).SendAsync("RestartDeclined");
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("room", out var value) && value is string code &&
            Rooms.TryGetValue(code, out var room))
        {
            var removed = false;
            string? otherPlayer = null;
            lock (room.Sync)
            {
                if (room.HostConnectionId == Context.ConnectionId)
                {
                    Rooms.TryRemove(code, out _);
                    removed = true;
                    otherPlayer = room.GuestConnectionId;
                }
                else if (room.GuestConnectionId == Context.ConnectionId)
                {
                    room.GuestConnectionId = null;
                    room.RestartRequester = null;
                    room.RestartRequesterSide = null;
                    otherPlayer = room.HostConnectionId;
                }
            }

            if (otherPlayer is not null)
                await Clients.Client(otherPlayer).SendAsync(
                    removed ? "RoomClosed" : "OpponentDisconnected");
        }

        await base.OnDisconnectedAsync(exception);
    }

    private bool TryGetRoom(out string code, out GameRoom room, out string side)
    {
        code = Context.Items["room"] as string ?? "";
        side = Context.Items["side"] as string ?? "";
        if (code.Length > 0 && side.Length > 0 && Rooms.TryGetValue(code, out room!))
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

    private static string NewRoomCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(6, alphabet, static (chars, symbols) =>
        {
            for (var i = 0; i < chars.Length; i++)
                chars[i] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];
        });
    }

    private static bool IsBoardFull(char[,] board)
    {
        foreach (var cell in board)
            if (cell == '\0')
                return false;
        return true;
    }

    private static bool HasWon(char[,] board, int row, int column, char mark)
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

    private sealed class GameRoom(string hostConnectionId)
    {
        public object Sync { get; } = new();
        public string HostConnectionId { get; } = hostConnectionId;
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
            Winner = null;
            LastRow = null;
            LastColumn = null;
            NextTurn = Round % 2 == 0 ? "O" : "X";
            TurnStartedAt = DateTimeOffset.UtcNow;
            TurnFinishedAt = null;
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
                GuestConnectionId is not null, ScoreX, ScoreO, TurnStartedAt,
                TurnFinishedAt, LastRow, LastColumn);
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
        int? LastColumn);
}
