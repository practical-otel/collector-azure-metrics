using infra;
using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using System.Collections.Generic;

return await Pulumi.Deployment.RunAsync(() =>
{
    // Create an Azure Resource Group
    var resourceGroup = new ResourceGroup("resourceGroup");

    var collector = new OtelCollector("collector", new OtelCollectorArgs {
        ResourceGroup = resourceGroup.Name
    });


    // Export the primary key of the Storage Account
    return new Dictionary<string, object?>
    {
        ["collector"] = collector.CollectorHostname
    };
});