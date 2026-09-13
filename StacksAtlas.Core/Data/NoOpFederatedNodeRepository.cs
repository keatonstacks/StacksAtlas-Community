using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Data;

public class NoOpFederatedNodeRepository : IFederatedNodeRepository
{
    public FederatedNode? GetById(string id) => null;
    public FederatedNode? GetByHardwareId(string hardwareId) => null;
    public List<FederatedNode> GetAll() => new();
    public void UpsertNode(FederatedNode node) { }
    public bool Delete(string id) => false;
}
