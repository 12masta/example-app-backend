using System.Linq;
using System.Threading.Tasks;
using Conduit.Features.Articles;
using Xunit;

namespace Conduit.IntegrationTests.Features.Articles;

public class ReadingTimeTests : SliceFixture
{
    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("one two three", 1)]
    public void MinutesFromBody_short_or_empty_is_at_least_one(string? body, int expected)
    {
        Assert.Equal(expected, ReadingTime.MinutesFromBody(body));
    }

    [Fact]
    public void MinutesFromBody_uses_200_words_per_minute()
    {
        var body = string.Join(' ', Enumerable.Repeat("word", 400));
        Assert.Equal(2, ReadingTime.MinutesFromBody(body));
    }

    [Fact]
    public void MinutesFromBody_rounds_up()
    {
        var body = string.Join(' ', Enumerable.Repeat("word", 201));
        Assert.Equal(2, ReadingTime.MinutesFromBody(body));
    }

    [Fact]
    public async Task Expect_List_Includes_ReadingTimeMinutes_Without_Body()
    {
        var body = string.Join(' ', Enumerable.Repeat("word", 400));
        var command = new Create.Command(
            new Create.ArticleData
            {
                Title = "Reading time list article",
                Description = "Description",
                Body = body,
                TagList = ["readingtime"],
            }
        );
        await ArticleHelpers.CreateArticle(this, command);

        var result = await SendAsync(new List.Query("readingtime", null, null, null, null));
        var article = Assert.Single(result.Articles);
        Assert.Null(article.Body);
        Assert.Equal(2, article.ReadingTimeMinutes);
    }

    [Fact]
    public async Task Expect_Details_Includes_ReadingTimeMinutes()
    {
        var body = string.Join(' ', Enumerable.Repeat("word", 50));
        var command = new Create.Command(
            new Create.ArticleData
            {
                Title = "Reading time details article",
                Description = "Description",
                Body = body,
                TagList = ["readingtimedetails"],
            }
        );
        var created = await ArticleHelpers.CreateArticle(this, command);

        var result = await SendAsync(new Details.Query(created.Slug!));
        Assert.Equal(1, result.Article.ReadingTimeMinutes);
        Assert.Equal(body, result.Article.Body);
    }
}
