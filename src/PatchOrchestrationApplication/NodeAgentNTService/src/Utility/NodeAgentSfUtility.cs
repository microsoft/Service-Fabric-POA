// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentNTService.Utility
{
    using System;
    using System.Collections;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Common;
    using Manager;

    /// <summary>
    /// Utility for carrying out operations on Repair Manager.
    /// For V1 of the POS service we'll be implementing this interface in both NTService and SFUtility executable
    /// </summary>
    class NodeAgentSfUtility : INodeAgentSfUtility
    {
        private readonly ServiceEventSource _eventSource = ServiceEventSource.Current;
        private const string OperationResultFileName = "WindowsUpdateOperationResult.txt";
        private const string SfUtilityFileName = "NodeAgentSFUtility.exe";
        private const string ExecutorDataForNtServiceFileName = "ExecutorDataForNtService.txt";

        private readonly Helper _helper;
        private readonly ServiceSettings _serviceSettings;
        private readonly CancellationToken _cancellationToken;
        private readonly string _nodeName;
        private readonly Uri _applicationUri;
        private readonly SettingsManager _settingsManager;

        public NodeAgentSfUtility(string nodeName, Uri applicationUri, SettingsManager settingsManager, CancellationToken cancellationToken)
        {
            this._helper = new Helper();
            this._settingsManager = settingsManager;
            this._serviceSettings = settingsManager.GetSettings();
            this._cancellationToken = cancellationToken;this._nodeName = nodeName;
            this._applicationUri = applicationUri;
        }

        public ExecutorDataForNtService GetExecutorDataForNtService()
        {
            string executorDataForNtServiceFilePath = Path.Combine(this._settingsManager.WorkFolder, ExecutorDataForNtServiceFileName);
            _eventSource.InfoMessage("Retrieving Executor data from {0}", executorDataForNtServiceFilePath);

            NodeAgentSfUtilityExitCodes exitCode = GetWuOperationState(TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));
            return SerializationUtility.DeserializeFromFile<ExecutorDataForNtService>(executorDataForNtServiceFilePath);
        }

        /// <summary>
        /// Gets the state of Windows Update operation
        /// </summary>
        /// <returns>Windows update operation state.</returns>
        public NodeAgentSfUtilityExitCodes GetWuOperationState(TimeSpan timeout = default(TimeSpan))
        {
            string[] arguments = {"GetWuOperationState", this._nodeName, timeout.TotalSeconds.ToString()};
            ProcessExecutor processExecutor = new ProcessExecutor(SfUtilityFileName, CreateProcessArgument(arguments));

            long retries = 0;
            while (!this._cancellationToken.IsCancellationRequested)
            {
                int state = processExecutor.Execute();
                if (state >= 0 && Enum.IsDefined(typeof(NodeAgentSfUtilityExitCodes), state))
                {
                    return (NodeAgentSfUtilityExitCodes) state;
                }
                if (retries >= this._serviceSettings.WUOperationRetryCount || this._cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                TimeSpan retryDelayTime = TimeSpan.FromMinutes(this._serviceSettings.WUDelayBetweenRetriesInMinutes);
                this._helper.WaitOnTask(Task.Delay(retryDelayTime), this._cancellationToken);
                retries++;            
            }
            throw new Exception("Not able to get valid WuOperationStates.");
        }

        /// <summary>
        /// Updates the status of installation operation
        /// </summary>
        /// <param name="updateState">State of Wu operation, possible values are DownloadAvailable, DownloadCompleted, OperationCompleted</param>
        /// <param name="operationResult">result of the search and download operation, can be null in case no results are there to be updated</param>
        /// <returns>true if operation is success else false.</returns>
        public bool UpdateInstallationStatus(NodeAgentSfUtilityExitCodes updateState, WindowsUpdateOperationResult operationResult = null, TimeSpan timeout = default(TimeSpan))
        {
            _eventSource.InfoMessage("Updating installation status : updateState : {0}, operationResult : {1}, timeout : {2}", updateState, operationResult, timeout);
            this.ReportCurrentNodeStatus(operationResult);
            string filePath = Path.Combine(this._settingsManager.TempFolder , OperationResultFileName);
            string[] arguments;

            if (operationResult != null)
            {
                operationResult.Serialize(filePath);
                arguments = new string[]{ "UpdateInstallationStatus", this._nodeName, this._applicationUri.ToString(), updateState.ToString(), timeout.TotalSeconds.ToString(), filePath };
            }
            else
            {
                arguments = new string[]{ "UpdateInstallationStatus", this._nodeName, this._applicationUri.ToString(), updateState.ToString(), timeout.TotalSeconds.ToString() };
            }
            
            ProcessExecutor processExecutor = new ProcessExecutor(SfUtilityFileName, CreateProcessArgument(arguments));

            long retries = 0;
            while (!this._cancellationToken.IsCancellationRequested)
            {
                if(processExecutor.Execute() == 0)                
                {
                    return true;
                }
                if (retries >= this._serviceSettings.WUOperationRetryCount || this._cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                TimeSpan retryDelayTime = TimeSpan.FromMinutes(this._serviceSettings.WUDelayBetweenRetriesInMinutes);
                this._helper.WaitOnTask(Task.Delay(retryDelayTime), this._cancellationToken);
                retries++;

            }            
            throw new Exception("Not able to update installation status.");            
        }

        /// <summary>
        /// Update the status of Search and Download operation
        /// </summary>
        /// <param name="nodeName">name of service fabric node</param>
        /// <param name="updateState">State of Wu operation, possible values are InstallationInProgress, InstallationCompleted, RestartRequested, RestartNotNeeded, OperationCompleted</param>
        /// <param name="operationResult">result of the install operation, can be null in case no results are there to be updated</param>
        /// <returns>true if operation is success else false.</returns>
        public bool UpdateSearchAndDownloadStatus(NodeAgentSfUtilityExitCodes updateState, WindowsUpdateOperationResult operationResult = null, TimeSpan timeout = default(TimeSpan))
        {            
            _eventSource.InfoMessage("Updating search and download  status : updateState : {0}, operationResult : {1}, timeout : {2}", updateState, operationResult, timeout);
            this.ReportCurrentNodeStatus(operationResult);
            string filePath = Path.Combine(this._settingsManager.TempFolder, OperationResultFileName);
            string[] arguments;

            if (operationResult != null)
            {
                operationResult.Serialize(filePath);
                arguments = new string[]
                    {"UpdateSearchAndDownloadStatus", this._nodeName, this._applicationUri.ToString(), updateState.ToString(), this._serviceSettings.WUOperationTimeOutInMinutes.ToString() ,timeout.TotalSeconds.ToString(), filePath};
            }
            else
            {
                arguments = new string[]
                    {"UpdateSearchAndDownloadStatus", this._nodeName, this._applicationUri.ToString(), updateState.ToString(), this._serviceSettings.WUOperationTimeOutInMinutes.ToString(), timeout.TotalSeconds.ToString()};
            }
            
            ProcessExecutor processExecutor = new ProcessExecutor(SfUtilityFileName, CreateProcessArgument(arguments));

            long retries = 0;
            while (!this._cancellationToken.IsCancellationRequested)
            {
                if (processExecutor.Execute() == 0)
                {
                    return true;
                }
                if (retries >= this._serviceSettings.WUOperationRetryCount || this._cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                TimeSpan retryDelayTime = TimeSpan.FromMinutes(this._serviceSettings.WUDelayBetweenRetriesInMinutes);
                this._helper.WaitOnTask(Task.Delay(retryDelayTime), this._cancellationToken);
                retries++;
            }
            throw new Exception("Not able to update Search/download status.");
        }

        /// <summary>
        /// Posts latest operation status of the current node in the form of health report
        /// </summary>
        /// <returns></returns>
        public void ReportCurrentNodeStatus(WindowsUpdateOperationResult result)
        {
            if (result != null && result.UpdateDetails != null)
            {
                StringBuilder message = new StringBuilder();
                message.AppendFormat("{0} updates were {1} on {2}", result.UpdateDetails.Count,
                    result.OperationType == WindowsUpdateOperationType.SearchAndDownload ? "downloaded" : "installed",
                    result.OperationTime.ToString("dddd, dd MMMM yyyy"));

                foreach (var update in result.UpdateDetails)
                {
                    message.AppendFormat("\nUpdateTitle : {0} , Result = {1}", update.Title, Enum.GetName(typeof(WuOperationResult), update.ResultCode));
                }

                message.AppendFormat("\nFor detailed results refer to https://docs.microsoft.com/azure/service-fabric/service-fabric-patch-orchestration-application#view-the-windows-update-results");

                this.ReportHealth("WindowsUpdateStatus", message.ToString(), HealthState.Ok, -1,
                            TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));
            }
        }

        /// <summary>
        /// Report health for the NodeAgentService.
        /// If windows update operation is not successful after exhausting all reties, we'll post warning level health report
        /// If windows update operation is successful we'll post Ok level health report.
        /// </summary>
        /// <param name="healthProperty">Title for health report. Once the health report is set, any future updates should be done using same healthProperty.</param>
        /// <param name="healthDescription">Description of the health. In case of failure a good description is very helpful for quick mitigation.</param>
        /// <param name="healthState"><see cref="HealthState"/> indicating the severity of the health report, use <see cref="HealthState.Error"/> with caution</param>
        /// <param name="timeToLiveInMinutes">Time to live for health report in the health manager in minutes. Default value is -1 indicating infinite time to live, any positive value indicates </param>
        /// <returns>true if operation is success else false.</returns>
        public NodeAgentSfUtilityExitCodes ReportHealth(string healthProperty, string healthDescription, HealthState healthState, long timeToLiveInMinutes = -1, TimeSpan timeout = default(TimeSpan))
        {
            _eventSource.InfoMessage("reporting health : healthProperty : {0}, healthDescription : {1}, healthState : {2}, timeToLiveInMinutes : {3}, timeout : {4}", healthProperty, healthDescription, healthState, timeToLiveInMinutes, timeout);
            string[] arguments = { "ReportHealth", this._applicationUri.ToString(), healthProperty + " - " + this._nodeName, healthDescription, healthState.ToString(), timeToLiveInMinutes.ToString(), timeout.TotalSeconds.ToString()};
            ProcessExecutor processExecutor = new ProcessExecutor(SfUtilityFileName, CreateProcessArgument(arguments));

            long retries = 0;
            while (!this._cancellationToken.IsCancellationRequested)
            {                
                if (processExecutor.Execute() == (int)NodeAgentSfUtilityExitCodes.Success)
                {
                    return NodeAgentSfUtilityExitCodes.Success;
                }
                if (retries >= this._serviceSettings.WUOperationRetryCount || this._cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                TimeSpan retryDelayTime = TimeSpan.FromMinutes(this._serviceSettings.WUDelayBetweenRetriesInMinutes);
                this._helper.WaitOnTask(Task.Delay(retryDelayTime), this._cancellationToken);
                retries++;
            }

            throw new Exception("Not able to report health.");
        }


        /// <summary>
        /// Report health for the NodeAgentServicePackage.
        /// If windows update operation is not successful after exhausting all reties, we'll post warning level health report
        /// If windows update operation is successful we'll post Ok level health report.
        /// </summary>
        /// <param name="healthProperty">Title for health report. Once the health report is set, any future updates should be done using same healthProperty.</param>
        /// <param name="healthDescription">Description of the health. In case of failure a good description is very helpful for quick mitigation.</param>
        /// <param name="healthState"><see cref="HealthState"/> indicating the severity of the health report, use <see cref="HealthState.Error"/> with caution</param>
        /// <param name="timeToLiveInMinutes">Time to live for health report in the health manager in minutes. Default value is -1 indicating infinite time to live, any positive value indicates </param>
        /// <returns>true if operation is success else false.</returns>
        public NodeAgentSfUtilityExitCodes ReportHealthOnDeployedServicePackage(string healthProperty, string healthDescription, HealthState healthState, long timeToLiveInMinutes = -1, TimeSpan timeout = default(TimeSpan))
        {
            _eventSource.InfoMessage("reporting health : healthProperty : {0}, healthDescription : {1}, healthState : {2}, timeToLiveInMinutes : {3}, timeout : {4}", healthProperty, healthDescription, healthState, timeToLiveInMinutes, timeout);
            string[] arguments = { "ReportHealthOnDeployedServicePackage", this._applicationUri.ToString(), this._nodeName, healthProperty, healthDescription, healthState.ToString(), timeToLiveInMinutes.ToString(), timeout.TotalSeconds.ToString() };
            ProcessExecutor processExecutor = new ProcessExecutor(SfUtilityFileName, CreateProcessArgument(arguments));

            long retries = 0;
            while (!this._cancellationToken.IsCancellationRequested)
            {
                if (processExecutor.Execute() == (int)NodeAgentSfUtilityExitCodes.Success)
                {
                    return NodeAgentSfUtilityExitCodes.Success;
                }
                if (retries >= this._serviceSettings.WUOperationRetryCount || this._cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                TimeSpan retryDelayTime = TimeSpan.FromMinutes(this._serviceSettings.WUDelayBetweenRetriesInMinutes);
                this._helper.WaitOnTask(Task.Delay(retryDelayTime), this._cancellationToken);
                retries++;
            }

            throw new Exception("Not able to report health.");
        }

        public bool GetApplicationDeployedStatus(TimeSpan timeout)
        {
            _eventSource.InfoMessage("Getting Application deployed status for application : {0}", this._applicationUri.ToString());
            string[] arguments = { "GetApplicationDeployedStatus", this._applicationUri.ToString(), timeout.TotalSeconds.ToString() };
            ProcessExecutor processExecutor = new ProcessExecutor(SfUtilityFileName, CreateProcessArgument(arguments));

            long retries = 0;
            while (!this._cancellationToken.IsCancellationRequested)
            {
                int exitCode = processExecutor.Execute();
                if (exitCode == (int)NodeAgentSfUtilityExitCodes.Success)
                {
                    return true;
                }
                if (exitCode == (int)NodeAgentSfUtilityExitCodes.ApplicationNotFound)
                {
                    return false;
                }
                if (exitCode == (int)NodeAgentSfUtilityExitCodes.DllNotFoundException)
                {
                    // RDBUG 9845931 - For a rare case Fabric.Code path might not be set by SF yet.
                    // Best course of action in that situation is to refresh the environment variables once fabric comes up, hence restart this service once SF has come up.
                    StringBuilder stringBuilder = new StringBuilder("Exiting the service to fetch new environment variables. Current environment variables are : ");
                    foreach (DictionaryEntry envVariable in Environment.GetEnvironmentVariables())
                    {
                        stringBuilder.AppendFormat("{0}={1} , ", envVariable.Key, envVariable.Value);
                    }

                    _eventSource.WarningMessage(stringBuilder.ToString());
                    // Wait for 5 seconds before exit
                    Task.Delay(5000).Wait();
                    Environment.Exit(10);
                }

                if (retries >= this._serviceSettings.WUOperationRetryCount || this._cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                TimeSpan retryDelayTime = TimeSpan.FromMinutes(this._serviceSettings.WUDelayBetweenRetriesInMinutes);
                this._helper.WaitOnTask(Task.Delay(retryDelayTime), this._cancellationToken);
                retries++;
            }

            throw new Exception("Not able to get application status.");
        }        

        public Task<NodeAgentSfUtilityExitCodes> GetWuOperationStateAsync(string nodeName, TimeSpan timeout, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<NodeAgentSfUtilityExitCodes> UpdateSearchAndDownloadStatusAsync(string nodeName, Uri applicationName, NodeAgentSfUtilityExitCodes updateState,
            WindowsUpdateOperationResult operationResult, int installationTimeout, TimeSpan timeout, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<NodeAgentSfUtilityExitCodes> UpdateInstallationStatusAsync(string nodeName, Uri applicationName, NodeAgentSfUtilityExitCodes updateState,
            WindowsUpdateOperationResult operationResult, TimeSpan timeout, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<NodeAgentSfUtilityExitCodes> GetApplicationDeployedStatusAsync(Uri applicationName, TimeSpan timeout, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public NodeAgentSfUtilityExitCodes ReportHealth(Uri applicationName, string healthProperty, string healthDescription, HealthState healthState, long timeToLiveInMinutes, TimeSpan timeout, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private string CreateProcessArgument(string[] arguments)
        {
            return '"' + String.Join("\" \"", arguments) + '"';
        }

        public NodeAgentSfUtilityExitCodes ReportHealthOnDeployedServicePackage(Uri applicationName, string nodeName, string healthProperty, string healthDescription, HealthState healthState, long timeToLiveInMinutes, TimeSpan timeout, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }

    class ProcessExecutor
    {        
        private readonly Process _process;
        private readonly ServiceEventSource _eventSource = ServiceEventSource.Current;

        public ProcessExecutor(string fileName, string arguments = null, bool runAsAdmin = false)
        {
            _process = new Process();
            _process.StartInfo.FileName = fileName;
            if (arguments != null)
            {
                _process.StartInfo.Arguments = arguments;
            }
            if (runAsAdmin)
            {
                _process.StartInfo.Verb = "runas";
            }
            _process.StartInfo.CreateNoWindow = true;
            _process.StartInfo.UseShellExecute = false;
            _eventSource.InfoMessage("Process executing : {0} {1}", fileName, arguments);
        }        

        public int Execute()
        {
            _process.Start();
            _process.WaitForExit();            
            int exitCode =_process.ExitCode;
            _eventSource.InfoMessage("Process completed with exit code : {0}", exitCode);
            return exitCode;
        }

        public void ExecuteAsync()
        {
            _process.Start();
        }
    }
}
