using System.Buffers.Binary;
using System.Net.Sockets;
using System.IO;

namespace NSCheatManager.Windows.Protocol;

public static class NoexsClient
{
    public static async Task DetachDmntAsync(string host, int port, CancellationToken cancellationToken)
    {
        try { await DirectAsync(host, port, cancellationToken); }
        catch when (!cancellationToken.IsCancellationRequested) { await FullHandshakeAsync(host, port, cancellationToken); }
    }

    private static async Task DirectAsync(string host, int port, CancellationToken token)
    {
        using var client = await ConnectAsync(host, port, token); await using var stream = client.GetStream();
        await CommandAsync(stream, 0x18, token);
    }

    private static async Task FullHandshakeAsync(string host, int port, CancellationToken token)
    {
        using var client = await ConnectAsync(host, port, token); await using var stream = client.GetStream();
        await SendAsync(stream, 0x1A, null, token); _ = await ReadIntAsync(stream, token);
        await SendAsync(stream, 0x10, null, token); var count = await ReadIntAsync(stream, token); if (count is < 1 or > 4096) throw new InvalidDataException("Invalid Noexs PID count.");
        var pids = new byte[count * 8]; await stream.ReadExactlyAsync(pids, token); Check(await ReadIntAsync(stream, token)); var dmntPid = pids.AsSpan(pids.Length - 8, 8).ToArray();
        await SendAsync(stream, 0x01, null, token); var status = new byte[4]; await stream.ReadExactlyAsync(status, token); await DrainAsync(stream, token);
        await SendAsync(stream, 0x0E, null, token); var current = new byte[8]; await stream.ReadExactlyAsync(current, token); Check(await ReadIntAsync(stream, token));
        await CommandAsync(stream, 0x0B, token); await SendAsync(stream, 0x0A, dmntPid, token); Check(await ReadIntAsync(stream, token)); await CommandAsync(stream, 0x0B, token); await CommandAsync(stream, 0x18, token); await CommandAsync(stream, 0x0B, token);
    }

    private static async Task<TcpClient> ConnectAsync(string host, int port, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var client = new TcpClient { NoDelay = true }; await client.ConnectAsync(host, port, timeout.Token); return client;
    }
    private static async Task CommandAsync(NetworkStream stream, byte opcode, CancellationToken token) { await SendAsync(stream, opcode, null, token); Check(await ReadIntAsync(stream, token)); }
    private static async Task SendAsync(NetworkStream stream, byte opcode, byte[]? payload, CancellationToken token) { await stream.WriteAsync(new[] { opcode }, token); if (payload is not null) await stream.WriteAsync(payload, token); await stream.FlushAsync(token); }
    private static async Task<int> ReadIntAsync(NetworkStream stream, CancellationToken token) { var bytes = new byte[4]; await stream.ReadExactlyAsync(bytes, token); return BinaryPrimitives.ReadInt32LittleEndian(bytes); }
    private static void Check(int result) { if (result != 0) throw new InvalidOperationException($"Noexs result 0x{result:X8}, module {result & 0x1FF}, description {(result >> 9) & 0x1FFF}."); }
    private static async Task DrainAsync(NetworkStream stream, CancellationToken token) { using var quiet = CancellationTokenSource.CreateLinkedTokenSource(token); quiet.CancelAfter(250); var buffer = new byte[256]; try { while (await stream.ReadAsync(buffer, quiet.Token) > 0) { } } catch (OperationCanceledException) when (!token.IsCancellationRequested) { } }
}
