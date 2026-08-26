using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Conduit.PlaywrightTests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string Url = "http://127.0.0.1:5080";

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"conduit-playwright-{Guid.NewGuid():N}.db"
    );

    private readonly string _dummyDbPath = Path.Combine(
        Path.GetTempPath(),
        $"conduit-playwright-dummy-{Guid.NewGuid():N}.db"
    );

    private IHost? _host;

    public CustomWebApplicationFactory()
    {
        // WebApplication.CreateBuilder reads environment variables. UseSetting alone
        // does not override Database:ConnectionString, so the host would open
        // src/Conduit/realworld.db and EnsureCreated would collide with local data.
        Environment.SetEnvironmentVariable("Database__Provider", "sqlite");
        Environment.SetEnvironmentVariable("Database__ConnectionString", $"Data Source={_dbPath}");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseUrls(Url);
        builder.ConfigureAppConfiguration(
            (_, config) =>
                config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Database:Provider"] = "sqlite",
                        ["Database:ConnectionString"] = $"Data Source={_dbPath}",
                    }
                )
        );
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureWebHost(webHostBuilder =>
        {
            webHostBuilder.UseSetting("Database:Provider", "sqlite");
            webHostBuilder.UseSetting("Database:ConnectionString", $"Data Source={_dummyDbPath}");
        });

        var testHost = builder.Build();

        builder.ConfigureWebHost(webHostBuilder =>
        {
            webHostBuilder.UseKestrel();
            webHostBuilder.UseUrls(Url);
            webHostBuilder.UseSetting("Database:Provider", "sqlite");
            webHostBuilder.UseSetting("Database:ConnectionString", $"Data Source={_dbPath}");
        });

        try
        {
            _host = builder.Build();
            _host.Start();
        }
        catch (Exception ex) when (IsAddressInUse(ex))
        {
            throw new InvalidOperationException(
                "Port 5080 is already in use. Stop `make run-local` before running Conduit.PlaywrightTests.",
                ex
            );
        }

        var server = _host.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        ClientOptions.BaseAddress = addresses!.Addresses.Select(static x => new Uri(x)).Last();

        testHost.Start();
        return testHost;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _host?.Dispose();
            TryDelete(_dbPath);
            TryDelete(_dbPath + "-shm");
            TryDelete(_dbPath + "-wal");
            TryDelete(_dummyDbPath);
            TryDelete(_dummyDbPath + "-shm");
            TryDelete(_dummyDbPath + "-wal");
        }

        base.Dispose(disposing);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Temp sqlite files are best-effort cleanup.
        }
    }

    private static bool IsAddressInUse(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SocketException)
            {
                return true;
            }

            if (
                current is IOException
                && current.Message.Contains(
                    "address already in use",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }
        }

        return false;
    }
}
