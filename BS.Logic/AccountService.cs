using Microsoft.Extensions.Configuration;
using NodaTime;
using VMelnalksnis.NordigenDotNet;
using VMelnalksnis.NordigenDotNet.Accounts;

namespace BS.Logic;

public class AccountService
{
    private readonly INordigenClient _nordigenClient;
    private readonly IConfiguration _configuration;


    public AccountService(INordigenClient nordigenClient, IConfiguration configuration)
    {
        _nordigenClient = nordigenClient;
        _configuration = configuration;
    }

    public async Task<List<BookedTransaction>> GetTransactions(Guid accountId)
    {
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        var past = now.Minus(Duration.FromDays(int.Parse(_configuration["RetrievalDays"])));
        var transactions = await _nordigenClient.Accounts.GetTransactions(accountId, new Interval(past, now));
        return transactions.Booked;
    }
}