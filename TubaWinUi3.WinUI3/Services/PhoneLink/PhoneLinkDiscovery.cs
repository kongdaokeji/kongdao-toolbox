using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services;

/// <summary>Best-effort IPv4 LAN discovery. Only public connection metadata is advertised.</summary>
internal sealed class PhoneLinkDiscovery : IDisposable
{
    internal const int DiscoveryPort = 18764;
    internal const string Request = "TUBALINK_DISCOVER_V1";
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _cts = new();
    private readonly byte[] _response;
    private int _disposed;
    internal Task Completion { get; }

    private PhoneLinkDiscovery(UdpClient udp, int httpPort)
    {
        _udp = udp;
        _response = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "tubalink", version = 1, name = Environment.MachineName, port = httpPort
        });
        Completion = RunAsync(_cts.Token);
    }

    internal static bool IsDiscoveryRequest(ReadOnlySpan<byte> bytes) =>
        bytes.SequenceEqual("TUBALINK_DISCOVER_V1"u8);

    internal static PhoneLinkDiscovery? TryStart(int httpPort)
    {
        UdpClient? udp = null;
        try
        {
            udp = new UdpClient(AddressFamily.InterNetwork);
            udp.ExclusiveAddressUse = true;
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            return new PhoneLinkDiscovery(udp, httpPort);
        }
        catch (Exception ex)
        {
            udp?.Dispose();
            Debug.WriteLine($"[PhoneLink] UDP discovery unavailable (HTTP remains available): {ex.Message}");
            return null;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var packet = await _udp.ReceiveAsync(ct).ConfigureAwait(false);
                    if (!IsDiscoveryRequest(packet.Buffer)) continue;
                    // Reply to the sender's actual source endpoint, never broadcast credentials.
                    await _udp.SendAsync(_response.AsMemory(), packet.RemoteEndPoint, ct).ConfigureAwait(false);
                }
                catch (SocketException ex) when (!ct.IsCancellationRequested)
                {
                    // Windows can report ICMP errors for a previous reply; keep discovery alive.
                    Debug.WriteLine($"[PhoneLink] UDP discovery network error: {ex.Message}");
                    await Task.Delay(100, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (ct.IsCancellationRequested) { }
        catch (SocketException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { Debug.WriteLine($"[PhoneLink] UDP discovery stopped: {ex.Message}"); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        _udp.Dispose(); // Releases 18764 immediately, even while ReceiveAsync is pending.
        _ = Completion.ContinueWith(_ => _cts.Dispose(), TaskScheduler.Default);
    }
}
