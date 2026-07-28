using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace EnableBanking.Handlers
{
    /// <summary>
    /// Adds the PSU headers some ASPSPs declare as required in their <c>required_psu_headers</c>
    /// metadata. Argenta, for example, requires <c>psu-ip-address</c>; Revolut requires none, which
    /// is why omitting them went unnoticed.
    /// </summary>
    public class PsuHeaderHandler : DelegatingHandler
    {
        private static readonly Lazy<string?> LocalAddress = new(ResolveLocalAddress);

        private readonly TokenHandlerOptions _options;

        public PsuHeaderHandler(IOptions<TokenHandlerOptions> options)
        {
            _options = options.Value;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var address = !string.IsNullOrWhiteSpace(_options.PsuIpAddress)
                ? _options.PsuIpAddress
                : LocalAddress.Value;

            if (!string.IsNullOrWhiteSpace(address))
            {
                request.Headers.Remove("psu-ip-address");
                request.Headers.Add("psu-ip-address", address);
            }

            return await base.SendAsync(request, cancellationToken);
        }

        /// <summary>
        /// Best-effort local IPv4, used when no address is configured. Returns null rather than a
        /// placeholder: sending a bogus address is worse than sending none.
        /// </summary>
        private static string? ResolveLocalAddress()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                                  && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                    .Select(unicast => unicast.Address)
                    .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork
                                          && !IPAddress.IsLoopback(ip))
                    ?.ToString();
            }
            catch (NetworkInformationException)
            {
                return null;
            }
        }
    }
}
