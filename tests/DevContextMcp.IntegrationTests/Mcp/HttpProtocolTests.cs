using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DevContextMcp.Server.Core.Contracts.Common;
using DevContextMcp.Server.Core.Contracts.ResolveLibrary;
using ModelContextProtocol.Client;

namespace DevContextMcp.IntegrationTests.Mcp;

public sealed class HttpProtocolTests
{
    [Fact]
    public async Task ChildProcessServesStatelessStreamableHttp()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var port = GetAvailablePort();
        var endpoint = new Uri($"http://127.0.0.1:{port}/mcp");
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"missing-http-mcp-index-{Guid.NewGuid():N}",
            "docs.db");
        var logs = new ConcurrentQueue<string>();
        var listening = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var process = CreateHostProcess(port, databasePath);
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            logs.Enqueue(eventArgs.Data);
            if (eventArgs.Data.Contains("Now listening on", StringComparison.OrdinalIgnoreCase))
            {
                listening.TrySetResult();
            }
        };
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                logs.Enqueue(eventArgs.Data);
            }
        };

        try
        {
            Assert.True(process.Start());
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            var exited = process.WaitForExitAsync(timeout.Token);
            var completed = await Task.WhenAny(listening.Task, exited);
            Assert.False(
                completed == exited,
                $"HTTP host exited before listening.{Environment.NewLine}{string.Join(Environment.NewLine, logs)}");
            await listening.Task.WaitAsync(timeout.Token);

            await using (var client = await CreateClientAsync(endpoint, timeout.Token))
            {
                var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
                var templates = await client.ListResourceTemplatesAsync(
                    cancellationToken: timeout.Token);
                var result = await client.CallToolAsync(
                    "resolve_library",
                    new Dictionary<string, object?> { ["query"] = "missing" },
                    cancellationToken: timeout.Token);
                var response = result.StructuredContent!.Value
                    .Deserialize<ResolveLibraryResponse>(
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));

                Assert.Equal(4, tools.Count);
                Assert.Contains(tools, tool => tool.Name == "resolve_library");
                Assert.Contains(templates, template =>
                    template.UriTemplate.Contains("/artifact/", StringComparison.Ordinal));
                Assert.Contains(templates, template =>
                    template.UriTemplate.Contains("/symbol/", StringComparison.Ordinal));
                Assert.Equal(ToolResultStatus.NotFound, response!.Status);
                Assert.Equal("index_unavailable", Assert.Single(response.Errors).Code);
            }

            await using (var secondClient = await CreateClientAsync(endpoint, timeout.Token))
            {
                var tools = await secondClient.ListToolsAsync(cancellationToken: timeout.Token);

                Assert.Equal(4, tools.Count);
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static Task<McpClient> CreateClientAsync(
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        return McpClient.CreateAsync(
            new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = endpoint,
                    TransportMode = HttpTransportMode.StreamableHttp,
                    ConnectionTimeout = TimeSpan.FromSeconds(10)
                }),
            cancellationToken: cancellationToken);
    }

    private static Process CreateHostProcess(int port, string databasePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = RepositoryRoot(),
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(HostAssemblyPath());
        startInfo.ArgumentList.Add(
            $"--DevContextMcp:McpUrl=http://127.0.0.1:{port}/mcp");
        startInfo.ArgumentList.Add(
            $"--DevContextMcp:DatabasePath={databasePath}");
        return new Process { StartInfo = startInfo };
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string HostAssemblyPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "DevContextMcp.Server.dll");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DevContextMcp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
