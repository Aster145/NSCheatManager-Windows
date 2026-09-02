using System.Net.Sockets;
using System.IO;
using System.Text;

namespace NSCheatManager.Windows.Protocol;

public sealed class SysBotbaseClient : IAsyncDisposable
{
    private readonly TcpClient _client = new();
    private NetworkStream? _stream;

    public async Task<GameIdentity> ConnectAndRecognizeAsync(string host, int port, CancellationToken cancellationToken)
    {
        await ConnectAsync(host, port, cancellationToken);
        return await RecognizeAsync(cancellationToken);
    }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        await _client.ConnectAsync(host, port, connectTimeout.Token);
        _stream = _client.GetStream();
    }

    private async Task<string> QueryWithTimeoutAsync(string command, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        return await QueryAsync(command, timeout.Token);
    }

    public async Task<GameIdentity> RecognizeAsync(CancellationToken cancellationToken)
    {
        var titleId = await QueryWithTimeoutAsync("getTitleID", cancellationToken);
        var buildId = await QueryWithTimeoutAsync("getBuildID", cancellationToken);
        var mainBase = await QueryWithTimeoutAsync("getMainNsoBase", cancellationToken);
        var heapBase = await QueryWithTimeoutAsync("getHeapBase", cancellationToken);
        if (!IsHex(titleId, 16) || !IsBuildId(buildId) || !TryParseAddress(mainBase, out _) || !TryParseAddress(heapBase, out _)) throw new InvalidDataException("sys-botbase returned an invalid game identity.");
        return new GameIdentity(titleId.ToUpperInvariant(), buildId.ToUpperInvariant(), mainBase, heapBase);
    }

    public async Task<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken)
    {
        var text = await QueryAsync("pixelPeek", cancellationToken);
        var normalized = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        var bytes = Convert.FromHexString(normalized);
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8 || bytes[^2] != 0xFF || bytes[^1] != 0xD9) throw new InvalidDataException("pixelPeek did not return a JPEG.");
        return bytes;
    }

    public async Task SetScreenEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException("Not connected.");
        var request = Encoding.ASCII.GetBytes((enabled ? "screenOn" : "screenOff") + "\r\n");
        await stream.WriteAsync(request, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private async Task<string> QueryAsync(string command, CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException("Not connected.");
        var request = Encoding.ASCII.GetBytes(command + "\r\n");
        await stream.WriteAsync(request, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        var response = new List<byte>();
        var buffer = new byte[1];
        while (response.Count <= 1024 * 1024)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0) throw new IOException("sys-botbase disconnected.");
            if (buffer[0] == (byte)'\n') break;
            if (buffer[0] != (byte)'\r') response.Add(buffer[0]);
        }
        if (response.Count > 1024 * 1024) throw new InvalidDataException("sys-botbase response is too large.");
        return Encoding.ASCII.GetString(response.ToArray()).Trim();
    }

    private static bool IsHex(string value, int digits) => value.Length == digits && value.All(Uri.IsHexDigit);
    private static bool IsBuildId(string value) => value.Length is >= 8 and <= 64 && value.All(Uri.IsHexDigit);
    private static bool TryParseAddress(string value, out ulong address) => ulong.TryParse(value.Replace("0x", "", StringComparison.OrdinalIgnoreCase), System.Globalization.NumberStyles.HexNumber, null, out address);
    public ValueTask DisposeAsync() { _stream?.Dispose(); _client.Dispose(); return ValueTask.CompletedTask; }
}

public sealed record GameIdentity(string TitleId, string BuildId, string MainBase, string HeapBase);
