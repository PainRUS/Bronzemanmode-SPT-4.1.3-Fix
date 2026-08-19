using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;

namespace Bronzeman;

[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public sealed class PatchLoader(
    IEnumerable<IRuntimePatch> patches,
    BronzemanConfig config)
    : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (!config.Unlocks.Quests)
            return Task.CompletedTask;

        foreach (var patch in patches)
        {
            if (patch is BronzemanQuestPatch)
                patch.Enable();
        }

        return Task.CompletedTask;
    }
}
