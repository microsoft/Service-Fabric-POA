// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentNTService.Manager
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using ServiceFabric.PatchOrchestration.Common;
    using Utility;
    using WUApiLib;

    using System.ComponentModel;
    using System.IO;

    /// <summary>
    /// Manages the search, download and installation of Windows updates. It also takes care of restarting the system, if required after installation of updates.
    /// </summary>
    class WindowsUpdateManager
    {
        private readonly ServiceEventSource _eventSource = ServiceEventSource.Current;
        private readonly OperationResultFormatter _operationResultFormatter;
        private readonly Helper _helper;
        private readonly CancellationToken _cancellationToken;
        private readonly NodeAgentSfUtility _nodeAgentSfUtility;

        private SettingsManager _settingsManager;
        private ServiceSettings _serviceSettings;
        private UpdateSession _uSession;        
        private WUCollectionWrapper _wuCollectionWrapper;
        private Task<bool> _task;
        private const string WUOperationStatus = "WUOperationStatus";
        private const string LastUpdateOperationStartTimeStampFile = "LastUpdateOperationStartTimeStampFile.txt";
        private DateTime lastUpdateOperationStartTimeStamp;

        /// <summary>
        /// Initializes the update manager.
        /// </summary>        
        public WindowsUpdateManager(OperationResultFormatter operationResultFormatter, NodeAgentSfUtility nodeAgentSfUtility, SettingsManager settingsManager, CancellationToken cancellationToken)
        {
            this._uSession = new UpdateSession();
            this._operationResultFormatter = operationResultFormatter;
            this._helper = new Helper();
            this._settingsManager = settingsManager;
            this._task = null;
            this._cancellationToken = cancellationToken;
            this._nodeAgentSfUtility = nodeAgentSfUtility;
            this.ResetManager();
            this.lastUpdateOperationStartTimeStamp = this.ReadLastOperationStartTimeStamp();
        }

        /// <summary>
        /// Reset Windows Update Manager.
        /// </summary>
        public void ResetManager()
        {
            this._serviceSettings = _settingsManager.GetSettings();            
            this._wuCollectionWrapper = null;
            _eventSource.Message("Resetting windows update manager.");
        }

        /// <summary>
        /// Starts search, download and installation of Windows Updates.
        /// </summary>
        public bool StartUpdate()
        {
            this._task = new Task<bool>(() => StartUpdateUtil(this._cancellationToken));
            this._task.Start();

            this._task.Wait();

            bool rescheduleNeeded = this._task.Result;
            _eventSource.InfoMessage("Windows update ended with flag rescheduleNeeded : {0}", rescheduleNeeded);
            if (this._task != null)
            {
                this._task.Dispose();
                this._task = null;
            }

            return rescheduleNeeded;
        }

        /// <summary>
        /// Check if System reboot is required or not.
        /// </summary>
        /// <returns></returns>
        public bool RebootRequired()
        {            
            SystemInformation systemInformation = new SystemInformation();
            if (systemInformation.RebootRequired)
            {
                _eventSource.InfoMessage("Reboot required.");
                return true;
            }
            _eventSource.InfoMessage("Reboot not required.");
            return false;
        }

        /// <summary>
        /// Resets the state machine. 
        /// </summary>
        /// <returns></returns>
        public bool ResetStateMachine()
        {
            _eventSource.InfoMessage("Resetting State Machine.");
            TimeSpan utilityTaskTimeOut = TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes);
            try
            {
                NodeAgentSfUtilityExitCodes wuOperationState =
                    this._nodeAgentSfUtility.GetWuOperationState(utilityTaskTimeOut);
                switch (wuOperationState)
                {
                    case NodeAgentSfUtilityExitCodes.None:
                        break;
                    case NodeAgentSfUtilityExitCodes.DownloadCompleted:
                        this._nodeAgentSfUtility.UpdateSearchAndDownloadStatus(NodeAgentSfUtilityExitCodes.OperationAborted, null, utilityTaskTimeOut);
                        break;
                    default:
                        this._nodeAgentSfUtility.UpdateInstallationStatus(NodeAgentSfUtilityExitCodes.OperationCompleted, null, utilityTaskTimeOut);
                        break;
                }
                return true;
            }
            catch (Exception e)
            {
                _eventSource.ErrorMessage("Not able to reset state machine. exception : {0}",e);
                return false;
            }
        }

        /// <summary>UpdateInstallationStatus
        /// Restart System if after installation restart is required.
        /// </summary>
        public void HandleRestart()
        {            
            TimeSpan utilityTaskTimeOut = TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes);

            NodeAgentSfUtilityExitCodes wuOperationState = this._nodeAgentSfUtility.GetWuOperationState(utilityTaskTimeOut);
            _eventSource.InfoMessage("Handling restart. Current WU Operation State : {0}.", wuOperationState);
            string systemRestartDescription = "Installation attempted, now trying to restart the node.";

            switch (wuOperationState)
            {
                case NodeAgentSfUtilityExitCodes.InstallationCompleted:
                    this._nodeAgentSfUtility.UpdateInstallationStatus(NodeAgentSfUtilityExitCodes.RestartRequested, null, utilityTaskTimeOut);
                    this._nodeAgentSfUtility.ReportHealth(WUOperationStatus, systemRestartDescription, HealthState.Ok, -1, TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));
                    this.RestartSystem();
                    break;

                case NodeAgentSfUtilityExitCodes.RestartRequested:
                    this._nodeAgentSfUtility.ReportHealth(WUOperationStatus, systemRestartDescription, HealthState.Ok, -1, TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));
                    this.RestartSystem();
                    break;
                case NodeAgentSfUtilityExitCodes.OperationCompleted:
                    break;
                default:
                    _eventSource.ErrorMessage("Invalid state : {0}", wuOperationState);
                    break;
            }
        }

        /// <summary>
        /// This will stop the execution of update task (if running). 
        /// </summary>
        public void Stop()
        {
            _eventSource.Message("Cancellation requested.");            
            if (this._task != null)
            {
                this._task.Wait();
                this._task.Dispose();
                this._task = null;
            }
            _eventSource.Message("Windows Update Manager stopped.");
        }

        /// <summary>
        /// Clean up the resources.
        /// </summary>
        public void Dispose()
        {
            this._settingsManager = null;            
            this._wuCollectionWrapper = null;
            this._uSession = null;            
            _eventSource.Message("Windows Update Manager disposed.");
        }

        /// <summary>
        /// Changes the state machine state to Completed.
        /// </summary>
        public void CompleteWUOperations()
        {
            TimeSpan utilityTaskTimeOut = TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes);
            NodeAgentSfUtilityExitCodes wuOperationState = this._nodeAgentSfUtility.GetWuOperationState(utilityTaskTimeOut);
            _eventSource.InfoMessage("Completing Wu Operation. Current state: {0}", wuOperationState);

            switch (wuOperationState)
            {
                case NodeAgentSfUtilityExitCodes.InstallationCompleted:
                case NodeAgentSfUtilityExitCodes.RestartNotNeeded:
                case NodeAgentSfUtilityExitCodes.RestartCompleted:
                    this._nodeAgentSfUtility.UpdateInstallationStatus(
                        NodeAgentSfUtilityExitCodes.OperationCompleted,
                        null,
                        utilityTaskTimeOut);
                    break;
                case NodeAgentSfUtilityExitCodes.OperationCompleted:
                    break;
                default:
                    _eventSource.WarningMessage("Invalid WU state : " +  wuOperationState);
                    break;
            }
        }

        /// <summary>
        /// This function tries to stop the Fabric services explicitly before shutting down machine.
        /// </summary>
        private void StopFabricServices()
        {
            // For one-box clusters fabricinstallersvc does not exist
            if (WindowsServiceUtility.CheckNtServiceExists(WindowsServiceUtility.FabricInstallerSvcName))
            {
                WindowsServiceUtility.StopNtService(WindowsServiceUtility.FabricInstallerSvcName, _eventSource);
            }

            WindowsServiceUtility.StopNtService(WindowsServiceUtility.FabricHostSvcName, _eventSource);
        }

        private DateTime ReadLastOperationStartTimeStamp()
        {
            string LastUpdateOperationStartTimeStampFilePath = this.GetLastOperationStartTimeStampFilePath();
            if (File.Exists(LastUpdateOperationStartTimeStampFilePath))
            {
                try
                {
                    string text = File.ReadAllText(LastUpdateOperationStartTimeStampFilePath).Trim();
                    return DateTime.ParseExact(text, "yyyyMMddHHmmss", null);
                }
                catch(Exception ex)
                {
                    _eventSource.WarningMessage(string.Format("LastOperationStartTimeStamp parsing failed with execption : ex {0}", ex.ToString()));
                }
            }
            return default(DateTime);
        }

        private void UpdateLastOperationStartTimeStamp(DateTime timeStamp)
        {
            try
            {
                this.WriteLastOperationStartTimeStamp(timeStamp);
            }
            catch(Exception ex)
            {
                this._eventSource.WarningMessage(string.Format("WriteLastOperationStartTimeStamp failed with exception {0}", ex.ToString()));
                return;
            }
            this.lastUpdateOperationStartTimeStamp = timeStamp;
        }

        private void WriteLastOperationStartTimeStamp(DateTime timeStamp)
        {
            string randomFilePath = Path.Combine(this._settingsManager.TempFolder, Path.GetRandomFileName());
            if(File.Exists(randomFilePath))
            {
                File.Delete(randomFilePath);
            }

            using (FileStream fs = File.Create(randomFilePath))
            {
                string text = timeStamp.ToString("yyyyMMddHHmmss");
                Byte[] info = new System.Text.UTF8Encoding(true).GetBytes(text);
                fs.Write(info, 0, info.Length);
            }

            string lastOperationStartTimeStampFilePath = this.GetLastOperationStartTimeStampFilePath();

            if (File.Exists(lastOperationStartTimeStampFilePath))
            {
                File.Delete(lastOperationStartTimeStampFilePath);
            }
            File.Move(randomFilePath, lastOperationStartTimeStampFilePath);
            File.Delete(randomFilePath);
            _eventSource.InfoMessage("LastOperationStartTimeStampFile written with timeStamp : {0}", this.lastUpdateOperationStartTimeStamp);
        }

        private string GetLastOperationStartTimeStampFilePath()
        {
            return Path.Combine(this._settingsManager.DataFolder, LastUpdateOperationStartTimeStampFile);
        }

        private void RestartSystem()
        {
            try
            {
                _eventSource.InfoMessage("Shutting down Fabric Services {0}, {1} before restarting the system",
                    WindowsServiceUtility.FabricInstallerSvcName, WindowsServiceUtility.FabricHostSvcName);
                this.StopFabricServices();

                _eventSource.InfoMessage("Restarting System.");
                ShutdownUtility.EnablePrivilege();
                
                if (!ShutdownUtility.InitiateSystemShutdownEx(null, "PatchOrchestrationApplication has initiated restarting of system for installing windows updates.", 5, true, true, ShutdownUtility.SHTDN_REASON_MAJOR_OTHER | ShutdownUtility.SHTDN_REASON_MINOR_OTHER ))
                {
                    throw new Win32Exception();
                }
                _eventSource.InfoMessage("waiting for system to shutdown.");
                this._helper.WaitOnTask(Task.Delay(-1), this._cancellationToken);
            }
            catch (Exception e)
            {
                string exceptionDesc = String.Format(@"Not able to restart the system. Failed with exception : {0}\n", e);
                _eventSource.ErrorMessage(exceptionDesc);
                throw;
            }
        }

        private bool StartUpdateUtil(CancellationToken cancellationToken)
        {
            _eventSource.InfoMessage("Windows Update Started.");
            try
            {
                return HandleWUOperationStates(cancellationToken);
            }
            catch (Exception e)
            {
                string exceptionDesc = String.Format("Not able to proceed with the Windows Update. Failed with exception : {0}", e);
                _eventSource.ErrorMessage(exceptionDesc);
                return true;
            }         
        }

        private bool HandleWUOperationStates(CancellationToken cancellationToken)
        {
            TimeSpan utilityTaskTimeOut = TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes);
            NodeAgentSfUtilityExitCodes wuOperationState = this._nodeAgentSfUtility.GetWuOperationState(utilityTaskTimeOut);
            _eventSource.InfoMessage("Current WU Operation State : {0}", wuOperationState);

            bool reschedule = false;
            switch (wuOperationState)
            {                
                case NodeAgentSfUtilityExitCodes.None:
                case NodeAgentSfUtilityExitCodes.OperationCompleted:
                    {
                        this.UpdateLastOperationStartTimeStamp(DateTime.UtcNow);
                        OperationResultCode searchResult = SearchUpdates(cancellationToken);
                        reschedule = (searchResult != OperationResultCode.orcSucceeded ? true : reschedule);

                        if (this._wuCollectionWrapper != null)
                        {
                            if (this._wuCollectionWrapper.Collection.Count == 0)
                            {
                                _eventSource.InfoMessage("No Windows Update available. Completing the operation.");
                                //Complete operation.
                                this._nodeAgentSfUtility.UpdateSearchAndDownloadStatus(
                                    NodeAgentSfUtilityExitCodes.OperationCompleted,
                                    this._operationResultFormatter.CreateSearchAndDownloadDummyResult(this.lastUpdateOperationStartTimeStamp),
                                    utilityTaskTimeOut
                                );

                                break;
                            }
                            string wUStatusUpdate = string.Format("Windows update download started.");
                            this._nodeAgentSfUtility.ReportHealth(WUOperationStatus, wUStatusUpdate, HealthState.Ok, -1, TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));

                            OperationResultCode downloadResult = DownloadUpdates(cancellationToken);
                            reschedule = (downloadResult != OperationResultCode.orcSucceeded ? true : reschedule);

                            WindowsUpdateOperationResult searchAndDownloadResult = this._operationResultFormatter.FormatSearchAndDownloadResult(downloadResult, this._wuCollectionWrapper, this.lastUpdateOperationStartTimeStamp);
                            _eventSource.InfoMessage("Search and download result: {0}", searchAndDownloadResult);

                            this._nodeAgentSfUtility.UpdateSearchAndDownloadStatus(NodeAgentSfUtilityExitCodes.DownloadCompleted, searchAndDownloadResult, utilityTaskTimeOut);

                            string WUDownloadComplete = string.Format("Windows updates downloaded, waiting for installation approval from Repair Manager.");
                            this._nodeAgentSfUtility.ReportHealth(WUOperationStatus, WUDownloadComplete, HealthState.Ok, -1, TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));
                            this.UpdateLastOperationStartTimeStamp(DateTime.UtcNow);

                            NodeAgentSfUtilityExitCodes exitCodes = this.WaitForInstallationApproval(cancellationToken);
                            if (exitCodes.Equals(NodeAgentSfUtilityExitCodes.Failure))
                            {
                                _eventSource.ErrorMessage("Not able to move from DownloadCompleted state to InstallationApproved state.");
                                reschedule = true;
                                break;
                            }

                            wUStatusUpdate = string.Format("Windows update installation in progress.");
                            this._nodeAgentSfUtility.ReportHealth(WUOperationStatus, wUStatusUpdate, HealthState.Ok, -1, TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));

                            this._nodeAgentSfUtility.UpdateInstallationStatus(NodeAgentSfUtilityExitCodes.InstallationInProgress, null, utilityTaskTimeOut);

                            OperationResultCode installResult = InstallUpdates(cancellationToken);
                            reschedule = (installResult != OperationResultCode.orcSucceeded ? true : reschedule);

                            WindowsUpdateOperationResult installationResult = this._operationResultFormatter.FormatInstallationResult(installResult, this._wuCollectionWrapper, this.lastUpdateOperationStartTimeStamp);
                            _eventSource.InfoMessage("Installation result: {0}", installationResult);

                            this._nodeAgentSfUtility.UpdateInstallationStatus(NodeAgentSfUtilityExitCodes.InstallationCompleted, installationResult, utilityTaskTimeOut);
                        }

                        break;
                    }
                case NodeAgentSfUtilityExitCodes.DownloadCompleted:
                case NodeAgentSfUtilityExitCodes.InstallationApproved:
                    {
                        if(wuOperationState == NodeAgentSfUtilityExitCodes.DownloadCompleted)
                        {
                            string WUDownloadComplete = string.Format("Windows updates downloaded, waiting for installation approval from Repair Manager.");
                            this._nodeAgentSfUtility.ReportHealth(WUOperationStatus, WUDownloadComplete, HealthState.Ok,-1, TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));
                        }

                        this.UpdateLastOperationStartTimeStamp(DateTime.UtcNow);

                        NodeAgentSfUtilityExitCodes exitCodes = this.WaitForInstallationApproval(cancellationToken);
                        if (exitCodes.Equals(NodeAgentSfUtilityExitCodes.Failure))
                        {
                            _eventSource.ErrorMessage("Not able to move from DownloadCompleted state to InstallationApproved state.");
                            reschedule = true;
                            break;
                        }

                        string wUStatusUpdate = string.Format("Windows update installation in progress.");
                        this._nodeAgentSfUtility.ReportHealth(WUOperationStatus, wUStatusUpdate, HealthState.Ok,-1, TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));

                        OperationResultCode searchResult = SearchUpdates(cancellationToken);
                        reschedule = (searchResult != OperationResultCode.orcSucceeded ? true : reschedule);

                        if (this._wuCollectionWrapper != null)
                        {
                            if (this._wuCollectionWrapper.Collection.Count == 0)
                            {
                                string msg =
                                    "Installation approved but no updates found to install. Completing the operation.";
                                this._nodeAgentSfUtility.ReportHealth(WUOperationStatus, msg, HealthState.Warning, -1, TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));
                                _eventSource.WarningMessage(msg);
                                //Complete operation.
                                this._nodeAgentSfUtility.UpdateInstallationStatus(
                                    NodeAgentSfUtilityExitCodes.OperationCompleted,
                                    this._operationResultFormatter.CreateInstallationDummyResult(this.lastUpdateOperationStartTimeStamp),
                                    utilityTaskTimeOut
                                );
                                break;
                            }

                            this._nodeAgentSfUtility.UpdateInstallationStatus(NodeAgentSfUtilityExitCodes.InstallationInProgress, null, utilityTaskTimeOut);

                            OperationResultCode installResult = InstallUpdates(cancellationToken);
                            reschedule = (installResult != OperationResultCode.orcSucceeded ? true : reschedule);

                            WindowsUpdateOperationResult installationResult = this._operationResultFormatter.FormatInstallationResult(installResult, this._wuCollectionWrapper, this.lastUpdateOperationStartTimeStamp);
                            _eventSource.InfoMessage("Installation result: {0}", installationResult);

                            this._nodeAgentSfUtility.UpdateInstallationStatus(NodeAgentSfUtilityExitCodes.InstallationCompleted, installationResult, utilityTaskTimeOut);
                        }
                        break;
                    }
                case NodeAgentSfUtilityExitCodes.InstallationInProgress:
                    {                        
                        OperationResultCode searchResult = SearchUpdates(cancellationToken);
                        reschedule = (searchResult != OperationResultCode.orcSucceeded ? true : reschedule);

                        if (this._wuCollectionWrapper != null)
                        {
                            if (this._wuCollectionWrapper.Collection.Count == 0)
                            {
                                //this is possible when installation is completed but NT service is killed before updating "InstallationCompleted" status.
                                break;
                            }

                            OperationResultCode installResult = InstallUpdates(cancellationToken);
                            reschedule = (installResult != OperationResultCode.orcSucceeded ? true : reschedule);

                            WindowsUpdateOperationResult installationResult = this._operationResultFormatter.FormatInstallationResult(installResult, this._wuCollectionWrapper, this.lastUpdateOperationStartTimeStamp);
                            _eventSource.InfoMessage("Installation result: {0}", installationResult);

                            this._nodeAgentSfUtility.UpdateInstallationStatus(NodeAgentSfUtilityExitCodes.InstallationCompleted, installationResult, utilityTaskTimeOut);
                        }
                        break;
                    }
                default:
                    break;
            }
            return reschedule;
        }

        private NodeAgentSfUtilityExitCodes WaitForInstallationApproval(CancellationToken cancellationToken)
        {
            _eventSource.InfoMessage("Waiting for Installation approval.");
            while (!cancellationToken.IsCancellationRequested)
            {
                NodeAgentSfUtilityExitCodes wuOperationState = this._nodeAgentSfUtility.GetWuOperationState(TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));

                if (wuOperationState.Equals(NodeAgentSfUtilityExitCodes.InstallationApproved)) 
                {
                    _eventSource.InfoMessage("Installation Approved.");
                    return wuOperationState;
                }
                else if(wuOperationState.Equals(NodeAgentSfUtilityExitCodes.OperationCompleted) || wuOperationState.Equals(NodeAgentSfUtilityExitCodes.None))
                {
                    _eventSource.InfoMessage("Installation Approval failed.");
                    return NodeAgentSfUtilityExitCodes.Failure;
                }
                this._helper.WaitOnTask(Task.Delay(TimeSpan.FromMinutes(this._serviceSettings.WUDelayBetweenRetriesInMinutes)), cancellationToken);
            }
            _eventSource.InfoMessage("Installation Approval failed.");
            return NodeAgentSfUtilityExitCodes.Failure;
        }

        #region Install Updates

        private OperationResultCode InstallUpdates(CancellationToken cancellationToken)
        {
            _eventSource.InfoMessage("Installation Started.");
            if (cancellationToken.IsCancellationRequested)
            {
                _eventSource.InfoMessage("Installation Aborted.");
                return OperationResultCode.orcAborted;
            }

            OperationResultCode installationResult = OperationResultCode.orcNotStarted;

            long retries = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                installationResult = InstallUpdatesUtil(cancellationToken);

                if (installationResult.Equals(OperationResultCode.orcSucceeded) || retries >= this._serviceSettings.WUOperationRetryCount || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                TimeSpan delayBetweenRetries = TimeSpan.FromMinutes(this._serviceSettings.WUDelayBetweenRetriesInMinutes);
                this._helper.WaitOnTask(Task.Delay(delayBetweenRetries), cancellationToken);
                retries++;
            }

            _eventSource.Message("Installation finished with result : {0}.", installationResult);
            return installationResult;
        }

        private OperationResultCode InstallUpdatesUtil(CancellationToken cancellationToken)
        {
            try
            {
                TimeSpan operationTimeOut = TimeSpan.FromMinutes(this.GetRemainingInstallationTimeout());
                UpdateCollection updatesToInstall = new UpdateCollection();
                foreach (WUUpdateWrapper item in this._wuCollectionWrapper.Collection.Values)
                {
                    if (item.IsDownloaded && !item.IsInstalled)
                    {
                        updatesToInstall.Add(item.Update);
                    }
                }
                // if no updates to install
                if (updatesToInstall.Count == 0)
                {
                    _eventSource.InfoMessage("No updates to install.");
                    return OperationResultCode.orcSucceeded;
                }

                IUpdateInstaller uInstaller = this._uSession.CreateUpdateInstaller();
                uInstaller.Updates = updatesToInstall;

                InstallationCompletedCallback installationCompletedCallback = new InstallationCompletedCallback();
                IInstallationJob installationJob = uInstaller.BeginInstall(new InstallationProgressChangedCallback(),
                    installationCompletedCallback, null);
                
                if (
                    !this._helper.WaitOnTask(installationCompletedCallback.Task,
                        (int)operationTimeOut.TotalMilliseconds, cancellationToken))
                {
                    _eventSource.Message("installationJob : Requested Abort");
                    installationJob.RequestAbort();
                }

                IInstallationResult uResult = uInstaller.EndInstall(installationJob);
                for (int i = 0; i < updatesToInstall.Count; i++)
                {
                    var hResult = uResult.GetUpdateResult(i).HResult;
                    var updateID = updatesToInstall[i].Identity.UpdateID;
                    this._wuCollectionWrapper.Collection[updateID].IsInstalled = (hResult == 0);
                    this._wuCollectionWrapper.Collection[updateID].HResult = hResult;
                    if (hResult != 0)
                    {
                        _eventSource.WarningMessage(string.Format("Install for update ID {0} returned hResult {1}", updateID, hResult));
                    }
                }

                return uResult.ResultCode;
            }
            catch (Exception e)
            {
                _eventSource.InfoMessage("Exception while installing Windows-Updates: {0}", e);
                return OperationResultCode.orcFailed;
            }
        }

        private long GetRemainingInstallationTimeout()
        {
            ExecutorDataForNtService executorDataForNtService = this._nodeAgentSfUtility.GetExecutorDataForNtService();
            if (!executorDataForNtService.ApprovedDateTime.HasValue)
            {
                // This should never happen, as we're reading timeout values only when we're in installation phase, implying rm task has been already approved.
                ServiceEventSource.Current.ErrorMessage("executorDataForNtService.ApprovedDateTime was found to be null");
                throw new InvalidDataException("executorDataForNtService.ApprovedDateTime was found to be null");
            }

            _eventSource.InfoMessage("ExecutorDataForNtService : ApprovedDateTime {0} , ExecutorTimeoutInMinutes {1}", executorDataForNtService.ApprovedDateTime, executorDataForNtService.ExecutorTimeoutInMinutes);
            TimeSpan elapsedTime = DateTime.UtcNow.Subtract(executorDataForNtService.ApprovedDateTime.Value);

            long timeout = elapsedTime.Minutes >= executorDataForNtService.ExecutorTimeoutInMinutes ? 0 : executorDataForNtService.ExecutorTimeoutInMinutes - elapsedTime.Minutes;

            _eventSource.InfoMessage("Timeout for installation process: {0}", timeout);
            return timeout;
        }

        class InstallationProgressChangedCallback : IInstallationProgressChangedCallback
        {
            public void Invoke(IInstallationJob installationJob, IInstallationProgressChangedCallbackArgs callbackArgs)
            {
                ServiceEventSource.Current.InfoMessage("Callback: Installation of Windows Updates is In-Progress. Percent completed : {0}", installationJob.GetProgress().PercentComplete);
            }
        }

        class InstallationCompletedCallback : CallBack, IInstallationCompletedCallback
        {
            public void Invoke(IInstallationJob installationJob, IInstallationCompletedCallbackArgs callbackArgs)
            {
                this.CompleteTask();
                ServiceEventSource.Current.InfoMessage("Callback: Installation of Windows Updates is Completed.");
            }
        }

        #endregion

        #region Download Updates
        private OperationResultCode DownloadUpdates(CancellationToken cancellationToken)
        {
            _eventSource.InfoMessage("Download Started.");
            if (cancellationToken.IsCancellationRequested)
            {
                _eventSource.InfoMessage("Download Aborted.");
                return OperationResultCode.orcAborted;
            }

            OperationResultCode downloadResult = OperationResultCode.orcNotStarted;
            long retries = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                downloadResult = DownloadUpdatesUtil(cancellationToken);

                if (downloadResult.Equals(OperationResultCode.orcSucceeded) || retries >= this._serviceSettings.WUOperationRetryCount || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                TimeSpan delayBetweenRetries = TimeSpan.FromMinutes(this._serviceSettings.WUDelayBetweenRetriesInMinutes);
                this._helper.WaitOnTask(Task.Delay(delayBetweenRetries), cancellationToken);
                retries++;
            }

            _eventSource.Message("Download finished with result : {0}.", downloadResult);
            return downloadResult;
        }

        private OperationResultCode DownloadUpdatesUtil(CancellationToken cancellationToken)
        {
            try
            {
                UpdateDownloader uDownloader = this._uSession.CreateUpdateDownloader();
                UpdateCollection updatesToDownload = new UpdateCollection();               

                foreach (WUUpdateWrapper item in this._wuCollectionWrapper.Collection.Values)
                {                    
                    if (!item.IsDownloaded)
                    {
                        if (item.Update.EulaAccepted == false)
                        {
                            if (this._serviceSettings.AcceptWindowsUpdateEula) {
                                try
                                {
                                    item.Update.AcceptEula();
                                }
                                catch (Exception e)
                                {
                                    _eventSource.WarningMessage(string.Format("Error occurred while accepting Eula for {0} . Exception : {1}",
                                        item, e));
                                }
                                updatesToDownload.Add(item.Update);
                            }
                        }
                        else
                        {
                            updatesToDownload.Add(item.Update);
                        }                        
                    }
                }

                uDownloader.Updates = updatesToDownload;

                DownloadCompletedCallback downloadCompletedCallback = new DownloadCompletedCallback();
                IDownloadJob downloadJob = uDownloader.BeginDownload(new DownloadProgressChangedCallback(),
                    downloadCompletedCallback, null);

                TimeSpan operationTimeOut = TimeSpan.FromMinutes(this._serviceSettings.WUOperationTimeOutInMinutes);
                if (
                    !this._helper.WaitOnTask(downloadCompletedCallback.Task, (int)operationTimeOut.TotalMilliseconds,
                        cancellationToken))
                {
                    _eventSource.Message("downloadJob : Requested Abort");
                    downloadJob.RequestAbort();
                }
                                
                IDownloadResult uResult = uDownloader.EndDownload(downloadJob);
                for (int i = 0; i < updatesToDownload.Count; i++)
                {
                    var hResult = uResult.GetUpdateResult(i).HResult;
                    var updateID = updatesToDownload[i].Identity.UpdateID;
                    this._wuCollectionWrapper.Collection[updateID].IsDownloaded = (hResult == 0);
                    this._wuCollectionWrapper.Collection[updateID].HResult = hResult;
                    if (hResult != 0)
                    {
                        _eventSource.WarningMessage(string.Format("Download for update ID {0} returned hResult {1}", updateID, hResult));
                    }
                }
                
                return uResult.ResultCode;
            }
            catch (Exception e)
            {
                if ((uint)e.HResult == WUErrorCodes.WU_E_NO_UPDATE)
                {
                    return OperationResultCode.orcSucceeded; // no updates found.
                }
                _eventSource.InfoMessage("Exception while downloading Windows-Updates: {0}", e);
                return OperationResultCode.orcFailed;
            }
        }

        class DownloadProgressChangedCallback : IDownloadProgressChangedCallback
        {
            public void Invoke(IDownloadJob downloadJob, IDownloadProgressChangedCallbackArgs callbackArgs)
            {
                ServiceEventSource.Current.VerboseMessage("Callback: Downloading of Windows Updates is In-Progress. Percent completed : "+ downloadJob.GetProgress().PercentComplete);
            }
        }

        class DownloadCompletedCallback : CallBack, IDownloadCompletedCallback
        {
            public void Invoke(IDownloadJob downloadJob, IDownloadCompletedCallbackArgs callbackArgs)
            {
                this.CompleteTask();
                ServiceEventSource.Current.VerboseMessage("Callback: Downloading of Windows Updates completed.");
            }
        }
        #endregion

        #region Search Updates
        private OperationResultCode SearchUpdates(CancellationToken cancellationToken)
        {
            _eventSource.InfoMessage("Search Started.");
            if (cancellationToken.IsCancellationRequested)
            {
                _eventSource.InfoMessage("Search Aborted.");
                return OperationResultCode.orcAborted;
            }

            ISearchResult searchResult = null;
            long retries = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                searchResult = SearchUpdatesUtil(cancellationToken);

                if ((searchResult != null && searchResult.ResultCode == OperationResultCode.orcSucceeded) || retries >= this._serviceSettings.WUOperationRetryCount || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                TimeSpan delayBetweenRetries = TimeSpan.FromMinutes(this._serviceSettings.WUDelayBetweenRetriesInMinutes);
                this._helper.WaitOnTask(Task.Delay(delayBetweenRetries), cancellationToken);
                retries++;
            }

            if (searchResult != null)
            {
                this._wuCollectionWrapper = this.GetWUCollection(searchResult.Updates);                
                _eventSource.Message("Search finished with result : {0}. Total searched results : {1}", searchResult.ResultCode, this._wuCollectionWrapper.Collection.Count);
                return searchResult.ResultCode;
            }
            else
            {
                this._wuCollectionWrapper = null;                
                _eventSource.Message("No Search Result!");
                return OperationResultCode.orcFailed;
            }
        }

        private ISearchResult SearchUpdatesUtil(CancellationToken cancellationToken)
        {
            try
            {
                IUpdateSearcher uSearcher = this._uSession.CreateUpdateSearcher();
                SearchCompletedCallback searchCompletedCallback = new SearchCompletedCallback();

                string searchQuery = this._serviceSettings.WUQuery;
                ISearchJob searchJob = uSearcher.BeginSearch(searchQuery, searchCompletedCallback, null);

                TimeSpan operationTimeOut = TimeSpan.FromMinutes(this._serviceSettings.WUOperationTimeOutInMinutes);
                if (
                    !this._helper.WaitOnTask(searchCompletedCallback.Task, (int)operationTimeOut.TotalMilliseconds,
                        cancellationToken))
                {
                    _eventSource.Message("searchJob : Requested Abort");
                    searchJob.RequestAbort();
                }

                ISearchResult uResult = uSearcher.EndSearch(searchJob);
                return uResult;
            }
            catch (Exception e)
            {
                _eventSource.InfoMessage("Exception while searching for Windows-Updates: {0}", e);
                return null;
            }
        }


        class SearchCompletedCallback : CallBack, ISearchCompletedCallback
        {
            public void Invoke(ISearchJob searchJob, ISearchCompletedCallbackArgs callbackArgs)
            {
                this.CompleteTask();
                ServiceEventSource.Current.InfoMessage("Callback: Searching of Windows Updates completed.");
            }
        }
        #endregion

        private WUCollectionWrapper GetWUCollection(UpdateCollection searchUpdates)
        {
            WUCollectionWrapper collectionWrapper = new WUCollectionWrapper();
            foreach (IUpdate2 update in searchUpdates)
            {
                if (UpdateIsInCategory(update))
                {
                    collectionWrapper.Add(update);
                }
            }
            return collectionWrapper;
        }

        private bool UpdateIsInCategory(IUpdate update)
        {
            if (this._serviceSettings.CategoryIds == null || this._serviceSettings.CategoryIds.Count == 0)
            {
                _eventSource.VerboseMessage(string.Format("CategoryIds not set, whitelisting {0}", update.Identity.UpdateID));
                return true;
            }

            _eventSource.VerboseMessage(string.Format("checking update {0} is in category Ids : {1}", update.Identity.UpdateID, string.Join(" , ",this._serviceSettings.CategoryIds.ToArray())));
            foreach (ICategory category in update.Categories)
            {
                _eventSource.VerboseMessage(string.Format("update id : {0} category id : {1}", update.Identity.UpdateID, category.CategoryID));
                if (CheckCategory(category))
                {
                    return true;
                }    
            }
            return false;
        }

        private bool CheckCategory(ICategory category)
        {
            if (this._serviceSettings.CategoryIds.Exists(x => x.Equals(category.CategoryID)))
            {
                return true;
            }

            if (category.Parent == null)
            {
                return false;
            }
            _eventSource.VerboseMessage(string.Format("category id : {0} parent of category id : {1}", category.CategoryID, category.Parent.CategoryID));
            return CheckCategory(category.Parent);
        }

        abstract class CallBack
        {
            private TaskCompletionSource<bool> taskSource = new TaskCompletionSource<bool>();
            protected void CompleteTask()
            {
                taskSource.SetResult(true);
            }

            public Task Task
            {
                get
                {
                    return taskSource.Task;
                }
            }
        }
    }


}
