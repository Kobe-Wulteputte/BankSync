using NodaTime;
using VMelnalksnis.NordigenDotNet;
using VMelnalksnis.NordigenDotNet.Accounts;

namespace BS.Logic;

public class AccountService
{
    private readonly INordigenClient _nordigenClient;

    public AccountService(INordigenClient nordigenClient)
    {
        _nordigenClient = nordigenClient;
    }

    public async Task<List<BookedTransaction>> GetTransactions(Guid accountId)
    {
        // TODO: intervals
        var transactions = await _nordigenClient.Accounts.GetTransactions(accountId);
        return transactions.Booked;
    }
}