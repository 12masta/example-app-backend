using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Conduit.PlaywrightTests;

[Collection(PlaywrightCollection.Name)]
public sealed class ApiHostTests
{
    public ApiHostTests(PlaywrightCollectionFixture fixture) => _ = fixture.Factory;

    [Fact]
    public async Task TagsEndpoint_ReturnsOk()
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri(CustomWebApplicationFactory.Url),
        };
        var response = await client.GetAsync("/api/tags");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
