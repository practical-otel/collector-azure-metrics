using System;
using System.IO;
using System.Security.Cryptography;
using Pulumi;
using Pulumi.AzureNative.App;
using Pulumi.AzureNative.App.Inputs;
using Pulumi.AzureNative.App.Outputs;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using Pulumi.Command.Local;
using FileShare = Pulumi.AzureNative.Storage.FileShare;
using SkuName = Pulumi.AzureNative.Storage.SkuName;
using Type = Pulumi.AzureNative.App.Type;

namespace infra;

public class OtelCollector : ComponentResource
{

    public Output<string> CollectorHostname { get; private set; }

    public Output<ManagedServiceIdentityResponse?> Identity { get; private set; }
    public Output<string> RevisionName { get; }

    public OtelCollector(string name, OtelCollectorArgs args, ComponentResourceOptions? options = null) : 
        base("azure-metrics:otel-collector:app", name, args, options)
    {
        var config =new Config();
        var honeycombApiKeySecret = new SecretArgs
        {
            Name = "honeycomb-api-key",
            Value = config.RequireSecret("HONEYCOMB_API_KEY")
        };

        var storageAccount = new StorageAccount("sa", new StorageAccountArgs
        {
            ResourceGroupName = args.ResourceGroup,
            Sku = new SkuArgs
            {
                Name = SkuName.Standard_LRS
            },
            Kind = Kind.StorageV2

        });

        var fileShare = new FileShare("fileShare", new()
        {
            ResourceGroupName = args.ResourceGroup,
            AccountName = storageAccount.Name,
            EnabledProtocols = "SMB",
            ShareName = "config",
        });

        var storageAccountKeys = ListStorageAccountKeys.Invoke(new ListStorageAccountKeysInvokeArgs
        {
            ResourceGroupName = args.ResourceGroup,
            AccountName = storageAccount.Name
        });

        string configHash;
        using var fs = new FileStream("../config.yaml", FileMode.Open);
        using var sha256 = SHA256.Create();
        configHash = BitConverter.ToString(sha256.ComputeHash(fs));

        var configFileUpload = new Command("config-file-upload", new CommandArgs
        {
            Triggers = new[] { configHash },
            Update = Output.Format(@$"
        az storage file upload -s {fileShare.Name} \
                    --source ../config.yaml \
                    --no-progress \
                    --account-key {storageAccountKeys.Apply(k => k.Keys[0].Value)} \
                    --account-name {storageAccount.Name} > /dev/null"),
            Create = Output.Format(@$"az storage file upload -s {fileShare.Name} \
                    --source ../config.yaml \
                    --no-progress \
                    --account-key {storageAccountKeys.Apply(k => k.Keys[0].Value)} \
                    --account-name {storageAccount.Name} > /dev/null"),
        }, new() { DeletedWith = fileShare });

        var containerAppEnvironment = new ManagedEnvironment("collector-env", new ManagedEnvironmentArgs
        {
            ResourceGroupName = args.ResourceGroup,
            AppLogsConfiguration = new AppLogsConfigurationArgs
            {
                Destination = ""
            },
        });

        var containerAppEnvironmentStorage = new ManagedEnvironmentsStorage("collector-env-storage", new ManagedEnvironmentsStorageArgs
        {
            ResourceGroupName = args.ResourceGroup,
            EnvironmentName = containerAppEnvironment.Name,
            StorageName = "collector-config",
            Properties = new ManagedEnvironmentStoragePropertiesArgs
            {
                AzureFile = new AzureFilePropertiesArgs
                {
                    AccessMode = "ReadWrite",
                    ShareName = fileShare.Name,
                    AccountKey = storageAccountKeys.Apply(k => k.Keys[0].Value),
                    AccountName = storageAccount.Name,
                }
            }
        });

        var collectorApp = new ContainerApp("collector", new ContainerAppArgs
        {
            EnvironmentId = containerAppEnvironment.Id,
            ResourceGroupName = args.ResourceGroup,
            ContainerAppName = "collector",
            Identity = new ManagedServiceIdentityArgs
            {
                Type = ManagedServiceIdentityType.SystemAssigned
            },
            Configuration = new ConfigurationArgs
            {
                Ingress = new IngressArgs
                {
                    External = true,
                    TargetPort = 4318
                },
                Secrets = {
                    honeycombApiKeySecret
                },
                
            },
            Template = new TemplateArgs
            {
                Scale = new ScaleArgs
                {
                    MinReplicas = 1,
                    MaxReplicas = 1,
                },
                Volumes = {
                new VolumeArgs
                {
                    Name = "config",
                    StorageType = StorageType.AzureFile,
                    StorageName = containerAppEnvironmentStorage.Name,
                }
            },
                Containers = {
                new ContainerArgs
                {
                    Name = "collector",
                    Image = "otel/opentelemetry-collector-contrib:latest",
                    VolumeMounts = {
                        new VolumeMountArgs
                        {
                            VolumeName = "config",
                            MountPath = "/etc/otelcol-contrib",
                        }
                    },
                    Env = {
                        new EnvironmentVarArgs {
                            SecretRef = honeycombApiKeySecret.Name,
                            Name = "HONEYCOMB_API_KEY"
                        },
                        new EnvironmentVarArgs {
                            Name = "CONFIG_FILE_HASH", // used to trigger revision updates
                            Value = configHash
                        },
                        new EnvironmentVarArgs {
                            Name = "EVENTHUB_CONNECTION_STRING_LOGS",
                            Value = args.EventHubConnectionStringLogs
                        },
                        new EnvironmentVarArgs {
                            Name = "EVENTHUB_CONSUMER_GROUP",
                            Value = args.EventHubConsumerGroup
                        },
                        new EnvironmentVarArgs {
                            Name = "EVENTHUB_CONNECTION_STRING_METRICS",
                            Value = args.EventHubConnectionStringMetrics
                        },
                    },
                    Probes = {
                        new ContainerAppProbeArgs {
                            HttpGet = new ContainerAppProbeHttpGetArgs {
                                Path = "/",
                                Port = 13133,
                            },
                            Type = Type.Readiness
                        },
                        new ContainerAppProbeArgs {
                            HttpGet = new ContainerAppProbeHttpGetArgs {
                                Path = "/",
                                Port = 13133,
                            },
                            Type = Type.Liveness
                        }
                    }
                }
            },
            }
        }, new()
        {
            DependsOn = { configFileUpload }
        });

        CollectorHostname = collectorApp.Configuration.Apply(c => c!.Ingress!.Fqdn);
        Identity = collectorApp.Identity;
        RevisionName = collectorApp.LatestRevisionName;
    }
}

public class OtelCollectorArgs : ResourceArgs
{
    public Input<string> ResourceGroup { get; set; } = null!;
    public Input<string> EventHubConsumerGroup { get; set; } = null!;
    public Input<string> EventHubConnectionStringMetrics { get; set; } = null!;
    public Input<string> EventHubConnectionStringLogs { get; set; } = null!;
}