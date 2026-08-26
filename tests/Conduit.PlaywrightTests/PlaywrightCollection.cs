using System.Threading.Tasks;
using Xunit;

namespace Conduit.PlaywrightTests;

public sealed class PlaywrightCollectionFixture : IAsyncLifetime
{
    public CustomWebApplicationFactory Factory { get; } = new();

    public Task InitializeAsync()
    {
        _ = Factory.Server;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await Factory.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PlaywrightCollection : ICollectionFixture<PlaywrightCollectionFixture>
{
    public const string Name = "Playwright";
}
