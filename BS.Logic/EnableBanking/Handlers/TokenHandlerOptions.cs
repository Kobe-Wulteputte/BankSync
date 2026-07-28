namespace EnableBanking.Handlers
{
    public class TokenHandlerOptions
    {
        public string KeyPath { get; set; } = string.Empty;
        public string AppKid { get; set; } = string.Empty;

        /// <summary>
        /// PSU IP address sent as the <c>psu-ip-address</c> header, which some ASPSPs require.
        /// Leave empty to fall back to the machine's local IPv4.
        /// </summary>
        public string PsuIpAddress { get; set; } = string.Empty;
    }
}
