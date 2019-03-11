// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.CoordinatorService
{
    using System;
    using System.Fabric;
    using System.Fabric.Health;

    class HealthManagerHelper
    {
        /// <summary>
        /// Source ID for the health report generated
        /// </summary>
        private const string SourceId = "Patch Orchestration Coordinator Service";

        /// <summary>
        /// Posts a health report against the current service to indicate status of Patch Orchestration Agent
        /// </summary>
        /// <param name="fabricClient">Fabric client object to carry out HM operations</param>
        /// <param name="serviceUri">Uri of the service against which health report is to be posted</param>
        /// <param name="healthReportProperty">Property of the health report</param>
        /// <param name="description">Description of the health report</param>
        /// <param name="healthState">HealthState for the health report</param>
        /// <param name="timeToLiveInMinutes">Time to live in minutes for health report</param>
        internal static void PostNodeHealthReport(FabricClient fabricClient, Uri serviceUri, string healthReportProperty, string description,
            HealthState healthState, long timeToLiveInMinutes = -1)
        {
            HealthInformation healthInformation = new HealthInformation(SourceId, healthReportProperty,
                healthState)
            {
                RemoveWhenExpired = true,
                Description = description
            };

            if (timeToLiveInMinutes >= 0)
            {
                healthInformation.TimeToLive = TimeSpan.FromMinutes(timeToLiveInMinutes);
            }

            ServiceHealthReport serviceHealthReport = new ServiceHealthReport(serviceUri, healthInformation);
            fabricClient.HealthManager.ReportHealth(serviceHealthReport);
        }
    }
}
