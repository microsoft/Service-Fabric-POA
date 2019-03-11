// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentSFUtility.Helpers
{
    using System;
    using System.Fabric;
    using System.Fabric.Query;
    using System.Threading;
    using System.Threading.Tasks;
    using Common;
    using Microsoft.ServiceFabric.PatchOrchestration.CoordinatorService;
    using Microsoft.ServiceFabric.Services.Remoting.Client;

    /// <summary>
    /// Provides methods to update the PatchOrchestrationApplication's CoordinatorService with result of 
    /// 1) Search and Download
    /// 2) Installation operation
    /// </summary>
    internal class CoordinatorServiceHelper
    {
        /// <summary>
        /// Application Uri needed to invoke the remote Api's of CoordinatorService
        /// </summary>
        private const string CoordinatorServiceSuffix = "/CoordinatorService";

        /// <summary>
        /// Gets the Status of the Deployed application
        /// </summary>
        /// <param name="fabricClient">fabric client to carry out operations on service fabric</param>
        /// <param name="applicationName">Application name which needs to be queried</param>
        /// <param name="timeout">timeout for this operation</param>
        /// <param name="cancellationToken">cancellation token to cancel this async operation</param>
        /// <returns>A Task representing the asnyc operation, result of the task would be
        ///  <see cref="NodeAgentSfUtilityExitCodes.Success"/> in case applicaiton exists,
        ///  <see cref="NodeAgentSfUtilityExitCodes.ApplicationNotFound"/> in case applicaiton doesn't exists
        /// </returns>
        internal static async Task<NodeAgentSfUtilityExitCodes> GetApplicationDeployedStatusAsync(FabricClient fabricClient,
            Uri applicationName, TimeSpan timeout, CancellationToken cancellationToken)
        {
            ApplicationList appList =
                await fabricClient.QueryManager.GetApplicationListAsync(applicationName, timeout, cancellationToken);

            if (0 == appList.Count)
            {
                return NodeAgentSfUtilityExitCodes.ApplicationNotFound;
            }

            return NodeAgentSfUtilityExitCodes.Success;
        }

        /// <summary>
        /// Updates Windows Update Operation Result
        /// </summary>
        /// <param name="fabricClient">fabric client to carry out operations on service fabric</param>
        /// <param name="applicationUri">Application name of the Patch Orchestration Application</param>
        /// <param name="operationResult">Result of Windows Update operation <see cref="WindowsUpdateOperationResult"/></param>
        /// <param name="timeout">Timeout for the operation</param>
        /// <param name="cancellationToken">Cancellation token to cancel this async operation</param>
        /// <returns>A Task representing the asnyc operation, result of the task would be <see cref="NodeAgentSfUtilityExitCodes"/></returns>
        internal static async Task<NodeAgentSfUtilityExitCodes> UpdateWuOperationResult(
            FabricClient fabricClient,
            Uri applicationUri,
            WindowsUpdateOperationResult operationResult,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            try
            {
                IDataInterface coordinatorServiceDataClient = ServiceProxy.Create<IDataInterface>(new Uri(applicationUri + CoordinatorServiceSuffix));
                await
                    coordinatorServiceDataClient.UpdateWuOperationResult(
                        operationResult,
                        timeout,
                        cancellationToken);
                return NodeAgentSfUtilityExitCodes.Success;
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ErrorMessage(
                    String.Format("CoordinatorServiceHelper.UpdateWuOperationResult failed. Exception details {0}", e));
                if (e is FabricTransientException)
                {
                    return NodeAgentSfUtilityExitCodes.RetryableException;
                }
                else if (e is FabricServiceNotFoundException)
                {
                    return NodeAgentSfUtilityExitCodes.ServiceNotFound;
                }
                else
                {
                    return NodeAgentSfUtilityExitCodes.Failure;
                }
            }
        }
    }
}