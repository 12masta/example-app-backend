using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace Conduit.PlaywrightTests;

[Collection(PlaywrightCollection.Name)]
public sealed class CriticalPathTests
{
    private static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("BASE_URL") ?? "http://localhost:30401";

    public CriticalPathTests(PlaywrightCollectionFixture fixture) => _ = fixture.Factory;

    [Fact]
    public async Task RegisterPublishAndSeeArticleOnGlobalFeed()
    {
        await EnsureFrontendAsync();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true }
        );
        var page = await browser.NewPageAsync();
        var coverage = new JsCoverage(page);
        await coverage.StartAsync();

        var id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var username = $"e2e{id}";
        var email = $"e2e{id}@example.com";
        var title = $"E2E article {username}";

        await page.GotoAsync($"{BaseUrl}/register");
        await page.GetByPlaceholder("Username").FillAsync(username);
        await page.GetByPlaceholder("Email").FillAsync(email);
        await page.GetByPlaceholder("Password").FillAsync("password1");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign up" }).ClickAsync();
        await page.WaitForURLAsync("**/settings");
        await coverage.HarvestAsync("register-and-settings");

        await page.GotoAsync($"{BaseUrl}/editor");
        await page.GetByPlaceholder("Article Title").FillAsync(title);
        await page.GetByPlaceholder("What's this article about?")
            .FillAsync("Critical path article");
        await page.GetByPlaceholder("Write your article (in markdown)")
            .FillAsync("This article was created by the e2e critical-path test.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Publish Article" }).ClickAsync();
        await page.WaitForURLAsync("**/article/**");
        await Assertions
            .Expect(page.GetByRole(AriaRole.Heading, new() { Name = title, Level = 1 }))
            .ToBeVisibleAsync();
        await coverage.HarvestAsync("editor-and-article");

        await page.GotoAsync($"{BaseUrl}/");
        await Assertions
            .Expect(
                page.Locator("[data-test=\"article-preview\"]").Filter(new() { HasText = title })
            )
            .ToBeVisibleAsync();
        await coverage.HarvestAsync("home");
        await coverage.WriteAsync();
    }

    private static async Task EnsureFrontendAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync(BaseUrl);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Frontend is not reachable at {BaseUrl}. Start `yarn start` on port 30401 with API_URL={CustomWebApplicationFactory.Url}/api.",
                ex
            );
        }
    }
}
