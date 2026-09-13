using StacksAtlas.Core.Models.Updates;

namespace StacksAtlas.Core.Services.Updates;

public interface IApplianceVersionProvider
{
    ApplianceVersionInfo GetCurrent();
}
