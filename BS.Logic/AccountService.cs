using Microsoft.Extensions.Configuration;
using NodaTime;
using VMelnalksnis.NordigenDotNet;
using VMelnalksnis.NordigenDotNet.Accounts;

namespace BS.Logic;

public class AccountService
{
    private readonly IConfiguration _configuration;
    private readonly INordigenClient _nordigenClient;


    public AccountService(INordigenClient nordigenClient, IConfiguration configuration)
    {
        _nordigenClient = nordigenClient;
        _configuration = configuration;
    }

    public async Task<List<BookedTransaction>> GetTransactions(Guid accountId)
    {
        Instant now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        Instant past = now.Minus(Duration.FromDays(int.Parse(_configuration["RetrievalDays"])));
        Transactions transactions = await _nordigenClient.Accounts.GetTransactions(accountId, new Interval(past, now));
        return transactions.Booked;
    }
}