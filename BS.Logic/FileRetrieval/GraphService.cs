using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace BS.Logic.FileRetrieval;

public class GraphService
{
    private readonly IConfiguration _configuration;

    public GraphService(IConfiguration configuration)
    {
        _configuration = configuration;
    }


    public async Task Test2()
    {
        var authority = new Uri(String.Format(CultureInfo.InvariantCulture, _configuration["MSGraph:Instance"], _configuration["MSGraph:Tenant"]));
        IConfidentialClientApplication app = ConfidentialClientApplicationBuilder.Create(_configuration["MSGraph:ClientId"])
            .WithClientSecret(_configuration["MSGraph:ClientSecret"])
            .WithAuthority(authority)
            .Build();
        app.AddInMemoryTokenCache();

        string[] scopes = { $"{_configuration["MSGraph:ApiUrl"]}.default" }; // Generates a scope -> "https://graph.microsoft.com/.default"


        await CallMSGraphUsingGraphSDK(app, scopes);
    }


    private static async Task CallMSGraphUsingGraphSDK(IConfidentialClientApplication app, string[] scopes)
    {
        // Prepare an authenticated MS Graph SDK client
        GraphServiceClient graphServiceClient = GetAuthenticatedGraphClient(app, scopes);


        List<User> allUsers = new List<User>();

        try
        {
            var user = await graphServiceClient.Users.Request().GetAsync();
            Console.WriteLine("User: " + user);
            // Console.WriteLine($"Found {users.Count()} users in the tenant");
        }
        catch (ServiceException e)
        {
            Console.WriteLine("We could not retrieve the user's list: " + $"{e}");
        }
    }


    private static GraphServiceClient GetAuthenticatedGraphClient(IConfidentialClientApplication app, string[] scopes)
    {
        GraphServiceClient graphServiceClient =
            new GraphServiceClient("https://graph.microsoft.com/V1.0/", new DelegateAuthenticationProvider(async (requestMessage) =>
            {
                // Retrieve an access token for Microsoft Graph (gets a fresh token if needed).
                AuthenticationResult result = await app.AcquireTokenForClient(scopes)
                    .ExecuteAsync();

                // Add the access token in the Authorization header of the API request.
                requestMessage.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", result.AccessToken);
            }));

        return graphServiceClient;
    }
}