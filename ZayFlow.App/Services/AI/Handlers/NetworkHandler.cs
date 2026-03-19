using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Contracts;

namespace ZayFlow.App.Services.AI.Handlers;

/// <summary>
/// Tier 5: Network tools — diagnostics, port scan, DNS, hosts file management.
/// </summary>
public class NetworkHandler
{
    private readonly ILogger<NetworkHandler> _logger;

    public NetworkHandler(ILogger<NetworkHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>network_diagnostics — Comprehensive network health check.</summary>
    public async Task<ActionResult> ExecuteNetworkDiagnosticsAsync(CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("🌐 Network Diagnostics Report\n");

        // Network interfaces
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToList();

        sb.AppendLine($"📡 Active adapters: {interfaces.Count}");
        foreach (var ni in interfaces)
        {
            var ipProps = ni.GetIPProperties();
            var ipv4 = ipProps.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            sb.AppendLine($"  • {ni.Name} ({ni.NetworkInterfaceType})");
            if (ipv4 != null)
                sb.AppendLine($"    IP: {ipv4.Address} / {ipv4.IPv4Mask}");
            sb.AppendLine($"    Speed: {ni.Speed / 1_000_000}Mbps");
        }

        // DNS servers
        var dnsServers = interfaces
            .SelectMany(ni => ni.GetIPProperties().DnsAddresses)
            .Distinct()
            .ToList();
        if (dnsServers.Any())
            sb.AppendLine($"\n🔗 DNS Servers: {string.Join(", ", dnsServers)}");

        // Gateway
        var gateways = interfaces
            .SelectMany(ni => ni.GetIPProperties().GatewayAddresses)
            .Select(g => g.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Distinct()
            .ToList();
        if (gateways.Any())
            sb.AppendLine($"🚪 Default Gateway: {string.Join(", ", gateways)}");

        // Connectivity tests
        sb.AppendLine("\n📊 Connectivity Tests:");
        var targets = new[] { ("Google DNS", "8.8.8.8"), ("Cloudflare", "1.1.1.1"), ("Microsoft", "13.107.4.52") };
        using var ping = new Ping();
        foreach (var (name, ip) in targets)
        {
            try
            {
                var reply = await ping.SendPingAsync(ip, 3000);
                sb.AppendLine($"  {(reply.Status == IPStatus.Success ? "✅" : "❌")} {name} ({ip}): {reply.RoundtripTime}ms");
            }
            catch
            {
                sb.AppendLine($"  ❌ {name} ({ip}): unreachable");
            }
        }

        // Public IP
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var publicIp = await http.GetStringAsync("https://api.ipify.org", ct);
            sb.AppendLine($"\n🌍 Public IP: {publicIp}");
        }
        catch { sb.AppendLine("\n🌍 Public IP: Could not determine"); }

        return new ActionResult { Success = true, Message = sb.ToString() };
    }

    /// <summary>port_scan — Scan common ports on a host.</summary>
    public async Task<ActionResult> ExecutePortScanAsync(string host, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host))
            return new ActionResult { Success = false, Message = "Please specify a host to scan." };

        var commonPorts = new (int Port, string Service)[]
        {
            (21, "FTP"), (22, "SSH"), (23, "Telnet"), (25, "SMTP"), (53, "DNS"),
            (80, "HTTP"), (110, "POP3"), (143, "IMAP"), (443, "HTTPS"), (445, "SMB"),
            (993, "IMAPS"), (995, "POP3S"), (1433, "MSSQL"), (3306, "MySQL"),
            (3389, "RDP"), (5432, "PostgreSQL"), (5900, "VNC"), (8080, "HTTP-Alt"), (8443, "HTTPS-Alt")
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"🔍 Port scan: {host}\n");

        var openPorts = new List<(int Port, string Service)>();

        foreach (var (port, service) in commonPorts)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port);
                if (await Task.WhenAny(connectTask, Task.Delay(1000, ct)) == connectTask && client.Connected)
                {
                    openPorts.Add((port, service));
                    sb.AppendLine($"  🟢 Port {port} ({service}): OPEN");
                }
            }
            catch { }
        }

        if (openPorts.Count == 0)
            sb.AppendLine("  No common ports open (or host unreachable).");

        sb.AppendLine($"\nScanned {commonPorts.Length} common ports, {openPorts.Count} open.");
        return new ActionResult { Success = true, Message = sb.ToString() };
    }

    /// <summary>dns_manage — DNS lookup and resolution.</summary>
    public async Task<ActionResult> ExecuteDnsManageAsync(string hostname, string action, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hostname))
            return new ActionResult { Success = false, Message = "Please specify a hostname." };

        var sb = new System.Text.StringBuilder();

        switch (action.ToLowerInvariant())
        {
            case "lookup" or "resolve" or "":
                try
                {
                    var addresses = await Dns.GetHostAddressesAsync(hostname, ct);
                    sb.AppendLine($"🔗 DNS Lookup: {hostname}\n");
                    foreach (var addr in addresses)
                        sb.AppendLine($"  {(addr.AddressFamily == AddressFamily.InterNetwork ? "IPv4" : "IPv6")}: {addr}");

                    // Reverse lookup
                    try
                    {
                        var hostEntry = await Dns.GetHostEntryAsync(addresses[0]);
                        sb.AppendLine($"\n  Reverse DNS: {hostEntry.HostName}");
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    return new ActionResult { Success = false, Message = $"DNS lookup failed: {ex.Message}" };
                }
                break;

            case "flush":
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "ipconfig",
                        Arguments = "/flushdns",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = System.Diagnostics.Process.Start(psi);
                    var output = await proc!.StandardOutput.ReadToEndAsync(ct);
                    proc.WaitForExit(5000);
                    sb.AppendLine($"🔄 DNS cache flushed.\n{output.Trim()}");
                }
                catch (Exception ex)
                {
                    return new ActionResult { Success = false, Message = $"Failed to flush DNS: {ex.Message}" };
                }
                break;

            default:
                return new ActionResult { Success = false, Message = "Unknown DNS action. Use: lookup, flush" };
        }

        return new ActionResult { Success = true, Message = sb.ToString() };
    }

    /// <summary>hosts_file — View or search the hosts file.</summary>
    public async Task<ActionResult> ExecuteHostsFileAsync(string action, CancellationToken ct)
    {
        var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

        if (!File.Exists(hostsPath))
            return new ActionResult { Success = false, Message = "Hosts file not found." };

        switch (action.ToLowerInvariant())
        {
            case "view" or "show" or "list" or "":
                var content = await File.ReadAllTextAsync(hostsPath, ct);
                var entries = content.Split('\n')
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
                    .Select(l => l.Trim())
                    .ToList();

                if (entries.Count == 0)
                    return new ActionResult { Success = true, Message = "📋 Hosts file has no custom entries (only comments)." };

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"📋 Hosts file entries ({entries.Count}):\n");
                foreach (var entry in entries.Take(30))
                    sb.AppendLine($"  {entry}");

                return new ActionResult { Success = true, Message = sb.ToString() };

            default:
                return new ActionResult { Success = false, Message = "Hosts file action: view. Editing requires admin privileges." };
        }
    }
}
