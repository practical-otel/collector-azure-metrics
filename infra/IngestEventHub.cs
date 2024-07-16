using Pulumi;
using Pulumi.AzureNative.EventHub;
using Pulumi.AzureNative.EventHub.Inputs;

namespace infra;

public class IngestEventHub : ComponentResource
{
    public Output<string> EventHubName { get; set; }

    public Output<string> HubConnectionStringMetrics { get; set; }
    public Output<string> HubConnectionStringLogs { get; set; }

    public Output<string> ConsumerGroup { get; set; }

    public IngestEventHub(string name, IngestEventHubArgs args, ComponentResourceOptions? options = null) :
        base("hub:ingest:otel", name, options)
    {
        var eventHubNamespace = new Namespace("diagnostics-hub", new NamespaceArgs
        {
            ResourceGroupName = args.ResourceGroupName,
            Sku = new SkuArgs
            {
                Name = SkuName.Standard
            },
        });

        var metricsHub = new EventHub("metrics", new EventHubArgs
        {
            ResourceGroupName = args.ResourceGroupName,
            NamespaceName = eventHubNamespace.Name,
            PartitionCount = 4,
            MessageRetentionInDays = 1
        });

        var logsHub = new EventHub("logs", new EventHubArgs
        {
            ResourceGroupName = args.ResourceGroupName,
            NamespaceName = eventHubNamespace.Name,
            PartitionCount = 4,
            MessageRetentionInDays = 1
        });

        var metricsConsumerGroup = new ConsumerGroup("collector", new ConsumerGroupArgs
        {
            ConsumerGroupName = "collector",
            ResourceGroupName = args.ResourceGroupName,
            NamespaceName = eventHubNamespace.Name,
            EventHubName = metricsHub.Name,
        });

        var metricsListenAuthRule = new EventHubAuthorizationRule("listen", new EventHubAuthorizationRuleArgs
        {
            ResourceGroupName = args.ResourceGroupName,
            NamespaceName = eventHubNamespace.Name,
            EventHubName = metricsHub.Name,
            Rights = [ AccessRights.Listen ]
        });

        var logsConsumerGroup = new ConsumerGroup("collector-logs", new ConsumerGroupArgs
        {
            ConsumerGroupName = "collector",
            ResourceGroupName = args.ResourceGroupName,
            NamespaceName = eventHubNamespace.Name,
            EventHubName = logsHub.Name,
        });

        var logsListenAuthRule = new EventHubAuthorizationRule("listen-logs", new EventHubAuthorizationRuleArgs
        {
            ResourceGroupName = args.ResourceGroupName,
            NamespaceName = eventHubNamespace.Name,
            EventHubName = logsHub.Name,
            Rights = [ AccessRights.Listen ]
        });

        this.EventHubName = metricsHub.Name;
        this.ConsumerGroup = metricsConsumerGroup.Name;
        this.HubConnectionStringMetrics = Output.Tuple(args.ResourceGroupName, eventHubNamespace.Name, metricsHub.Name, metricsListenAuthRule.Name).Apply(
            tuple =>
            {
                var keys = ListEventHubKeys.Invoke(new ListEventHubKeysInvokeArgs {
                    ResourceGroupName = tuple.Item1,
                    NamespaceName = tuple.Item2,
                    EventHubName = tuple.Item3,
                    AuthorizationRuleName = tuple.Item4
                });

                return keys.Apply(k => k.PrimaryConnectionString);
            });
        this.HubConnectionStringLogs = Output.Tuple(args.ResourceGroupName, eventHubNamespace.Name, logsHub.Name, logsListenAuthRule.Name).Apply(
            tuple =>
            {
                var keys = ListEventHubKeys.Invoke(new ListEventHubKeysInvokeArgs {
                    ResourceGroupName = tuple.Item1,
                    NamespaceName = tuple.Item2,
                    EventHubName = tuple.Item3,
                    AuthorizationRuleName = tuple.Item4
                });

                return keys.Apply(k => k.PrimaryConnectionString);
            });
    }
}

public class IngestEventHubArgs : ResourceArgs
{
    public Output<string> ResourceGroupName { get; set; } = null!;
}