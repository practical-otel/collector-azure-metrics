using Pulumi;
using Pulumi.AzureNative.EventHub;
using Pulumi.AzureNative.EventHub.Inputs;

namespace infra;

public class IngestEventHub : ComponentResource
{
    public Output<string> EventHubName { get; set; }

    public Output<string> ConnectionString { get; set; }

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

        var eventHub = new EventHub("metrics", new EventHubArgs
        {
            ResourceGroupName = args.ResourceGroupName,
            NamespaceName = eventHubNamespace.Name,
            PartitionCount = 4,
            MessageRetentionInDays = 1
        });

        var consumerGroup = new ConsumerGroup("collector", new ConsumerGroupArgs
        {
            ConsumerGroupName = "collector",
            ResourceGroupName = args.ResourceGroupName,
            NamespaceName = eventHubNamespace.Name,
            EventHubName = eventHub.Name,
        });

        var listenAuthRule = new EventHubAuthorizationRule("listen", new EventHubAuthorizationRuleArgs
        {
            ResourceGroupName = args.ResourceGroupName,
            NamespaceName = eventHubNamespace.Name,
            EventHubName = eventHub.Name,
            Rights = [ AccessRights.Listen ]
        });



        this.EventHubName = eventHub.Name;
        this.ConsumerGroup = consumerGroup.Name;
        this.ConnectionString = Output.Tuple(args.ResourceGroupName, eventHubNamespace.Name, eventHub.Name, listenAuthRule.Name).Apply(
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