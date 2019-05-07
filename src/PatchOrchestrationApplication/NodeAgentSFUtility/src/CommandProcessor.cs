// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentSFUtility
{
    using System;
    using Common;
    using Helpers;
    using System.IO;
    using System.Linq;
    using System.Fabric;
    using System.Fabric.Repair;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Utility for executing the commands which were specified in commandline parameter 
    /// </summary>
    public class CommandProcessor : INodeAgentSfUtility
    {
        private readonly FabricClient fabricClient;

        /// <summary>
        /// Cancellation token which can be used to stop any ongoing async operations of command processor
        /// </summary>
        private readonly CancellationTokenSource cancellationTokenSource;

        private const string ExecutorDataForNtServiceFileName = "ExecutorDataForNtService.txt";
        private const string ServiceNameSuffix = "/NodeAgentService";

        /// <summary>
        /// Constructor for Command Processor
        /// </summary>
        /// <param name="fabricClient">Fabric client object used for carrying out service fabric client requests</param>
        /// <param name="serviceEventSource">Eventsource used for logging</param>
        public CommandProcessor(FabricClient fabricClient, IPatchOrchestrationEvents serviceEventSource)
        {
            this.fabricClient = fabricClient;
            ServiceEventSource.Current = serviceEventSource;
            this.cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Processes arguments recieved from commandline
        /// </summary>
        /// <param name="args">Array of arguments</param>
        /// <para>Arguments specified in the commandline are case sensitive, following commands are supported
        /// Format -- CommandName list_of_arguments 
        /// Eg: Command argument1 argument2 argument3, Following are the allowed commands and their template
        /// GetWuOperationState nodeName TimeoutInSeconds
        /// UpdateSearchAndDownloadStatus nodeName applicationName WuOperationStates InstallationTimeoutInMinutes TimeoutInSeconds ResultFilePath
        /// UpdateInstallationStatus nodeName applicationName WuOperationStates TimeoutInSeconds ResultFilePath
        /// ReportHealth applicationName title description HealthState TimeToLiveInMinutes TimeoutInSeconds
        /// GetApplicationDeployedStatus applicationUri TimeoutInSeconds
        /// </para>
        public async Task<NodeAgentSfUtilityExitCodes> ProcessArguments(String[] args)
        {
            if (!args.Any())
                return NodeAgentSfUtilityExitCodes.InvalidArgument;

            switch (args[0])
            {
                case "GetWuOperationState":
                    return await this.GetWuOperationStateAsync(args[1], TimeSpan.FromSeconds(int.Parse(args[2])), this.cancellationTokenSource.Token);

                case "UpdateSearchAndDownloadStatus":
                    return
                        await
                         this.UpdateSearchAndDownloadStatusAsync(
                             args[1],
                             new Uri(args[2]),
                             (NodeAgentSfUtilityExitCodes)Enum.Parse(typeof(NodeAgentSfUtilityExitCodes), (args[3])),
                             (args.Count() == 7) ? WindowsUpdateOperationResult.Deserialize(args[6]) : null,
                             int.Parse(args[4]), 
                             new TimeSpan(0, 0, int.Parse(args[5])),
                             this.cancellationTokenSource.Token);

                case "UpdateInstallationStatus":
                    return
                        await
                         this.UpdateInstallationStatusAsync(
                             args[1],
                             new Uri(args[2]), 
                             (NodeAgentSfUtilityExitCodes)Enum.Parse(typeof(NodeAgentSfUtilityExitCodes), (args[3])),
                             (args.Count() == 6) ? WindowsUpdateOperationResult.Deserialize(args[5]) : null,
                             new TimeSpan(0, 0, int.Parse(args[4])),
                             this.cancellationTokenSource.Token);

                case "ReportHealth":
                    return
                        this.ReportHealth(
                            new Uri(args[1]),
                            args[2],
                            args[3],
                            (HealthState)Enum.Parse(typeof(HealthState), (args[4])),
                            long.Parse(args[5]),
                            TimeSpan.FromSeconds(int.Parse(args[6])),
                            this.cancellationTokenSource.Token);

                case "GetApplicationDeployedStatus":
                    return
                        await this.GetApplicationDeployedStatusAsync(new Uri(args[1]), TimeSpan.FromSeconds(int.Parse(args[2])),
                            this.cancellationTokenSource.Token);

                default:
                {
                    string errorMessage = String.Format("Unknown command = {0} recieved", args[0]);
                    ServiceEventSource.Current.ErrorMessage(errorMessage);
                    return NodeAgentSfUtilityExitCodes.InvalidArgument;
                }
            }
        }

        /// <summary>
        /// Gets the state of Windows Update operation using the state stored in RepairTask
        /// </summary>
        /// <param name="nodeName">Name of current Service Fabric node</param>
        /// <param name="timeout">Timeout for the async operation</param>
        /// <param name="cancellationToken">The cancellation token to cancel the async operation</param>
        /// <returns>A Task representing the asnyc operation, result of the task would be <see cref="NodeAgentSfUtilityExitCodes"/></returns>
        public async Task<NodeAgentSfUtilityExitCodes> GetWuOperationStateAsync(String nodeName, TimeSpan timeout, CancellationToken cancellationToken)
        {
            RepairTask repairTask;

            try
            {
                repairTask =
                    await
                    RepairManagerHelper.GetRepairTaskForNode(
                        this.fabricClient,
                        nodeName,
                        timeout,
                        cancellationToken);
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ErrorMessage(
                    String.Format("RepairManagerHelper.GetRepairTaskForNode failed. Exception details {0}", e));
                if (e is FabricTransientException)
                {
                    return NodeAgentSfUtilityExitCodes.RetryableException;
                }
                else
                {
                    return NodeAgentSfUtilityExitCodes.Failure;
                }
            }
            
            if (null == repairTask)
            {
                ServiceEventSource.Current.VerboseMessage(String.Format("No repair task found for this node, Operation State = {0}", NodeAgentSfUtilityExitCodes.None));
                return NodeAgentSfUtilityExitCodes.None;
            }

            NodeAgentSfUtilityExitCodes resultState;
            ExecutorDataForRmTask executorData = SerializationUtility.Deserialize<ExecutorDataForRmTask>(repairTask.ExecutorData);
            ExecutorDataForNtService executorDataForNtService = new ExecutorDataForNtService() { ApprovedDateTime = repairTask.ApprovedTimestamp, ExecutorTimeoutInMinutes = executorData.ExecutorTimeoutInMinutes};

            string workFolder = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            string executorDataForNtServiceFilePath = Path.Combine(workFolder, ExecutorDataForNtServiceFileName);
            SerializationUtility.Serialize(executorDataForNtServiceFilePath, executorDataForNtService);

            switch (repairTask.State)
            {
                case RepairTaskState.Claimed:
                case RepairTaskState.Preparing:
                    resultState = NodeAgentSfUtilityExitCodes.DownloadCompleted;
                    break;
                case RepairTaskState.Approved:
                    resultState = NodeAgentSfUtilityExitCodes.InstallationApproved;
                    break;
                case RepairTaskState.Executing:
                {
                    resultState = executorData.ExecutorSubState;
                    if (resultState == NodeAgentSfUtilityExitCodes.RestartRequested)
                    {
                        if (this.GetRestartStatus(executorData.RestartRequestedTime))
                        {
                            string resultDetails =
                                "Installation of the updates completed, Restart post installation completed successfully";
                            resultState = await RepairManagerHelper.UpdateRepairTask(this.fabricClient, nodeName,
                                RepairTaskState.Executing, RepairTaskResult.Pending,
                                resultDetails, NodeAgentSfUtilityExitCodes.RestartCompleted, timeout,
                                cancellationToken);

                            if (resultState == NodeAgentSfUtilityExitCodes.Success)
                            {
                                resultState = NodeAgentSfUtilityExitCodes.RestartCompleted;
                            }
                            else
                            {
                                ServiceEventSource.Current.ErrorMessage(
                                    String.Format("Post restart, update of Repair task failed with {0}", resultState));
                                resultState = NodeAgentSfUtilityExitCodes.RetryableException;
                            }
                        }
                    }

                    break;
                }

                case RepairTaskState.Completed:
                case RepairTaskState.Restoring:
                {
                    resultState = NodeAgentSfUtilityExitCodes.OperationCompleted;
                    break;
                }

                default:
                {
                    ServiceEventSource.Current.ErrorMessage(String.Format("Repair task for current node in unexpected state {0}", repairTask.State));
                    resultState = NodeAgentSfUtilityExitCodes.RepairTaskInvalidState;
                    break;
                }
            }

            ServiceEventSource.Current.InfoMessage("GetWuOperationStateAsync returned {0}", resultState);
            return resultState;
        }

        /// <summary>
        /// Updates the status of search and download operation in CoordinatorService's repliable store
        /// And updates the RepairTask with appropriate state as per the updateState provided
        /// </summary>
        /// <param name="nodeName">Name of current service fabric node</param>
        /// <param name="applicationName">Uri of the Patch Orchestration Application.</param>
        /// <param name="updateState">State of Wu operation, possible values are DownloadAvailable, DownloadCompleted, OperationCompleted</param>
        /// <param name="operationResult">result of the search and download operation, cannot be null</param>
        /// <param name="installationTimeout">Amount of time a node can undergo installation, during installation node would be in disabled state</param>
        /// <param name="timeout">Timeout for the async operation</param>
        /// <param name="cancellationToken">The cancellation token to cancel the async operation</param>
        /// <returns>Task containing result of operation, true for success, false for failure</returns>
        /// <returns>
        /// A Task representing the asnyc operation, result of the task would be <see cref="NodeAgentSfUtilityExitCodes.Success"/> in case of success
        /// Any other <see cref="NodeAgentSfUtilityExitCodes"/> in case of failure
        /// </returns>
        public async Task<NodeAgentSfUtilityExitCodes> UpdateSearchAndDownloadStatusAsync(String nodeName, Uri applicationName,
            NodeAgentSfUtilityExitCodes updateState,WindowsUpdateOperationResult operationResult, int installationTimeout, TimeSpan timeout, CancellationToken cancellationToken)
        {
            String taskDescription = null;
            String resultDetails = null;
            NodeAgentSfUtilityExitCodes result;
            ExecutorDataForRmTask executorData = new ExecutorDataForRmTask()
            {
                ExecutorSubState = updateState,
                ExecutorTimeoutInMinutes = installationTimeout
            };

            if (null != operationResult)
            {
                int succeededOperations;
                int abortedOperations;
                int totalOperations;

                this.GetWuOperationResultCount(operationResult, out totalOperations, out abortedOperations,
                    out succeededOperations);

                result = await
                    CoordinatorServiceHelper.UpdateWuOperationResult(
                        this.fabricClient,
                        applicationName,
                        operationResult,
                        timeout,
                        cancellationToken);

                if (result != NodeAgentSfUtilityExitCodes.Success)
                {
                    return result;
                }
                
                taskDescription =
                    String.Format(
                        "{0} updates successfully downloaded on {1}. Creating this repair task to install the downloaded updates",
                        succeededOperations, operationResult.OperationTime);
                resultDetails =
                    String.Format("{0} updates searched. {1} downloaded successfully, {2} downloads were aborted",
                        operationResult.UpdateDetails.Count, succeededOperations, abortedOperations);
            }

            switch (updateState)
            {
                case NodeAgentSfUtilityExitCodes.DownloadCompleted:
                {
                    result = await
                        RepairManagerHelper.CreateRepairTaskForNode(
                            this.fabricClient,
                            nodeName,
                            taskDescription,
                            resultDetails,
                            executorData,
                            timeout,
                            cancellationToken);
                    break;
                }

                case NodeAgentSfUtilityExitCodes.OperationCompleted:
                {
                    result = NodeAgentSfUtilityExitCodes.Success;
                    break;
                }

                case NodeAgentSfUtilityExitCodes.OperationAborted:
                {
                        ServiceEventSource.Current.InfoMessage(String.Format("Operation aborted for a claimed task"));
                        result = await RepairManagerHelper.UpdateRepairTask(this.fabricClient, nodeName,
                            RepairTaskState.Completed, RepairTaskResult.Failed,
                            "Aborting the operation", updateState, timeout, cancellationToken);
                        break;
                }

                default:
                {
                    ServiceEventSource.Current.ErrorMessage(
                        String.Format("UpdateSearchAndDownloadStatusAsync called with invalid state {0}", updateState));
                    result = NodeAgentSfUtilityExitCodes.InvalidArgument;
                    break;
                }
            }

            ServiceEventSource.Current.InfoMessage("UpdateSearchAndDownloadStatusAsync result = {0}", result);
            return result;
        }

        /// <summary>
        /// Update the status of installation operation in Coordinator Service's Relaible store
        /// Also update the status of repair task as per the updateState provided
        /// </summary>
        /// <param name="nodeName">Name of the service fabric node</param>
        /// <param name="applicationName">Uri of the Patch Orchestration Application.</param>
        /// <param name="updateState">State of Wu operation, possible values are InstallationInProgress, InstallationCompleted, RestartRequested, RestartNotNeeded, OperationCompleted</param>
        /// <param name="operationResult">result of the install operation, can be null in case no results are there to be updated</param>
        /// <param name="timeout">Timeout for the async operation</param>
        /// <param name="cancellationToken">The cancellation token to cancel the async operation</param>
        /// <returns>
        /// A Task representing the asnyc operation, result of the task would be <see cref="NodeAgentSfUtilityExitCodes.Success"/> in case of success
        /// Any other <see cref="NodeAgentSfUtilityExitCodes"/> in case of failure
        /// </returns>
        public async Task<NodeAgentSfUtilityExitCodes> UpdateInstallationStatusAsync(String nodeName, Uri applicationName, 
            NodeAgentSfUtilityExitCodes updateState, WindowsUpdateOperationResult operationResult,
            TimeSpan timeout, CancellationToken cancellationToken)
        {
            NodeAgentSfUtilityExitCodes result;
            String resultDetails = null;

            if (operationResult != null)
            {
                int succeededOperations;
                int abortedOperations;
                int totalOperations;
                this.GetWuOperationResultCount(operationResult,out totalOperations,out abortedOperations,out succeededOperations);

                result =
                    await
                    CoordinatorServiceHelper.UpdateWuOperationResult(
                        this.fabricClient,
                        applicationName,
                        operationResult,
                        timeout,
                        cancellationToken);
                if (result != NodeAgentSfUtilityExitCodes.Success)
                {
                    return result;
                }

                resultDetails =
                    String.Format(
                        "{0} out of {1} updates were installed successfully, {2} were aborted",
                        succeededOperations,
                        operationResult.UpdateDetails.Count,
                        abortedOperations);
            }

            switch (updateState)
            {
                case NodeAgentSfUtilityExitCodes.InstallationCompleted:
                {
                    result =
                        await
                        RepairManagerHelper.UpdateRepairTask(
                            this.fabricClient,
                            nodeName,
                            RepairTaskState.Executing,
                            RepairTaskResult.Pending,
                            resultDetails,
                            NodeAgentSfUtilityExitCodes.InstallationCompleted,
                            timeout,
                            cancellationToken);
                    break;
                }

                case NodeAgentSfUtilityExitCodes.InstallationInProgress:
                {
                    result = await
                        RepairManagerHelper.UpdateRepairTask(this.fabricClient, nodeName,
                            RepairTaskState.Executing, RepairTaskResult.Pending, resultDetails, NodeAgentSfUtilityExitCodes.InstallationInProgress, timeout,
                            cancellationToken);
                    break;
                }

                case NodeAgentSfUtilityExitCodes.RestartRequested:
                {
                    resultDetails = "Installation of the updates completed, Restart pending";
                    result = await RepairManagerHelper.UpdateRepairTask(this.fabricClient, nodeName,
                        RepairTaskState.Executing, RepairTaskResult.Pending,
                        resultDetails, NodeAgentSfUtilityExitCodes.RestartRequested, timeout, cancellationToken, DateTime.UtcNow);
                    break;
                }

                case NodeAgentSfUtilityExitCodes.RestartNotNeeded:
                {
                    resultDetails = "Installation of the updates completed, Restart not needed";
                    result = await RepairManagerHelper.UpdateRepairTask(this.fabricClient, nodeName,
                        RepairTaskState.Executing, RepairTaskResult.Pending,
                        resultDetails, NodeAgentSfUtilityExitCodes.RestartNotNeeded, timeout, cancellationToken);
                    break;
                }

                case NodeAgentSfUtilityExitCodes.RestartCompleted:
                {
                    resultDetails = "Installation of the updates completed, Restart post installation completed successfully";
                    result = await RepairManagerHelper.UpdateRepairTask(this.fabricClient, nodeName,
                        RepairTaskState.Executing, RepairTaskResult.Pending,
                        resultDetails, NodeAgentSfUtilityExitCodes.RestartCompleted, timeout, cancellationToken);
                    break;
                }

                case NodeAgentSfUtilityExitCodes.OperationCompleted:
                {
                    ServiceEventSource.Current.InfoMessage(String.Format("Mark the operation as completed"));
                    result = await RepairManagerHelper.UpdateRepairTask(this.fabricClient, nodeName,
                        RepairTaskState.Restoring, RepairTaskResult.Succeeded,
                        resultDetails, NodeAgentSfUtilityExitCodes.OperationCompleted, timeout, cancellationToken);
                    break;
                }

                default:
                {
                    ServiceEventSource.Current.ErrorMessage(String.Format("UpdateInstallationStatusAsync called with invalid state {0}", updateState));
                    result = NodeAgentSfUtilityExitCodes.InvalidArgument;
                    break;
                }
            }

            ServiceEventSource.Current.InfoMessage("UpdateInstallationStatusAsync result = {0}", result);
            return result;
        }

        /// <summary>
        /// Utility to Report health for the NodeAgentService. Typical usecases are as below
        /// If windows update operation is not successful after exhausting all reties, user can post warning level health report
        /// If windows update operation is successfull, user can post Ok level health report.
        /// </summary>
        /// <param name="applicationName">Application name for constructing the servicename</param>
        /// <param name="healthProperty">Title for health report. Once the health report is set, any future updates should be done using same healthProperty.</param>
        /// <param name="healthDescription">Description of the health. In case of failure a good description is very helpful for quick mitigation.</param>
        /// <param name="healthState"><see cref="HealthState"/>Indicating the severity of the health report</param>
        /// <param name="timeToLiveInMinutes">Time to live for health report in the health manager in minutes. Default value is -1 indicating infinite time to live, any positive value indicates </param>
        /// <param name="timeout">Timeout for the async operation</param>
        /// <param name="cancellationToken">Cancellation token to cancel this async operation</param>
        /// <returns>Operation result in <see cref="NodeAgentSfUtilityExitCodes"/></returns>
        public NodeAgentSfUtilityExitCodes ReportHealth(Uri applicationName, String healthProperty, String healthDescription, HealthState healthState,
            long timeToLiveInMinutes, TimeSpan timeout, CancellationToken cancellationToken)
        {
            NodeAgentSfUtilityExitCodes result = HealthManagerHelper.PostServiceHealthReport(this.fabricClient, applicationName, ServiceNameSuffix, healthProperty, healthDescription, (System.Fabric.Health.HealthState)healthState, timeout, timeToLiveInMinutes);
            ServiceEventSource.Current.InfoMessage("CommandProcessor.ReportHealth returned {0}", result);
            return result;
        }

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
        public async Task<NodeAgentSfUtilityExitCodes> GetApplicationDeployedStatusAsync(Uri applicationName,
            TimeSpan timeout, CancellationToken cancellationToken)
        {
            NodeAgentSfUtilityExitCodes result = await
                    CoordinatorServiceHelper.GetApplicationDeployedStatusAsync(this.fabricClient, applicationName,
                        timeout, cancellationToken);
            ServiceEventSource.Current.InfoMessage("CommandProcessor.GetApplicationDeployedStatusAsync returned {0}", result);
            return result;
        }

        /// <summary>
        /// Logic to compute if Restart has happened since last requested time
        /// </summary>
        /// <param name="lastRequestedTime">Reference time which needs to be checked against system uptime</param>
        /// <returns>true in case restart has happened since last requested time, otherwise false</returns>
        public bool GetRestartStatus(DateTime lastRequestedTime)
        {
            return ((DateTime.UtcNow - this.GetSystemUptime()) >= lastRequestedTime);
        }

        /// <summary>
        /// Gets System Uptime for the current machine
        /// </summary>
        /// <returns>Timespan indicating how much time system is up</returns>
        public TimeSpan GetSystemUptime()
        {
            return TimeSpan.FromMilliseconds(GetTickCount64());
        }

        [DllImport("kernel32")]
        extern static UInt64 GetTickCount64();

        private void GetWuOperationResultCount(WindowsUpdateOperationResult operationResult, out int totalOperations, out int abortedOperations, out int succeededOperations)
        {
            totalOperations = 0;
            abortedOperations = 0;
            succeededOperations = 0;

            if (operationResult == null)
                return;

            totalOperations = operationResult.UpdateDetails.Count;
            foreach (var updateDetail in operationResult.UpdateDetails)
            {
                switch (updateDetail.ResultCode)
                {
                    case WuOperationResult.Aborted:
                        abortedOperations++;
                        break;
                    case WuOperationResult.Succeeded:
                        succeededOperations++;
                        break;
                }
            }
        }
    }
}
