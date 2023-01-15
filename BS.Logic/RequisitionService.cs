using VMelnalksnis.NordigenDotNet;
using VMelnalksnis.NordigenDotNet.Requisitions;

namespace BS.Logic;

public class RequisitionService
{
    private readonly INordigenClient _nordigenClient;
    private Guid _reqId = new Guid("e5e492ab-d407-4722-8b56-82ef4656841e");

    public Guid ReqId => _reqId;

    public RequisitionService(INordigenClient nordigenClient)
    {
        _nordigenClient = nordigenClient;
    }

    public async Task<Requisition> New(string institutionId)
    {
        return await _nordigenClient.Requisitions.Post(new RequisitionCreation(new Uri("http://localhost"), institutionId));
    }

    public IAsyncEnumerable<Requisition> GetAll()
    {
        return _nordigenClient.Requisitions.Get();
    }
}