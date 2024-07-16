using infra;
using Pulumi;
using Pulumi.AzureNative.Resources;
using System.Collections.Generic;

return await Pulumi.Deployment.RunAsync(() =>
{
    var resourceGroup = new ResourceGroup("resourceGroup");

    var ingestEventHub = new IngestEventHub("ingest", new IngestEventHubArgs {
        ResourceGroupName = resourceGroup.Name
    });

    var collector = new OtelCollector("collector", new OtelCollectorArgs {
        ResourceGroup = resourceGroup.Name,
        EventHubConnectionStringLogs = ingestEventHub.HubConnectionStringLogs,
        EventHubConnectionStringMetrics = ingestEventHub.HubConnectionStringMetrics,
        EventHubConsumerGroup = ingestEventHub.ConsumerGroup,
    });

    return new Dictionary<string, object?>
    {
        ["collector"] = Output.Format($"https://{collector.CollectorHostname}/v1/traces"),
        ["ingest"] = ingestEventHub.EventHubName,
        ["identity"] = collector.Identity.Apply(i => i!.PrincipalId),
        ["latest-revision-name"] = collector.RevisionName
    };
});