using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;

namespace EnableBanking.Handlers
{
    public class TokenHandler : DelegatingHandler
    {
        private readonly string _jwtAudience = "api.enablebanking.com";
        private readonly string _jwtIssuer = "enablebanking.com";

        private readonly TokenHandlerOptions _options;

        public TokenHandler(IOptions<TokenHandlerOptions> options)
        {
            _options = options.Value;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetAccessToken());
            return await base.SendAsync(request, cancellationToken);
        }

        /// <summary>
        /// Resolves a relative <see cref="TokenHandlerOptions.KeyPath"/> against the application
        /// directory rather than the working directory. IIS worker processes and Windows services
        /// start in system32, which is where a relative path would otherwise be looked up.
        /// </summary>
        private string ResolveKeyPath() =>
            Path.IsPathRooted(_options.KeyPath)
                ? _options.KeyPath
                : Path.Combine(AppContext.BaseDirectory, _options.KeyPath);

        private string GetAccessToken()
        {
            using RSA rsa = RSA.Create();
            var text = File.ReadAllText(ResolveKeyPath());
            rsa.ImportFromPem(text);

            var signingCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
            {
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            };

            var now = DateTime.Now;
            var unixTimeSeconds = new DateTimeOffset(now).ToUnixTimeSeconds();

            var jwt = new JwtSecurityToken(
                audience: _jwtAudience,
                issuer: _jwtIssuer,
                claims: new Claim[] {
                    new Claim(JwtRegisteredClaimNames.Iat, unixTimeSeconds.ToString(), ClaimValueTypes.Integer64)
                },
                expires: now.AddMinutes(30),
                signingCredentials: signingCredentials
            );
            jwt.Header.Add("kid", _options.AppKid);
            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }
    }
}
