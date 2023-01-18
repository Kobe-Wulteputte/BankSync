using VMelnalksnis.NordigenDotNet;
using VMelnalksnis.NordigenDotNet.Requisitions;

namespace BS.Logic;

public class RequisitionService
{
    private readonly INordigenClient _nordigenClient;

    public RequisitionService(INordigenClient nordigenClient)
    {
        _nordigenClient = nordigenClient;
    }

    public Guid ReqId { get; } = new("e5e492ab-d407-4722-8b56-82ef4656841e");

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