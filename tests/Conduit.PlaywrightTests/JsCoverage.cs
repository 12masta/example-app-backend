using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Conduit.PlaywrightTests;

internal sealed partial class JsCoverage(IPage page)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly List<object> _entries = [];
    private readonly Dictionary<string, CachedScript> _scripts = new(StringComparer.Ordinal);
    private readonly List<Task> _cacheTasks = [];
    private readonly Lock _gate = new();
    private ICDPSession? _cdp;

    [GeneratedRegex(@"sourceMappingURL=(\S+)")]
    private static partial Regex SourceMapComment();

    public async Task StartAsync()
    {
        _cdp ??= await page.Context.NewCDPSessionAsync(page);
        _cdp.Event("Debugger.scriptParsed").OnEvent += OnScriptParsed;
        await _cdp.SendAsync("Debugger.enable");
        await _cdp.SendAsync("Profiler.enable");
        await _cdp.SendAsync(
            "Profiler.startPreciseCoverage",
            new Dictionary<string, object> { ["callCount"] = true, ["detailed"] = true }
        );
    }

    public async Task HarvestAsync(string phase)
    {
        if (_cdp is null)
        {
            throw new InvalidOperationException("JS coverage was not started.");
        }

        await DrainCacheAsync();

        var response = await _cdp.SendAsync("Profiler.takePreciseCoverage");
        await _cdp.SendAsync("Profiler.stopPreciseCoverage");

        if (response is { } json && json.TryGetProperty("result", out var result))
        {
            foreach (var script in result.EnumerateArray())
            {
                var record = await ToRecordAsync(_cdp, script, phase);
                if (record is not null)
                {
                    _entries.Add(record);
                }
            }
        }

        await _cdp.SendAsync(
            "Profiler.startPreciseCoverage",
            new Dictionary<string, object> { ["callCount"] = true, ["detailed"] = true }
        );
    }

    public async Task WriteAsync()
    {
        await DrainCacheAsync();
        Directory.CreateDirectory(RepoPaths.E2eJsRaw);
        var path = Path.Combine(RepoPaths.E2eJsRaw, "v8-coverage.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(_entries, JsonOptions));
    }

    private void OnScriptParsed(object? sender, JsonElement? payload)
    {
        if (payload is not { } json)
        {
            return;
        }

        var task = CacheParsedScriptAsync(json);
        lock (_gate)
        {
            _cacheTasks.Add(task);
        }
    }

    private async Task CacheParsedScriptAsync(JsonElement parsed)
    {
        var scriptId = parsed.TryGetProperty("scriptId", out var idProp)
            ? idProp.GetString()
            : null;
        var url = parsed.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
        if (string.IsNullOrEmpty(scriptId) || string.IsNullOrEmpty(url) || _cdp is null)
        {
            return;
        }

        if (url.StartsWith("debugger://", StringComparison.Ordinal))
        {
            return;
        }

        string? source = null;
        try
        {
            var sourceResponse = await _cdp.SendAsync(
                "Debugger.getScriptSource",
                new Dictionary<string, object> { ["scriptId"] = scriptId }
            );
            if (
                sourceResponse is { } sourceJson
                && sourceJson.TryGetProperty("scriptSource", out var sourceProp)
            )
            {
                source = sourceProp.GetString();
            }
        }
        catch (PlaywrightException)
        {
            source = await TryGetStringAsync(url);
        }

        if (string.IsNullOrEmpty(source))
        {
            source = await TryGetStringAsync(url);
        }

        string? sourceMap = null;
        if (!string.IsNullOrEmpty(source))
        {
            sourceMap = await TryGetSourceMapAsync(url, source);
        }

        var cached = new CachedScript(url, source, sourceMap);
        lock (_gate)
        {
            _scripts[scriptId] = cached;
            _scripts[url] = cached;
        }
    }

    private async Task DrainCacheAsync()
    {
        Task[] copy;
        lock (_gate)
        {
            copy = [.. _cacheTasks];
        }

        if (copy.Length > 0)
        {
            await Task.WhenAll(copy);
        }
    }

    private async Task<object?> ToRecordAsync(ICDPSession cdp, JsonElement script, string phase)
    {
        var url = script.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
        if (string.IsNullOrEmpty(url) || url.StartsWith("debugger://", StringComparison.Ordinal))
        {
            return null;
        }

        var scriptId = script.TryGetProperty("scriptId", out var idProp)
            ? idProp.GetString()
            : null;
        var cached = FindCached(scriptId, url);

        var source = cached?.Source;
        if (string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(scriptId))
        {
            try
            {
                var sourceResponse = await cdp.SendAsync(
                    "Debugger.getScriptSource",
                    new Dictionary<string, object> { ["scriptId"] = scriptId }
                );
                if (
                    sourceResponse is { } sourceJson
                    && sourceJson.TryGetProperty("scriptSource", out var sourceProp)
                )
                {
                    source = sourceProp.GetString();
                }
            }
            catch (PlaywrightException)
            {
                // The script was unloaded after navigation; fall back to URL or skip.
            }
        }

        source ??= await TryGetStringAsync(url);
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }

        var sourceMap = cached?.SourceMap ?? await TryGetSourceMapAsync(url, source);

        var functions = new List<object>();
        if (script.TryGetProperty("functions", out var functionsProp))
        {
            foreach (var fn in functionsProp.EnumerateArray())
            {
                functions.Add(
                    new
                    {
                        functionName = fn.TryGetProperty("functionName", out var name)
                            ? name.GetString()
                            : "",
                        isBlockCoverage = fn.TryGetProperty("isBlockCoverage", out var block)
                            && block.GetBoolean(),
                        ranges = fn.TryGetProperty("ranges", out var ranges)
                            ? ranges
                                .EnumerateArray()
                                .Select(r => new
                                {
                                    startOffset = r.GetProperty("startOffset").GetInt32(),
                                    endOffset = r.GetProperty("endOffset").GetInt32(),
                                    count = r.GetProperty("count").GetInt32(),
                                })
                            : [],
                    }
                );
            }
        }

        return new
        {
            phase,
            url,
            source,
            sourceMap,
            functions,
        };
    }

    private CachedScript? FindCached(string? scriptId, string url)
    {
        lock (_gate)
        {
            if (!string.IsNullOrEmpty(scriptId) && _scripts.TryGetValue(scriptId, out var byId))
            {
                return byId;
            }

            return _scripts.TryGetValue(url, out var byUrl) ? byUrl : null;
        }
    }

    private static async Task<string?> TryGetSourceMapAsync(string scriptUrl, string source)
    {
        var match = SourceMapComment().Match(source);
        if (!match.Success)
        {
            return null;
        }

        var mapUrl = match.Groups[1].Value;
        if (mapUrl.StartsWith("data:", StringComparison.Ordinal))
        {
            return null;
        }

        var resolved = Uri.TryCreate(scriptUrl, UriKind.Absolute, out var scriptUri)
            ? new Uri(scriptUri, mapUrl).ToString()
            : mapUrl;
        return await TryGetStringAsync(resolved);
    }

    private static async Task<string?> TryGetStringAsync(string url)
    {
        if (
            !url.StartsWith("http://", StringComparison.Ordinal)
            && !url.StartsWith("https://", StringComparison.Ordinal)
        )
        {
            return null;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    private sealed record CachedScript(string Url, string? Source, string? SourceMap);
}
