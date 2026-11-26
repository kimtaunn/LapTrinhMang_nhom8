using Xunit;

public class BotServiceTests
{
    private const int BoardSize = 15;

    [Fact]
    public void HardDifficultyTakesAnImmediateWinningMove()
    {
        var board = new char[BoardSize, BoardSize];
        board[7, 3] = 'X';
        for (var column = 4; column <= 7; column++)
            board[7, column] = 'O';

        var move = BotService.ChooseMove(board, "hard");

        Assert.Equal((7, 8), move);
    }

    [Fact]
    public void HardDifficultyBlocksAnImmediateOpponentWin()
    {
        var board = new char[BoardSize, BoardSize];
        board[7, 3] = 'O';
        for (var column = 4; column <= 7; column++)
            board[7, column] = 'X';

        var move = BotService.ChooseMove(board, "hard");

        Assert.Equal((7, 8), move);
    }

    [Fact]
    public void DifficultyAndEmptyBoardBasicsAreHandled()
    {
        var board = new char[BoardSize, BoardSize];

        Assert.True(BotService.IsValidDifficulty("easy"));
        Assert.True(BotService.IsValidDifficulty("medium"));
        Assert.True(BotService.IsValidDifficulty("hard"));
        Assert.False(BotService.IsValidDifficulty("expert"));
        Assert.Equal((7, 7), BotService.ChooseMove(board, "easy"));
        Assert.Equal((7, 7), BotService.ChooseMove(board, "medium"));
        Assert.Equal((7, 7), BotService.ChooseMove(board, "hard"));
    }

    [Fact]
    public void ChooseMoveRejectsInvalidDifficultyAndBoardSize()
    {
        Assert.Throws<ArgumentException>(() =>
            BotService.ChooseMove(new char[BoardSize, BoardSize], "expert"));
        Assert.Throws<ArgumentException>(() =>
            BotService.ChooseMove(new char[BoardSize - 1, BoardSize], "medium"));
    }
}
