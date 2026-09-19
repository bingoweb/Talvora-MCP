using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

internal static class SmokeSupport
{
    internal static async Task<CallToolResult> EnsureSuccess(
        McpClientTool tool,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var result = await tool.CallAsync(arguments, cancellationToken: cancellationToken);
        if (result.IsError is true)
        {
            throw new InvalidOperationException($"Tool failed: {tool.Name}");
        }
        return result;
    }
    
    internal static async Task<CallToolResult> EnsureError(
        McpClientTool tool,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var result = await tool.CallAsync(arguments, cancellationToken: cancellationToken);
        if (result.IsError is not true)
        {
            throw new InvalidOperationException($"Tool unexpectedly succeeded: {tool.Name}");
        }
        return result;
    }
    
    internal static async Task<string> ReadToolText(
        McpClientTool tool,
        string path,
        CancellationToken cancellationToken = default)
    {
        var result = await EnsureSuccess(
            tool,
            new Dictionary<string, object?> { ["path"] = path },
            cancellationToken);
        return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
            ?? throw new InvalidOperationException($"Tool did not return text content: {tool.Name}");
    }
    
    internal static int GetFreeLoopbackTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(
            System.Net.IPAddress.Loopback,
            0);
        listener.Start();
        try
        {
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
    
    internal static async Task RunDevServerFixtureAsync(
        int port,
        string body)
    {
        var listener =
            new System.Net.Sockets.TcpListener(
                System.Net.IPAddress.Loopback,
                port);
    
        listener.Start();
    
        Console.WriteLine("READY");
        await Console.Out.FlushAsync();
    
        var bodyBytes =
            System.Text.Encoding.UTF8.GetBytes(body);
    
        try
        {
            while (true)
            {
                using var client =
                    await listener.AcceptTcpClientAsync();
                using var stream =
                    client.GetStream();
    
                try
                {
                    var requestBuffer =
                        new byte[4096];
                    var bytesRead =
                        await stream.ReadAsync(
                            requestBuffer);
    
                    if (bytesRead == 0)
                    {
                        continue;
                    }
    
                    var headerText =
                        "HTTP/1.1 200 OK\r\n" +
                        "Content-Type: text/plain; charset=utf-8\r\n" +
                        $"Content-Length: {bodyBytes.Length}\r\n" +
                        "Connection: close\r\n\r\n";
                    var headerBytes =
                        System.Text.Encoding.ASCII.GetBytes(
                            headerText);
    
                    await stream.WriteAsync(
                        headerBytes);
                    await stream.WriteAsync(
                        bodyBytes);
                    await stream.FlushAsync();
                }
                catch (IOException)
                {
                }
                catch (System.Net.Sockets.SocketException)
                {
                }
            }
        }
        finally
        {
            listener.Stop();
        }
    }
}
