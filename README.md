# Process Azure Metrics with the Otel Collector

This repo shows how you can setup an OpenTelemetry Collector in Azure Container Apps to process Azure platform logs and metrics.

## Structure

The OpenTelemetry Collector is hosting in Azure Container Apps using a consumption plan.

There are 2 separate persistent volumes attached to the container app.

- Config file
- State storage

There is a single EventHub Namespace created and then 2 separate event hubs (one for logs, one for metrics). This is arguably personal preference, the collector receiver does some matching on them to split them. I've seen some issues where there have been issues which I put down to a batch containing both, but that may have been fixed. Either way, I prefer them to be separate.

## Azure EventHubs and State

Azure EventHubs is an event streaming service. It stores an ordered stream of events that you can move through to receive all of them. It's similar to kafka, however there is a specific nuance in that it doesn't support "server-side" checkpoints. This means that your client needs to maintain where it's read upto in the stream within event hubs and then ask for the next batch based on that point.

In order to do this, when interacting with Azure EventHubs, you need to use a "Checkpoint", and that needs to be persistently stored.

The OpenTelemetry Collector implements something called "Storage Extensions" which provide a uniform interface for components (like the Azure EventHub receiver) to interact with external storage. One of these is the `file_storage` extension which will store data in a local folder so that it's persistent beyond restarts. This is what we need to use in our collector to ensure that we don't end up reprocessing the stream from the start, each time that our container is restarted.

### Setting up file_storage

Like all components, you need to setup your component before you can use it. This is done in the extensions section.

```yaml
extensions: 
  file_storage/local:
    directory: /var/lib/otelcol/state
```

Then you need to add it to the `service` section.

```yaml
service:
  extensions: [file_storage/local]
```

Then you can use it in your eventhubreceiver component.

```yaml
  azureeventhub/metrics:
    ...
    storage: file_storage/local
```

## Connecting your Azure resources

This solution only sets up the pipeline to send the data onward to a backend (in this case Honeycomb). You'll need to use Diagnostic settings for each service to forward the data to the eventhubs you've setup.