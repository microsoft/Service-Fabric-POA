// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentService
{
    using System;
    using System.Fabric.Health;
    using System.Fabric;

    /// <summary>
    /// Helper class for HealthManager
    /// </summary>
    public class HealthManagerHelper
    {
        /// <summary>
        /// Source ID for the health report generated from Patch orchestration Agent
        /// </summary>
        internal const string SourceId = "Patch Orchestration Node Agent Service";

        /// <summary>
        /// This indicated how long healthy reports should persist in health store.
        /// For unhealthy reports we use the default value infinite.
        /// </summary>
        internal const long HealthyEventTtlInMinutes = 30;
        
        /// <summary>
        /// Posts a health report against the current service instance of Patch Orchestration Agent
        /// </summary>
        /// <param name="fabricClient">Fabric client to carry out HealthManager operation on cluster</param>
        /// <param name="serviceContext">Context of the current service</param>
        /// <param name="sourceId">SourceId for health report</param>
        /// <param name="healthReportProperty">Property of the health report</param>
        /// <param name="description">description of the health report</param>
        /// <param name="healthState">HealthState for the health report</param>
        /// <param name="timeToLiveInMinutes">Time to live for health report</param>
        internal static void PostServiceInstanceHealthReport(FabricClient fabricClient, ServiceContext serviceContext, string sourceId, string healthReportProperty, string description,
            HealthState healthState, long timeToLiveInMinutes = -1)
        {
            HealthInformation healthInformation = new HealthInformation(sourceId, healthReportProperty,
                healthState);
            healthInformation.RemoveWhenExpired = true;
            healthInformation.Description = description;
            if (timeToLiveInMinutes >= 0)
            {
                healthInformation.TimeToLive = TimeSpan.FromMinutes(timeToLiveInMinutes);
            }

            StatelessServiceInstanceHealthReport serviceInstanceHealthReport =
                new StatelessServiceInstanceHealthReport(serviceContext.PartitionId,
                    serviceContext.ReplicaOrInstanceId, healthInformation);
            fabricClient.HealthManager.ReportHealth(serviceInstanceHealthReport);
        }
    }
}
