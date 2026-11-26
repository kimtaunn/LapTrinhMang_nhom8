using System.Security.Cryptography;

public static class BotService
{
    private static readonly (int Row, int Column)[] Directions =
        [(0, 1), (1, 0), (1, 1), (1, -1)];

    public static bool IsValidDifficulty(string difficulty) =>
        difficulty is "easy" or "medium" or "hard";

    public static (int Row, int Column) ChooseMove(char[,] board, string difficulty)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (board.GetLength(0) != CaroRules.BoardSize ||
            board.GetLength(1) != CaroRules.BoardSize)
            throw new ArgumentException($"The board must be {CaroRules.BoardSize}x{CaroRules.BoardSize}.", nameof(board));
        if (!IsValidDifficulty(difficulty))
            throw new ArgumentException("Difficulty must be easy, medium, or hard.", nameof(difficulty));

        var candidates = GetCandidates(board);
        if (candidates.Count == 0)
            throw new InvalidOperationException("Bot không tìm thấy ô trống hợp lệ.");

        if (difficulty == "easy")
            return candidates[RandomNumberGenerator.GetInt32(candidates.Count)];

        var winningMoves = FindImmediateWins(board, candidates, 'O');
        if (winningMoves.Count > 0)
            return winningMoves[RandomNumberGenerator.GetInt32(winningMoves.Count)];

        var blockingMoves = FindImmediateWins(board, candidates, 'X');
        if (blockingMoves.Count > 0)
            return blockingMoves[RandomNumberGenerator.GetInt32(blockingMoves.Count)];

        var defenseWeight = difficulty == "hard" ? 1.2 : 1.0;
        var bestScore = double.NegativeInfinity;
        var bestMoves = new List<(int Row, int Column)>();
        foreach (var move in candidates)
        {
            board[move.Row, move.Column] = 'O';
            var attackScore = ScoreAt(board, move.Row, move.Column, 'O');
            var ownWinningContinuations = difficulty == "hard"
                ? FindImmediateWins(board, GetCandidates(board), 'O').Count
                : 0;
            var opponentWinningContinuations = difficulty == "hard"
                ? FindImmediateWins(board, GetCandidates(board), 'X').Count
                : 0;
            board[move.Row, move.Column] = 'X';
            var defenseScore = ScoreAt(board, move.Row, move.Column, 'X');
            board[move.Row, move.Column] = '\0';

            var score = attackScore + defenseWeight * defenseScore;
            if (difficulty == "hard")
            {
                if (ownWinningContinuations > 1)
                    score += 250_000 * (ownWinningContinuations - 1);
                score -= 500_000 * opponentWinningContinuations;
            }
            if (score > bestScore)
            {
                bestScore = score;
                bestMoves.Clear();
                bestMoves.Add(move);
            }
            else if (score == bestScore)
            {
                bestMoves.Add(move);
            }
        }

        return bestMoves[RandomNumberGenerator.GetInt32(bestMoves.Count)];
    }

    private static List<(int Row, int Column)> GetCandidates(char[,] board)
    {
        var candidates = new List<(int Row, int Column)>();
        var hasPieces = false;
        for (var row = 0; row < CaroRules.BoardSize; row++)
        {
            for (var column = 0; column < CaroRules.BoardSize; column++)
            {
                if (board[row, column] == '\0')
                    continue;

                hasPieces = true;
                for (var dRow = -2; dRow <= 2; dRow++)
                {
                    for (var dColumn = -2; dColumn <= 2; dColumn++)
                    {
                        var candidateRow = row + dRow;
                        var candidateColumn = column + dColumn;
                        if (candidateRow >= 0 && candidateRow < CaroRules.BoardSize &&
                            candidateColumn >= 0 && candidateColumn < CaroRules.BoardSize &&
                            board[candidateRow, candidateColumn] == '\0')
                            candidates.Add((candidateRow, candidateColumn));
                    }
                }
            }
        }

        if (!hasPieces)
            return [(CaroRules.BoardSize / 2, CaroRules.BoardSize / 2)];

        return candidates.Distinct().ToList();
    }

    private static List<(int Row, int Column)> FindImmediateWins(
        char[,] board,
        List<(int Row, int Column)> candidates,
        char mark)
    {
        var winningMoves = new List<(int Row, int Column)>();
        foreach (var move in candidates)
        {
            board[move.Row, move.Column] = mark;
            if (CaroRules.HasWon(board, move.Row, move.Column, mark))
                winningMoves.Add(move);
            board[move.Row, move.Column] = '\0';
        }
        return winningMoves;
    }

    private static int ScoreAt(char[,] board, int row, int column, char mark)
    {
        var score = 0;
        foreach (var (dRow, dColumn) in Directions)
        {
            var forward = Count(board, row, column, dRow, dColumn, mark);
            var backward = Count(board, row, column, -dRow, -dColumn, mark);
            var count = 1 + forward + backward;
            var openEnds = 0;
            if (IsOpen(board, row + (forward + 1) * dRow, column + (forward + 1) * dColumn))
                openEnds++;
            if (IsOpen(board, row - (backward + 1) * dRow, column - (backward + 1) * dColumn))
                openEnds++;

            score += count switch
            {
                >= 5 => 1_000_000,
                4 when openEnds == 2 => 100_000,
                4 when openEnds == 1 => 10_000,
                3 when openEnds == 2 => 5_000,
                3 when openEnds == 1 => 500,
                2 when openEnds == 2 => 250,
                2 when openEnds == 1 => 40,
                _ => 5
            };
        }
        return score;
    }

    private static int Count(char[,] board, int row, int column, int dRow, int dColumn, char mark)
    {
        var count = 0;
        var nextRow = row + dRow;
        var nextColumn = column + dColumn;
        while (nextRow >= 0 && nextRow < CaroRules.BoardSize &&
            nextColumn >= 0 && nextColumn < CaroRules.BoardSize &&
            board[nextRow, nextColumn] == mark)
        {
            count++;
            nextRow += dRow;
            nextColumn += dColumn;
        }
        return count;
    }

    private static bool IsOpen(char[,] board, int row, int column) =>
        row >= 0 && row < CaroRules.BoardSize &&
        column >= 0 && column < CaroRules.BoardSize &&
        board[row, column] == '\0';
}
