namespace EnableBanking.Handlers;

// Source - https://stackoverflow.com/a
// Posted by Kiran, modified by community. See post 'Timeline' for change history
// Retrieved 2026-01-01, License - CC BY-SA 3.0

public class LoggingHandler : DelegatingHandler
{
    public LoggingHandler()
        : base()
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Console.WriteLine("Request:");
        Console.WriteLine(request.ToString());
        if (request.Content != null)
        {
            Console.WriteLine(await request.Content.ReadAsStringAsync());
        }

        Console.WriteLine();

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        Console.WriteLine("Response:");
        Console.WriteLine(response.ToString());
        if (response.Content != null)
        {
            Console.WriteLine(await response.Content.ReadAsStringAsync());
        }

        Console.WriteLine();

        return response;
    }
}