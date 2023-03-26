using VMelnalksnis.NordigenDotNet;
using VMelnalksnis.NordigenDotNet.Requisitions;

namespace BS.Logic.Nordigen;

public class RequisitionService
{
    private readonly INordigenClient _nordigenClient;

    public RequisitionService(INordigenClient nordigenClient)
    {
        _nordigenClient = nordigenClient;
    }

    public async Task<Requisition> New(string institutionId, string? userName)
    {
        var creation = new RequisitionCreation(new Uri("http://localhost"), institutionId);
        creation.Reference = userName;
        return await _nordigenClient.Requisitions.Post(creation);
    }

    public IAsyncEnumerable<Requisition> GetAll()
    {
        return _nordigenClient.Requisitions.Get();
    }
}