using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Data;

public interface IFederatedNodeRepository
{
    FederatedNode? GetById(string id);
    FederatedNode? GetByHardwareId(string hardwareId);
    List<FederatedNode> GetAll();
    void UpsertNode(FederatedNode node);
    bool Delete(string id);
}
