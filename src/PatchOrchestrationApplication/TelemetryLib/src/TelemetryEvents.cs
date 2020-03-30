
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.TelemetryLib
{
    using Helper;
    using System.Fabric;
    using Microsoft.ApplicationInsights;
    using System.Collections.Generic;
    using Microsoft.ApplicationInsights.Extensibility;

    /// <summary>
    /// Contains common telemetry events
    /// </summary>
    public class TelemetryEvents
    {
        private string AppInsightsInstrumentationKey = TelemetryConstants.AppInsightsInstrumentationKey;

        private const string AppInsightsEndPointAddress = "https://vortex.data.microsoft.com/collect/v1";
        private const string TokenString = "$Token$";
        private readonly TelemetryClient telemetryClient;
        private readonly FabricClient fabricClient;
        private readonly ITelemetryEventSource eventSource;

        private const string InstallationEventName = "PatchOrchestrationApplication.InstallationEvent";
        // Every time a new version of application would be release, manually update this version.
        // This application version is used for telemetry
        // For consistency keep this applicaiton version same as application version from application manifest.
        private const string ApplicationVersion = "1.4.4";

        public TelemetryEvents(FabricClient fabricClient, ITelemetryEventSource eventSource)
        {
            this.fabricClient = fabricClient;
            this.eventSource = eventSource;
            var appInsightsTelemetryConf = TelemetryConfiguration.Active;
            // in the current version the instrumentation key is hard-coded
            // adding AIF- to instrumentation key, and changing the endpoint to send telemetry to Vortex instead of AI.
            appInsightsTelemetryConf.InstrumentationKey = (AppInsightsInstrumentationKey == TokenString) ? "" : AppInsightsInstrumentationKey;
            appInsightsTelemetryConf.TelemetryChannel.EndpointAddress = AppInsightsEndPointAddress;
            this.telemetryClient = new TelemetryClient(appInsightsTelemetryConf);
        }

        /// <summary>
        /// Sends the telemetry for an installation event
        /// </summary>
        /// <param name="nodeName">name of the node where installation was done</param>
        /// <param name="updateFrequency">frequency at which update should be done</param>
        /// <param name="updateQuery">query used to filter updates</param>
        /// <param name="approvalPolicy">approval policy used in coordinator service</param>
        /// <param name="totalInstallations">Total number of updates installed</param>
        /// <param name="successfulInstallations">Out of total installations, how many were successfull</param>
        /// <param name="operationResult">Result of installation</param>
        public void PatchInstallationEvent(
            string nodeName,
            string updateFrequency,
            string updateQuery,
            string approvalPolicy,
            double totalInstallations,
            double successfulInstallations,
            string operationResult
            )
        {
            string clusterId;
            string tenantId;
            string clusterType;

            CustomerIdentificationUtility customerIdentification = new CustomerIdentificationUtility(this.fabricClient);

            try
            {
                if (customerIdentification.IsTelemtryDisabled() || string.IsNullOrEmpty(this.telemetryClient.InstrumentationKey))
                {
                    this.eventSource.VerboseMessage("Skipping sending telemetry as Telemetry is disabled for this cluster");
                    return;
                }

                customerIdentification.GetClusterIdAndType(out clusterId, out tenantId, out clusterType);
                this.PatchInstallationEvent(
                    clusterId,
                    tenantId,
                    clusterType,
                    ApplicationVersion,
                    nodeName,
                    updateFrequency,
                    updateQuery,
                    approvalPolicy,
                    totalInstallations,
                    successfulInstallations,
                    operationResult);
            }
            finally
            {
                customerIdentification?.Dispose();
            }
        }

        private void PatchInstallationEvent(
            string clusterId,
            string tenantId,
            string clusterType,
            string applicationVersion,
            string nodeName,
            string updateFrequency,
            string updateQuery,
            string approvalPolicy,
            double totalInstallations,
            double successfulInstallations,
            string operationResult)
        {
            this.eventSource.PatchInstallationTelemetryEvent(
                clusterId,
                tenantId,
                clusterType,
                nodeName,
                updateFrequency,
                updateQuery,
                approvalPolicy,
                applicationVersion,
                totalInstallations,
                successfulInstallations,
                operationResult);

            IDictionary<string, string> eventProperties = new Dictionary<string, string>();
            eventProperties.Add("ClusterId", clusterId);
            eventProperties.Add("TenantId", tenantId);
            eventProperties.Add("clusterType", clusterType);
            eventProperties.Add("applicationVersion", applicationVersion);
            eventProperties.Add("NodeNameHash", (nodeName.GetHashCode()).ToString());
            eventProperties.Add("updateFrequency", updateFrequency);
            eventProperties.Add("updateQuery", updateQuery);
            eventProperties.Add("approvalPolicy", approvalPolicy);
            eventProperties.Add("operationResult", operationResult);

            IDictionary<string, double> eventMetrics = new Dictionary<string, double>();
            eventMetrics.Add("totalInstallations", totalInstallations);
            eventMetrics.Add("successfulInstallations", successfulInstallations);
            this.telemetryClient.TrackEvent(InstallationEventName, eventProperties, eventMetrics);
            this.telemetryClient.Flush();
            // allow time for flushing
            System.Threading.Thread.Sleep(1000);
        }
    }
}