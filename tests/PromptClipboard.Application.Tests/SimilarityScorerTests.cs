namespace PromptClipboard.Application.Tests;

using PromptClipboard.Application.Services;

public class SimilarityScorerTests
{
    [Fact]
    public void IdenticalStrings_ScoreOne()
    {
        var score = SimilarityScorer.Score("Hello World", "Body text", "Hello World", "Body text");
        Assert.Equal(1.0, score, precision: 2);
    }

    [Fact]
    public void CompletelyDifferent_ScoreLow()
    {
        var score = SimilarityScorer.Score("Alpha", "First content", "Omega", "Second content");
        Assert.True(score < 0.5);
    }

    [Fact]
    public void SendEmail_VsUsers_HighScore()
    {
        var score = SimilarityScorer.Score("Send email to user", "", "Send email to users", "");
        Assert.True(score >= 0.70, $"Expected >= 0.70, got {score}");
    }

    [Fact]
    public void Dice_EmptyStrings_One()
    {
        Assert.Equal(1.0, SimilarityScorer.Dice("", ""));
    }

    [Fact]
    public void Dice_OneEmpty_Zero()
    {
        Assert.Equal(0.0, SimilarityScorer.Dice("hello", ""));
        Assert.Equal(0.0, SimilarityScorer.Dice("", "hello"));
    }

    [Fact]
    public void Dice_ShortStrings_ExactMatch()
    {
        Assert.Equal(1.0, SimilarityScorer.Dice("ab", "ab"));
        Assert.Equal(0.0, SimilarityScorer.Dice("ab", "cd"));
    }

    [Fact]
    public void Normalize_HandlesUnicode()
    {
        var result = SimilarityScorer.Normalize("  Hello\r\n  World  ");
        Assert.Equal("hello world", result);
    }
}
