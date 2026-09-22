using System;
using System.Net;
using System.Threading.Tasks;
using Conduit.Infrastructure.Errors;
using Conduit.IntegrationTests.Features.Users;
using Xunit;
using ArticlesCreate = Conduit.Features.Articles.Create;
using ArticlesDetails = Conduit.Features.Articles.Details;
using UsersCreate = Conduit.Features.Users.Create;

namespace Conduit.IntegrationTests.Features.Articles;

public class DraftTests : SliceFixture
{
    [Fact]
    public async Task Expect_Create_Article_As_Draft()
    {
        var command = new ArticlesCreate.Command(
            new ArticlesCreate.ArticleData
            {
                Title = "Draft article title",
                Description = "Description of the draft",
                Body = "Body of the draft",
                IsDraft = true,
            }
        );

        var article = await ArticleHelpers.CreateArticle(this, command);

        Assert.NotNull(article);
        Assert.True(article.IsDraft);
    }

    [Fact]
    public async Task Expect_Draft_Details_Hidden_From_Other_Users()
    {
        var createCommand = new ArticlesCreate.Command(
            new ArticlesCreate.ArticleData
            {
                Title = "Private draft for author only",
                Description = "Description of the private draft",
                Body = "Body of the private draft",
                IsDraft = true,
            }
        );

        var article = await ArticleHelpers.CreateArticle(this, createCommand);
        var slug = article.Slug ?? throw new InvalidOperationException();

        await SendAsync(
            new UsersCreate.Command(
                new UsersCreate.UserData("other-reader", "other@example.com", "password")
            )
        );

        var detailsHandler = new ArticlesDetails.QueryHandler(
            GetDbContext(),
            new StubCurrentUserAccessor("other-reader")
        );

        var exception = await Assert.ThrowsAsync<RestException>(async () =>
            await detailsHandler.Handle(
                new ArticlesDetails.Query(slug),
                new System.Threading.CancellationToken()
            )
        );

        Assert.Equal(HttpStatusCode.NotFound, exception.Code);
    }

    [Fact]
    public async Task Expect_Author_Can_Open_Own_Draft()
    {
        var createCommand = new ArticlesCreate.Command(
            new ArticlesCreate.ArticleData
            {
                Title = "Author readable draft",
                Description = "Description of the author draft",
                Body = "Body of the author draft",
                IsDraft = true,
            }
        );

        var article = await ArticleHelpers.CreateArticle(this, createCommand);
        var slug = article.Slug ?? throw new InvalidOperationException();

        var detailsHandler = new ArticlesDetails.QueryHandler(
            GetDbContext(),
            new StubCurrentUserAccessor(UserHelpers.DefaultUserName)
        );

        var envelope = await detailsHandler.Handle(
            new ArticlesDetails.Query(slug),
            new System.Threading.CancellationToken()
        );

        Assert.NotNull(envelope.Article);
        Assert.True(envelope.Article.IsDraft);
        Assert.Equal(slug, envelope.Article.Slug);
    }
}
