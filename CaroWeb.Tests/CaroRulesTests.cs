using System.Reflection;
using Xunit;

public class CaroRulesTests
{
    private const int BoardSize = 15;

    [Fact]
    public void FiveInARowWithOpenEndsWins()
    {
        var board = MakeFiveInARow();

        Assert.True(HasWon(board));
    }

    [Fact]
    public void FiveInARowWithOneBlockedEndStillWins()
    {
        var board = MakeFiveInARow();
        board[7, 3] = 'O';

        Assert.True(HasWon(board));
    }

    [Fact]
    public void FiveInARowBlockedAtBothEndsDoesNotWin()
    {
        var board = MakeFiveInARow();
        board[7, 3] = 'O';
        board[7, 9] = 'O';

        Assert.False(HasWon(board));
    }

    private static char[,] MakeFiveInARow()
    {
        var board = new char[BoardSize, BoardSize];
        for (var column = 4; column <= 8; column++)
            board[7, column] = 'X';
        return board;
    }

    private static bool HasWon(char[,] board)
    {
        var rulesType = typeof(BotService).Assembly.GetType("CaroRules", throwOnError: true)!;
        var method = rulesType.GetMethod("HasWon", BindingFlags.Public | BindingFlags.Static)!;
        return (bool)method.Invoke(null, new object[] { board, 7, 6, 'X' })!;
    }
}
