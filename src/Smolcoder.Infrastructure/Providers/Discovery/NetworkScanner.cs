using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Application.Models;

namespace Smolcoder.Infrastructure.Providers.Discovery;

/// <summary>
/// One address on one network interface.
/// </summary>
public sealed class InterfaceAddress
{
    public InterfaceAddress(string interfaceName, IPAddress address, IPAddress mask, bool loopback)
    {
        InterfaceName = interfaceName;
        Address = address;
        Mask = mask;
        Loopback = loopback;
    }

    public string InterfaceName { get; }

    public IPAddress Address { get; }

    public IPAddress Mask { get; }

    public bool Loopback { get; }
}

/// <summary>
/// A range of addresses to sweep.
/// </summary>
public sealed class Subnet
{
    public Subnet(uint first, uint last, uint own)
    {
        First = first;
        Last = last;
        Own = own;
    }

    public uint First { get; }

    public uint Last { get; }

    /// <summary>
    /// This computer's own address in the range, which is skipped.
    /// </summary>
    public uint Own { get; }
}

/// <summary>
/// Finds Ollama and LM Studio servers on the local network: a quick TCP sweep of the private
/// IPv4 subnets this computer is on (ports 11434 and 1234), then each open port is asked what it is.
/// Runs only when the user asks, never at startup.
/// </summary>
public sealed class NetworkScanner : INetworkScanner
{
    private const int Parallelism = 64;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan IdentifyTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly Regex VirtualInterface = new Regex("^(docker|br-|veth|virbr|vmnet|vboxnet|bridge|utun|awdl|llw|anpi|ap\\d)|vEthernet \\((WSL|Default Switch)", RegexOptions.IgnoreCase);
    private static readonly int[] Ports = new int[] { ServerAddress.OllamaPort, ServerAddress.LmStudioPort };

    private readonly ServerIdentifier identifier;

    public NetworkScanner(ServerIdentifier identifier)
    {
        this.identifier = identifier;
    }

    public async Task<NetworkScanResult> ScanAsync(IProgress<ScanProgress> progress, CancellationToken cancellationToken)
    {
        List<Subnet> subnets = LocalSubnets(InterfaceAddresses());
        List<IPAddress> targets = new List<IPAddress>();
        foreach (Subnet subnet in subnets)
        {
            for (uint value = subnet.First; value <= subnet.Last; value++)
            {
                if (value != subnet.Own)
                {
                    targets.Add(ToAddress(value));
                }
            }
        }
        int total = targets.Count * Ports.Length;
        int done = 0;
        int answered = 0;
        object gate = new object();
        Dictionary<string, List<int>> open = new Dictionary<string, List<int>>();
        List<(IPAddress Address, int Port)> probes = new List<(IPAddress, int)>();
        foreach (IPAddress target in targets)
        {
            foreach (int port in Ports)
            {
                probes.Add((target, port));
            }
        }

        ParallelOptions options = new ParallelOptions();
        options.MaxDegreeOfParallelism = Parallelism;
        options.CancellationToken = cancellationToken;
        await Parallel.ForEachAsync(probes, options, async (probe, token) =>
        {
            ConnectOutcome outcome = await ConnectAsync(probe.Address, probe.Port, token);
            lock (gate)
            {
                done++;
                if (outcome != ConnectOutcome.Unreachable)
                {
                    answered++;
                }
                if (outcome == ConnectOutcome.Open)
                {
                    string key = probe.Address.ToString();
                    List<int>? ports;
                    if (!open.TryGetValue(key, out ports))
                    {
                        ports = new List<int>();
                        open[key] = ports;
                    }
                    ports.Add(probe.Port);
                }
            }
            progress.Report(new ScanProgress(done, total));
        });

        List<FoundHost> hosts = new List<FoundHost>();
        List<Task<FoundHost?>> identifications = new List<Task<FoundHost?>>();
        foreach (KeyValuePair<string, List<int>> pair in open)
        {
            identifications.Add(IdentifyHostAsync(pair.Key, pair.Value, cancellationToken));
        }
        foreach (Task<FoundHost?> identification in identifications)
        {
            FoundHost? host = await identification;
            if (host != null)
            {
                hosts.Add(host);
            }
        }
        hosts.Sort((first, second) => string.CompareOrdinal(first.Address, second.Address));
        return new NetworkScanResult(hosts, targets.Count, total > 0 && answered == 0);
    }

