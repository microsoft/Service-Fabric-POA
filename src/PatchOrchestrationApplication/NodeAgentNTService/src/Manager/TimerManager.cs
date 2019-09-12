// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentNTService.Manager
{
    using Common;
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Utility;
    using System.Security.Cryptography;
    using System.Text;
    using WUApiLib;

    /// <summary>
    /// Schedules the callback to execute at frequency/time mentioned in Settings.xml. 
    /// </summary>
    class TimerManager
    {
        private readonly ServiceEventSource _eventSource = ServiceEventSource.Current;
        private const string CheckpointFileName = "TimerCheckPoint.txt";
        private const long WaitTimeInMinutes = 5;
        private const string CommandPromptExecutableName = "cmd.exe";

        private readonly DateTime _checkpointFileDefaultDateTime = default(DateTime);
        private readonly SettingsManager _settingsManager;        
        private readonly Helper _helper;
        private readonly WindowsUpdateManager _windowsUpdateManager;
        private readonly CancellationToken _cancellationToken;        
        private readonly NodeAgentSfUtility _nodeAgentSfUtility;
        private readonly ServiceSettings _serviceSettings;
        private const string WUOperationStatus = "WUOperationStatus";
        private const string WUOperationSetting = "WUOperationSetting";

        /// <summary>
        /// Initializes timer manager.
        /// </summary>             
        public TimerManager(NodeAgentSfUtility nodeAgentSfUtility, SettingsManager settingsManager, WindowsUpdateManager windowsUpdateManager, CancellationToken cancellationToken)
        {            
            this._settingsManager = settingsManager;            
            this._serviceSettings = settingsManager.GetSettings();                         
            this._cancellationToken = cancellationToken;
            this._helper = new Helper();
            this._windowsUpdateManager = windowsUpdateManager;
            this._nodeAgentSfUtility = nodeAgentSfUtility;
        }

        /// <summary>
        /// Starts timer. Throws an exception in case settings.xml is not found.
        /// </summary>
        public void StartTimer()
        {
            try
            {
                //System.Diagnostics.Debugger.Launch();
                _eventSource.InfoMessage("Starting Timer.");

                this.WaitForSettingsFile();
                this.Clean();

                this.LoadSettings();                

                this.DisableWindowsUpdate();
                
                this.PostWUUpdateEventOnService();

                this.ScheduleTimer();
            }
            catch (Exception e)
            {
                string msg = string.Format("Exception while starting timer. {0}", e);
                _eventSource.ErrorMessage(msg);
                
                this._nodeAgentSfUtility.ReportHealth(WUOperationSetting, msg, HealthState.Warning, -1,
                            TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));

                TimeSpan operationTimeSpan = TimeSpan.FromMinutes(WaitTimeInMinutes);
                if (this._helper.WaitOnTask(Task.Delay(operationTimeSpan), this._cancellationToken))
                {
                    this.StartTimer();
                }
            }
        }

        /// <summary>
        /// This will post an event containing the information about patching on CoordinatorService.
        /// </summary>
        private void PostWUUpdateEventOnService()
        {
            CheckpointFileData fileData = this.ReadCheckpointFile();
            string formatString = "Last patching attempt happened at : {0}, Next patching cycle is scheduled at : {1}";
            string healthDescription = "";
            if (fileData.lastAttemptedUpdateTime.Equals(_checkpointFileDefaultDateTime))
            {
                healthDescription = string.Format(formatString, "N/A", fileData.schedulingDateTime.ToString());
            }
            else
            {
                healthDescription = string.Format(formatString, fileData.lastAttemptedUpdateTime.ToString(), fileData.schedulingDateTime.ToString());
            }
            healthDescription += "\nFor detailed installation results, refer to https://docs.microsoft.com/azure/service-fabric/service-fabric-patch-orchestration-application#view-the-windows-update-results";
            this._nodeAgentSfUtility.ReportHealth(WUOperationStatus, healthDescription, HealthState.Ok, -1, TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));

        }

        /// <summary>
        /// This will disable the windows update on system.
        /// </summary>
        private void DisableWindowsUpdate()
        {
            
            if (!this._serviceSettings.DisableWindowsUpdates)
            {
                _eventSource.InfoMessage("Not disabling automatic windows updates.");
                return;
            }

            _eventSource.InfoMessage("Disabling automatic windows updates.");
            do
            {
                string msg = "Not able to disable the 'Windows Updates'. ";
                try
                {
                    WindowsAutoUpdateUtility auUtility = new WindowsAutoUpdateUtility();
                    if (auUtility.IsWindowsServer2012R2OrLower())
                    {
                        _eventSource.InfoMessage("Detected OS version is Windows Server 2012R2 or lower");
                        AutomaticUpdatesClass updates = new AutomaticUpdatesClass();

                        _eventSource.InfoMessage("Current automatic windows updates notification level {0}.", updates.Settings.NotificationLevel);
                        auUtility.DisableAUThroughWUApi(updates);
                    }
                    else
                    {
                        _eventSource.InfoMessage("Detected OS version is higher than Windows Server 2012R2");
                        _eventSource.InfoMessage("Current AU registry values are {0}", auUtility.LogCurrentAUValues());
                        auUtility.SetAUOptions();
                        _eventSource.InfoMessage("New AU registry values are {0}", auUtility.LogCurrentAUValues());
                    }
                    string updateMsg = "Windows Update policy has been configured to Notify before Download";
                    this._nodeAgentSfUtility.ReportHealth(WUOperationSetting, updateMsg, HealthState.Ok, -1, TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));
                    return;
                }
                catch (Exception e)
                {
                    msg = string.Format(msg + "Failing with exception : {0}", e);                                        
                }

                _eventSource.WarningMessage(msg);
                this._nodeAgentSfUtility.ReportHealth(WUOperationSetting, msg, HealthState.Warning, -1, TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));
                this._helper.WaitOnTask(Task.Delay(TimeSpan.FromMinutes(WaitTimeInMinutes)), this._cancellationToken);
            } while (true);            
        }

        // Clean the temp folder
        private void Clean()
        {
            string tmpFolderPath = this._settingsManager.TempFolder;
            Array.ForEach(Directory.GetFiles(tmpFolderPath), File.Delete);
        }

        private void RemoveSelf()
        {
            try
            {
                string data = string.Format("/C sc stop POSNodeSvc & " +
                                            "sc delete POSNodeSvc & " +
                                            "logman stop PatchOrchestrationServiceTraces & " +
                                            "logman delete PatchOrchestrationServiceTraces & " +
                                            "del /s /q {0} & " +
                                            "rmdir /s /q {0}" , this._settingsManager.RootFolder);

                ProcessExecutor proc = new ProcessExecutor(CommandPromptExecutableName, data, true);
                proc.ExecuteAsync();                
            }
            catch (Exception e)
            {
                _eventSource.ErrorMessage("Not able to delete NT Service. Failed with exception : {0}", e);
                throw;
            }
        }

        // polls every 5 minutes, execute the callback if the date-time is according to frequency mentioned in Settings.xml.
        private void ScheduleTimer()
        {
            try
            {
                if (!this.CheckApplicationExists())
                {
                    _eventSource.InfoMessage("Application deleted. Removing NT service...");
                    this._windowsUpdateManager.ResetStateMachine();
                    this.RemoveSelf();                    
                    return;
                }

                // If cancellation token is canceled or application is not found
                if (this._cancellationToken.IsCancellationRequested)
                {
                    _eventSource.InfoMessage("Canceled timer.");
                    return;
                }

                NodeAgentSfUtilityExitCodes exitCode = this._nodeAgentSfUtility.GetWuOperationState(
                    TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));
                _eventSource.InfoMessage("Current Wu state: {0}", exitCode);
                if (exitCode == NodeAgentSfUtilityExitCodes.RestartRequested)
                {                                              
                    _eventSource.ErrorMessage("Not able to restart system.");
                    
                    //wait for sometime before retrying. This delay is recommended if posting health reports.
                    if (this._helper.WaitOnTask(Task.Delay(TimeSpan.FromMinutes(WaitTimeInMinutes)),
                        this._cancellationToken))
                    {
                        this.ScheduleWindowsUpdates();
                        this.ScheduleTimer();
                    }
                    return;
                }
                else if (exitCode == NodeAgentSfUtilityExitCodes.RestartCompleted)
                {
                    this.ScheduleWindowsUpdates();

                    this.CreateNewCheckpointFile();                       

                    this.ScheduleTimer();
                    return;
                }

                CheckpointFileData fileData = this.ReadCheckpointFile();

                if (fileData.rescheduleNeeded)
                {
                    // If total retries are exhausted, schedule the call back for next interval mentioned in Settings.xml.
                    if (this.IncrementRetryCount() == false)
                    {
                        if (this._windowsUpdateManager.ResetStateMachine())
                        {
                            this.UpdateSettingsAndCreateCheckpoint();
                        }
                        else
                        {
                            if (this._helper.WaitOnTask(Task.Delay(TimeSpan.FromMinutes(WaitTimeInMinutes)),
                                this._cancellationToken))
                            {
                                this.ScheduleTimer();
                            }
                            return;
                        }
                    }
                }
                else
                {
                    // Do not update the lastAttemptedTime.
                    this.UpdateSettingsAndCreateCheckpoint(false);                    
                }

                // read checkpoint file after modifications.
                fileData = this.ReadCheckpointFile();

                // Execute call back
                if (this.ScheduleWindowsUpdatesFlag(fileData))
                {
                    bool retryNeeded = this.ScheduleWindowsUpdates();

                    if (retryNeeded)
                    {
                        fileData.rescheduleNeeded = true;
                        this.WriteCheckpointFile(fileData);
                    }
                    else
                    {
                        this.CreateNewCheckpointFile();
                    }

                    this.ScheduleTimer();
                    return;
                }
            }
            catch (Exception e)
            {
                _eventSource.ErrorMessage("ScheduleTimer ended with exception : {0}", e);
            }
            TimeSpan operationTimeSpan = TimeSpan.FromMinutes(WaitTimeInMinutes);
            if (this._helper.WaitOnTask(Task.Delay(operationTimeSpan), this._cancellationToken))
            {
                this.ScheduleTimer();
            }
        }

        private bool ScheduleWindowsUpdatesFlag(CheckpointFileData fileData)
        {
            return !fileData.schedulingDateTime.Equals(_checkpointFileDefaultDateTime) &&
                    DateTime.Compare(fileData.schedulingDateTime, DateTime.UtcNow) <= 0;
        }

        private bool ScheduleWindowsUpdates()
        {            
                _eventSource.InfoMessage("Timer Callback started.");
                this._windowsUpdateManager.ResetManager();
                bool rescheduleNeeded = this._windowsUpdateManager.StartUpdate();

                // before reboot writing in checkpoint file.
                if (this._windowsUpdateManager.RebootRequired())
                {
                    if (rescheduleNeeded)
                    {
                        CheckpointFileData fileData = this.ReadCheckpointFile();
                        fileData.rescheduleNeeded = true;
                        this.WriteCheckpointFile(fileData);
                    }

                    this._windowsUpdateManager.HandleRestart();
                }

                this._windowsUpdateManager.CompleteWUOperations();
                return rescheduleNeeded;                       
        }

        private bool CheckApplicationExists()
        {
            bool applicationExists = this._nodeAgentSfUtility.GetApplicationDeployedStatus(TimeSpan.FromMinutes(this._serviceSettings.OperationTimeOutInMinutes));            
            _eventSource.InfoMessage("Application exists : {0}", applicationExists);
            return applicationExists;
        }

        private void CreateNewCheckpointFile(bool updateAttempted = true)
        {
            CheckpointFileData checkpointFileData = this.ReadCheckpointFile();
            checkpointFileData.schedulingDateTime = this.GetNextSchedulingTime();
            if(updateAttempted)
            {
                checkpointFileData.lastAttemptedUpdateTime =  DateTime.UtcNow;
            }
            checkpointFileData.rescheduleCount = 0;
            checkpointFileData.rescheduleNeeded = false;
            this.WriteCheckpointFile(checkpointFileData);
            this.PostWUUpdateEventOnService();
        }

        private bool UpdateSettingsAndCreateCheckpoint(bool updateAttempted = true)
        {            
            if (this.CheckIfNewSettingsAvailable())
            {   // create temporary copy of settings.xml             
                this.CreateTempCopyOfSettingsFile();

                try
                {
                    this._settingsManager.UpdateSettings(this._settingsManager.TempCopyofSettingsFilePath);
                    string message = "Successfully updated the settings.";
                    this._nodeAgentSfUtility.ReportHealth(WUOperationSetting, message, HealthState.Ok, 1);
                }
                catch(Exception ex)
                {
                    string healthWarning = string.Format("Attempt to update settings failed with exception ex: {0}", ex);
                    this._nodeAgentSfUtility.ReportHealth(WUOperationSetting, healthWarning, HealthState.Warning);
                }

                //create checkpoint
                this.CreateNewCheckpointFile(updateAttempted);

                //create copy of settings.xml
                this.CreateCopyOfSettingsFile();

                //delete temporary copy of settings.xml
                this.DeleteTempCopyOfSettingsFile();

                _eventSource.InfoMessage("New Settings updated.");            
                return true;
            }
            return false;
        }

        private void LoadSettings()
        {
            CheckpointFileData checkpointFileData = ReadCheckpointFile();
            bool scheduleWindowsUpdatesFlag = this.ScheduleWindowsUpdatesFlag(checkpointFileData);

            if (!File.Exists(this._settingsManager.CopyofSettingsFilePath) || !scheduleWindowsUpdatesFlag)
            {
                this.CreateCopyOfSettingsFile();
            }
            try
            {
                this._settingsManager.UpdateSettings(this._settingsManager.CopyofSettingsFilePath);
            }
            catch
            {
                /**
                 * It could be possible that the setting is corrupt and sheduleWindowsUpdatesFlag is set as
                 * CheckPoint is old. In that case, it will crash here even though we have updated the settings.xml
                 * to remove the invalid values.
                 */
                _eventSource.WarningMessage("UpdateSettings crashed probably because of corrupt settings. So, creating copy of latest settings file.");
                this.CreateCopyOfSettingsFile();
                throw;
            }
            _eventSource.InfoMessage("Loaded settings: {0}", this._settingsManager.GetSettings());

            if (!scheduleWindowsUpdatesFlag)
            {
                // Do not update the last updateAttemptedTime.
                this.CreateNewCheckpointFile(false);
                _eventSource.InfoMessage("Loaded checkpoint file.");
            }
        }
        
        private bool IncrementRetryCount()
        {
            _eventSource.InfoMessage("Incrementing reschedule Count.");
            CheckpointFileData checkpointFileData = this.ReadCheckpointFile();            
            if (checkpointFileData.rescheduleCount >= this._serviceSettings.WURescheduleCount)
            {
                _eventSource.InfoMessage("Exceeded reschedule count. WU retry count : {0} , Current NT Service retry count : {1}", this._serviceSettings.WURescheduleCount, (checkpointFileData.rescheduleCount + 1));
                return false;
            }
            checkpointFileData.rescheduleCount++;
            checkpointFileData.schedulingDateTime.AddMinutes(this._serviceSettings.WURescheduleTimeInMinutes);
            checkpointFileData.rescheduleNeeded = false;
            _eventSource.InfoMessage("Incremented reschedule count. Service reschedule Count is :{0}",
                (checkpointFileData.rescheduleCount + 1));
            this.WriteCheckpointFile(checkpointFileData);
            return true;
        }

        private CheckpointFileData ReadCheckpointFile()
        {
            CheckpointFileData checkpointFileData = new CheckpointFileData();
            string checkpointFilepath = this.GetCheckpointFilePath();
            if (File.Exists(checkpointFilepath))
            {
                string text = File.ReadAllText(checkpointFilepath).Trim();
                try
                {
                    string[] arr = text.Split(' ');
                    checkpointFileData.schedulingDateTime = DateTime.ParseExact(arr[0], "yyyyMMddHHmmss", null);
                    checkpointFileData.rescheduleCount = (long)Convert.ChangeType(arr[1], typeof(long));
                    checkpointFileData.rescheduleNeeded = Boolean.Parse(arr[2]);
                    if (arr.Length == 4)
                    {
                        checkpointFileData.lastAttemptedUpdateTime = DateTime.ParseExact(arr[3], "yyyyMMddHHmmss", null);
                    }
                }
                catch(Exception ex)
                {
                    _eventSource.ErrorMessage("TimerCheckPoint.txt is not in correct format. Content of the file : {0}, Exception thrown {1}", text, ex);
                    File.Delete(checkpointFilepath);
                }
            }
            _eventSource.InfoMessage("Checkpoint file read: {0}", checkpointFileData);
            return checkpointFileData;
        }

        private void WriteCheckpointFile(CheckpointFileData fileData)
        {
            string checkpointFilepath = this.GetCheckpointFilePath();
            string randomFilePath = this.GetRandomFilePath();

            if (File.Exists(randomFilePath))
            {
                File.Delete(randomFilePath);
            }
            using (FileStream fs = File.Create(randomFilePath))
            {
                string data = fileData.schedulingDateTime.ToString("yyyyMMddHHmmss") + " " + fileData.rescheduleCount + " " + fileData.rescheduleNeeded;
                if (!fileData.lastAttemptedUpdateTime.Equals(_checkpointFileDefaultDateTime))
                {
                     data += " " + fileData.lastAttemptedUpdateTime.ToString("yyyyMMddHHmmss");
                }
                Byte[] info = new System.Text.UTF8Encoding(true).GetBytes(data);
                fs.Write(info, 0, info.Length);
            }

            if (File.Exists(checkpointFilepath))
            {
                File.Delete(checkpointFilepath);
            }
            File.Move(randomFilePath, checkpointFilepath);                
            File.Delete(randomFilePath);     
            _eventSource.InfoMessage("Checkpoint file written : {0}", fileData);       
        }
        
        private bool CheckIfNewSettingsAvailable()
        {
            string copyOfSettingsFilePath = this._settingsManager.CopyofSettingsFilePath;
            if (!File.Exists(copyOfSettingsFilePath))
            {         
                _eventSource.InfoMessage("{0} doesn't exist. ", copyOfSettingsFilePath);       
                return true;
            }
            string settingsFilePath = this._settingsManager.SettingsFilePath;

            string copyOfSettingsFileMd5 = GetMD5(copyOfSettingsFilePath);
            string settingsFileMd5 = GetMD5(settingsFilePath);
            
            if (!copyOfSettingsFileMd5.Equals(settingsFileMd5))
            {
                _eventSource.InfoMessage("New Settings.xml found.");
                return true;
            }
            return false;
        }

        private string GetMD5(string filename)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filename))
                {
                    return Encoding.Default.GetString(md5.ComputeHash(stream));
                }
            }
        }

        private void CreateCopyOfSettingsFile()
        {            
            string copyOfSettingsFilePath = this._settingsManager.CopyofSettingsFilePath;
            string settingsFilePath = this._settingsManager.SettingsFilePath;

            if (File.Exists(copyOfSettingsFilePath))
            {
                File.Delete(copyOfSettingsFilePath);
            }

            File.Copy(settingsFilePath, copyOfSettingsFilePath);
            _eventSource.InfoMessage("Copy of settings.xml done. Location : {0}", copyOfSettingsFilePath);
        }

        private void CreateTempCopyOfSettingsFile()
        {
            string tempCopyOfSettingsFilePath = this._settingsManager.TempCopyofSettingsFilePath;
            string settingsFilePath = this._settingsManager.SettingsFilePath;

            if (File.Exists(tempCopyOfSettingsFilePath))
            {
                File.Delete(tempCopyOfSettingsFilePath);
            }

            File.Copy(settingsFilePath, tempCopyOfSettingsFilePath);
            _eventSource.InfoMessage("Temp copy of settings.xml done. Location : {0}", tempCopyOfSettingsFilePath);
        }

        private void DeleteTempCopyOfSettingsFile()
        {
            string tempCopyOfSettingsFilePath = this._settingsManager.TempCopyofSettingsFilePath;            
            if (File.Exists(tempCopyOfSettingsFilePath))
            {
                File.Delete(tempCopyOfSettingsFilePath);
            }
        }

        private void WaitForSettingsFile()
        {
            while (!File.Exists(this._settingsManager.SettingsFilePath))
            {
                _eventSource.VerboseMessage("Waiting for Settings.xml.");
                this._helper.WaitOnTask(Task.Delay(TimeSpan.FromSeconds(40)), this._cancellationToken);
            }            
        }        
        
        /// <summary>
        /// Stops the timer.         
        /// </summary>
        public void StopTimer()
        {
            try
            {                                       
                this._windowsUpdateManager.Stop();
                _eventSource.InfoMessage("Timer Stopped.");
            }
            catch (Exception e)
            {
                _eventSource.ErrorMessage("Exception in StopTimer {0}", e);
                throw e;
            }
        }

        /// <summary>
        /// Close the timer.
        /// </summary>
        public void DisposeTimer()
        {
            try
            {                                
                this._windowsUpdateManager.Dispose();
                _eventSource.InfoMessage("Timer Disposed.");
            }
            catch (Exception e)
            {
                _eventSource.ErrorMessage("Exception in DisposeTimer {0}", e);
                throw e;
            }
        }
                
        private string GetCheckpointFilePath()
        {            
            return Path.Combine(this._settingsManager.DataFolder, CheckpointFileName);
        }

        private string GetRandomFilePath()
        {            
            return Path.Combine(this._settingsManager.TempFolder, Path.GetRandomFileName());
        }        

        private DateTime GetNextSchedulingTime()
        {         
            ServiceSettings settings = this._serviceSettings;
            DateTime currentTime = DateTime.UtcNow;
            DateTime settingsDateTime = settings.Date;

            switch (settings.Frequency)
            {
                case Frequency.Monthly:
                {
                    DateTime next;
                    if (settings.IsLastDayOfMonth)
                    {
                        next = new DateTime(currentTime.Year, currentTime.Month, 1, settingsDateTime.Hour,
                            settingsDateTime.Minute, settingsDateTime.Second);
                        next = next.AddMonths(1).AddDays(-1);

                        if (DateTime.Compare(next, currentTime) < 0)
                        {
                            next = next.AddDays(1).AddMonths(1).AddDays(-1);
                        }
                    }
                    else
                    {
                        next = new DateTime(currentTime.Year, currentTime.Month, settingsDateTime.Day,
                            settingsDateTime.Hour, settingsDateTime.Minute, settingsDateTime.Second);
                        if (DateTime.Compare(next, currentTime) < 0)
                        {
                            next = next.AddMonths(1);
                        }
                    }
                    
                    return next;
                }

                case Frequency.Weekly:
                    {
                        DateTime next = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day, settingsDateTime.Hour, settingsDateTime.Minute, settingsDateTime.Second);
                        next = next.AddDays((settings.DayOfWeek - next.DayOfWeek + 7) % 7);

                        if (DateTime.Compare(next, currentTime) < 0)
                        {
                            next = next.AddDays(7);
                        }
                        return next;
                    }

                case Frequency.Daily:
                    {
                        DateTime next = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day, settingsDateTime.Hour, settingsDateTime.Minute, settingsDateTime.Second);                        

                        if (DateTime.Compare(next, currentTime) < 0)
                        {
                            next = next.AddDays(1);
                        }
                        return next;
                    }

                case Frequency.Once:
                    {
                        DateTime next = new DateTime(settingsDateTime.Year, settingsDateTime.Month, settingsDateTime.Day, settingsDateTime.Hour, settingsDateTime.Minute, settingsDateTime.Second);

                        if (DateTime.Compare(next, currentTime) < 0)
                        {                            
                            return _checkpointFileDefaultDateTime;
                        }
                        return next;
                    }
                case Frequency.None:
                    {                        
                        return _checkpointFileDefaultDateTime;
                    }
                case Frequency.Hourly:
                    {                        
                        return currentTime.AddMinutes(this._serviceSettings.HourlyFrequencyInMinutes);
                    }
                default:                    
                    return _checkpointFileDefaultDateTime;
            }
        }

        class CheckpointFileData
        {
            public DateTime schedulingDateTime = default(DateTime);
            public long rescheduleCount = 0;
            public bool rescheduleNeeded = false;
            public DateTime lastAttemptedUpdateTime = default(DateTime);

            public override string ToString()
            {
                return
                    string.Format(
                        "CheckpointFileData : schedulingDateTime : {0} , rescheduleCount : {1} , rescheduleNeeded : {2}, lastAttemptedUpdateTime : {3}", 
                        schedulingDateTime, rescheduleCount, rescheduleNeeded, lastAttemptedUpdateTime
                    );
            }
        }        
    }
}
