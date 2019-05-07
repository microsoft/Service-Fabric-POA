// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.Common
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Different states possible for the Windows Update operation, this would be the basis of state machine for Windows Update operations
    /// </summary>
    public enum NodeAgentSfUtilityExitCodes
    {
        #region FailureExitCodes
        /// <summary>
        /// Operation failed with DllNotFoundException
        /// </summary>
        DllNotFoundException = -12,

        /// <summary>
        /// Application queried wasn't found
        /// </summary>
        ApplicationNotFound = -11,

        /// <summary>
        /// Operation timed out in one of the async operations
        /// </summary>
        TimeoutException = -10,
        
        /// <summary>
        /// Callee can retry the operation again if this exception happens
        /// </summary>
        RetryableException = -9,

        /// <summary>
        /// Process was cancelled or exited externally
        /// </summary>
        ProcessTerminated = -8,
        
        /// <summary>
        /// CoordinatorService wasn't found
        /// </summary>
        ServiceNotFound = -7,

        /// <summary>
        /// Arguments passed to the executable were invalid
        /// </summary>
        InvalidArgument = -6,
        
        /// <summary>
        /// RepairTask was not found or was found in an invalid state
        /// </summary>
        RepairTaskInvalidState = -5,

        /// <summary>
        /// Operation failed, for more details refer to logs
        /// </summary>
        Failure = -1,

        /// <summary>
        /// Operation was successfull
        /// </summary>
        Success = 0,
        #endregion

        #region WuOperationStates
        /// <summary>
        /// None implies, there wasn't an ongoing operation on the node. Possible state transitions
        /// None -> OperationCompleted (in case there were no downloads available), None
        /// </summary>
        None = 1,

        /// <summary>
        /// DownloadCompleted implies, Download operation has completed with success, partial failure, or failure
        /// DownloadCompleted -> InstallationApproved, OperationCompleted (in case RM couldn't approve installation request or there are no updates to install), None (In case repair task is deleted)
        /// </summary>
        DownloadCompleted,

        /// <summary>
        /// InstallationApproved implies, Download operation was completed earlier and RM has approved the installation
        /// InstallationApproved -> InstallationInProgress
        /// </summary>
        InstallationApproved,

        /// <summary>
        /// Installation is in progress, corresponds to RM state of Executing
        /// InstallationInProgress -> InstallationCompleted, InstallationInProgress (in case installation is aborted and more installation still needs to be done)
        /// </summary>
        InstallationInProgress,

        /// <summary>
        /// Installation completed with success, partial success, or failure
        /// InstallationCompleted -> RestartRequested, RestartNotNeeded, OperationCompleted (in case no installations were done)
        /// </summary>
        InstallationCompleted,

        /// <summary>
        /// Restart has been requested and the intention would be marked in repair task
        /// RestartRequested -> RestartCompleted
        /// </summary>
        RestartRequested,

        /// <summary>
        /// No restart was needed after completed of installation
        /// RestartNotNeeded -> OperationCompleted
        /// </summary>
        RestartNotNeeded,

        /// <summary>
        /// Restart completed successfully
        /// RestartCompleted -> OperationCompleted
        /// </summary>
        RestartCompleted,

        /// <summary>
        /// Windows Update operation completed successfully
        /// OperationCompleted -> None
        /// </summary>
        OperationCompleted,

        /// <summary>
        /// Aborting windows update operation
        /// OperationAborted -> OperationCompleted
        /// </summary>
        OperationAborted
        #endregion
    }

    /// <summary>
    /// Health state which is needed to post health reports against HealthManager
    /// </summary>
    public enum HealthState
    {
        Ok = 1,
        Warning = 2,
    }

    /// <summary>
    /// Interface for carrying out operations from NodeAgentUtility
    /// For V1 of the POS service we'll be implementing this interface in both NTService and SFUtility executable
    /// </summary>
    interface INodeAgentSfUtility
    {
        /// <summary>
        /// Gets the state of Windows Update operation
        /// </summary>
        /// <param name="nodeName">Name of current Service Fabric node</param>
        /// <param name="timeout">Timeout for the async operation</param>
        /// <param name="cancellationToken">The cancellation token to cancel the async operation</param>
        /// <returns>A Task representing the asnyc operation, result of the task would be <see cref="NodeAgentSfUtilityExitCodes"/></returns>
        Task<NodeAgentSfUtilityExitCodes> GetWuOperationStateAsync(string nodeName, TimeSpan timeout, CancellationToken cancellationToken);

        /// <summary>
        /// Updates the status of search and download operation
        /// </summary>
        /// <param name="nodeName">Name of current Service Fabric node</param>
        /// <param name="applicationName">Uri of the Patch Orchestration Application.</param>
        /// <param name="updateState">State of Wu operation, possible values are DownloadAvailable, DownloadCompleted, OperationCompleted</param>
        /// <param name="operationResult">result of the search and download operation, can be null in case no results are there to be updated</param>
        /// <param name="installationTimeout">Amount of time a node can undergo installation, during installation node would be in disabled state</param>
        /// <param name="timeout">Timeout for the async operation</param>
        /// <param name="cancellationToken">The cancellation token to cancel the async operation</param>
        /// <returns>A Task representing the asnyc operation, result of the task would be <see cref="NodeAgentSfUtilityExitCodes"/></returns>
        Task<NodeAgentSfUtilityExitCodes> UpdateSearchAndDownloadStatusAsync(string nodeName, Uri applicationName, NodeAgentSfUtilityExitCodes updateState, WindowsUpdateOperationResult operationResult, int installationTimeout, TimeSpan timeout, CancellationToken cancellationToken);

        /// <summary>
        /// Update the status of installation operation
        /// </summary>
        /// <param name="nodeName">Name of current Service Fabric node</param>
        /// <param name="applicationName">Uri of the Patch Orchestration Application.</param>
        /// <param name="updateState">State of Wu operation, possible values are InstallationInProgress, InstallationCompleted, RestartRequested, RestartNotNeeded, OperationCompleted</param>
        /// <param name="operationResult">result of the install operation, can be null in case no results are there to be updated</param>
        /// <param name="timeout">Timeout for the async operation</param>
        /// <param name="cancellationToken">The cancellation token to cancel the async operation</param>
        /// <returns>A Task representing the asnyc operation, result of the task would be <see cref="NodeAgentSfUtilityExitCodes"/></returns>
        Task<NodeAgentSfUtilityExitCodes> UpdateInstallationStatusAsync(string nodeName, Uri applicationName, NodeAgentSfUtilityExitCodes updateState, WindowsUpdateOperationResult operationResult, TimeSpan timeout, CancellationToken cancellationToken);

        /// <summary>
        /// Report health for the NodeAgentService
        /// If windows update operation is not successful after exhausting all reties, we'll post warning level health report
        /// If windows update operation is successfull we'll post Ok level health report.
        /// </summary>
        /// <param name="applicationName">Name of application for constructing service name</param>
        /// <param name="healthProperty">Title for health report. Once the health report is set, any future updates should be done using same healthProperty.</param>
        /// <param name="healthDescription">Description of the health. In case of failure a good description is very helpful for quick mitigation.</param>
        /// <param name="healthState"><see cref="HealthState"/>Indicating the severity of the health report</param>
        /// <param name="timeToLiveInMinutes">Time to live for health report in the health manager in minutes. Default value is -1 indicating infinite time to live, any positive value indicates </param>
        /// <param name="timeout">Timeout for the async operation</param>
        /// <param name="cancellationToken">The cancellation token to cancel the async operation</param>
        /// <returns>Operation result in <see cref="NodeAgentSfUtilityExitCodes"/></returns>
        NodeAgentSfUtilityExitCodes ReportHealth(Uri applicationName, string healthProperty, string healthDescription, HealthState healthState, long timeToLiveInMinutes, TimeSpan timeout, CancellationToken cancellationToken);
        
        /// <summary>
        /// Gets the application status of a deployed application
        /// </summary>
        /// <param name="applicationName">Uri of the application to be queried.</param>
        /// <param name="timeout">Timeout for the async operation</param>
        /// <param name="cancellationToken">The cancellation token to cancel the async operation</param>
        /// <returns>A Task representing the asnyc operation, result of the task would be
        ///  <see cref="NodeAgentSfUtilityExitCodes.Success"/> in case applicaiton exists,
        ///  <see cref="NodeAgentSfUtilityExitCodes.ApplicationNotFound"/> in case applicaiton doesn't exists
        /// </returns>
        Task<NodeAgentSfUtilityExitCodes> GetApplicationDeployedStatusAsync(Uri applicationName, TimeSpan timeout, CancellationToken cancellationToken);
    }
}