    /// <summary>
    /// Private IPv4 subnets of the interfaces that are up. Virtual interfaces (containers, VPN
    /// tunnels, AirDrop) are skipped, and anything wider than /24 is cut to the /24 around this
    /// computer, so a /16 office network is not swept for an hour.
    /// </summary>
    public static List<Subnet> LocalSubnets(IEnumerable<InterfaceAddress> addresses)
    {
        List<Subnet> subnets = new List<Subnet>();
        HashSet<uint> seen = new HashSet<uint>();
        foreach (InterfaceAddress entry in addresses)
        {
            if (entry.Loopback || entry.Address.AddressFamily != AddressFamily.InterNetwork || VirtualInterface.IsMatch(entry.InterfaceName))
            {
                continue;
            }
            if (!ServerAddress.IsPrivateHost(entry.Address.ToString()) || entry.Address.ToString().StartsWith("127.", StringComparison.Ordinal) || entry.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            {
                continue;
            }
            uint own = ToNumber(entry.Address);
            uint mask = ToNumber(entry.Mask);
            int prefix = PrefixLength(mask);
            if (prefix >= 31)
            {
                continue;
            }
            if (prefix < 24)
            {
                mask = 0xFFFFFF00;
            }
            uint network = own & mask;
            uint broadcast = network | ~mask;
            if (!seen.Add(network))
            {
                continue;
            }
            subnets.Add(new Subnet(network + 1, broadcast - 1, own));
        }
        return subnets;
    }

    private static List<InterfaceAddress> InterfaceAddresses()
    {
        List<InterfaceAddress> addresses = new List<InterfaceAddress>();
        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }
                bool loopback = adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback;
                foreach (UnicastIPAddressInformation unicast in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }
                    addresses.Add(new InterfaceAddress(adapter.Name, unicast.Address, unicast.IPv4Mask, loopback));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // No interfaces we can read: nothing to scan.
        }
        return addresses;
    }

    private enum ConnectOutcome
    {
        Open,
        Refused,
        Unreachable
    }

    private static async Task<ConnectOutcome> ConnectAsync(IPAddress address, int port, CancellationToken token)
    {
        using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            limit.CancelAfter(ConnectTimeout);
            using (TcpClient client = new TcpClient(AddressFamily.InterNetwork))
            {
                try
                {
                    await client.ConnectAsync(address, port, limit.Token);
                    return ConnectOutcome.Open;
                }
                catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    // Someone is there, just not a model server.
                    return ConnectOutcome.Refused;
                }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    return ConnectOutcome.Unreachable;
                }
            }
        }
    }

    private async Task<FoundHost?> IdentifyHostAsync(string address, List<int> ports, CancellationToken token)
    {
        List<ServerInfo> servers = new List<ServerInfo>();
        foreach (int port in ports)
        {
            ServerInfo? info = await identifier.IdentifyAsync("http://" + address + ":" + port, IdentifyTimeout, token);
            if (info != null)
            {
                servers.Add(info);
            }
        }
        if (servers.Count == 0)
        {
            return null;
        }
        string name = await ConfirmedNameAsync(address);
        foreach (ServerInfo server in servers)
        {
            foreach (ModelDescriptor model in server.Models)
            {
                model.Host = name;
            }
        }
        return new FoundHost(address, name, servers);
    }

    /// <summary>
    /// A name for the address that resolves back to it (so it stays valid if the IP changes),
    /// or the address itself.
    /// </summary>
    private static async Task<string> ConfirmedNameAsync(string address)
    {
        try
        {
            using (CancellationTokenSource limit = new CancellationTokenSource(IdentifyTimeout))
            {
                IPHostEntry reverse = await Dns.GetHostEntryAsync(address, limit.Token);
                string name = reverse.HostName;
                if (name.Length == 0 || name == address)
                {
                    return address;
                }
                IPAddress[] forward = await Dns.GetHostAddressesAsync(name, limit.Token);
                foreach (IPAddress candidate in forward)
                {
                    if (candidate.ToString() == address)
                    {
                        return name.TrimEnd('.');
                    }
                }
            }
        }
        catch (Exception)
        {
            // No reverse name.
        }
        return address;
    }

    private static uint ToNumber(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress ToAddress(uint value)
    {
        return new IPAddress(new byte[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });
    }

    private static int PrefixLength(uint mask)
    {
        int length = 0;
        while ((mask & 0x80000000) != 0)
        {
            length++;
            mask <<= 1;
        }
        return length;
    }
}
