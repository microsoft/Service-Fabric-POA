// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.TelemetryLib
{
    /// <summary>
    /// Telemetry doesn't have an eventsource of its own. This interface declares the bare minimum events,
    /// which should be implemented by eventsource of components which want to use Telemtry
    /// </summary>
    public interface ITelemetryEventSource
    {
        void VerboseMessage(string message, params object[] args);

        void PatchInstallationTelemetryEvent(string clusterId,
            string tenantId,
            string clusterType,
            string nodeName,
            string updateFrequency,
            string updateQuery,
            string approvalPolicy,
            string applicationVersion,
            double totalInstallations,
            double successfulInstallations,
            string operationResult);
    }
}
