using Seq.Api;
using Seq.Api.ResourceGroups;

namespace SeqMcpServer.Services;

public static class SeqCapabilities
{
    public const string EventsResourceGroupName = "Events";
    public const string ScanLinkName = "Scan";

    public static async Task<bool> SupportsScanAsync(SeqConnection connection, CancellationToken cancellationToken = default)
    {
        var eventsGroup = await ((ILoadResourceGroup)connection).LoadResourceGroupAsync(EventsResourceGroupName, cancellationToken);
        return eventsGroup.Links.ContainsKey(ScanLinkName);
    }
}
