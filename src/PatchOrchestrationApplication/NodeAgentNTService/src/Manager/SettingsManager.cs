// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentNTService.Manager
{
    using System;
    using System.IO;
    using System.Xml;
    /// <summary>
    /// Parse and store the Settings.xml.
    /// </summary>
    class SettingsManager
    {
        private readonly ServiceEventSource eventSource = ServiceEventSource.Current;

        private const string WUQueryName = "WUQuery";
        private const string WUOperationRetryCountName = "WUOperationRetryCount";
        private const string WUDelayBetweenRetriesInMinutesName = "WUDelayBetweenRetriesInMinutes";
        private const string WUOperationTimeOutInMinutesName = "WUOperationTimeOutInMinutes";
        private const string WURescheduleTimeInMinutesName = "WURescheduleTimeInMinutes";
        private const string WURescheduleCountName = "WURescheduleCount";
        private const string WUFrequencyName = "WUFrequency";
        private const string DisableAutoUpdateSettingInOSName = "DisableAutoUpdateSettingInOS";
        private const string OperationTimeOutInMinutesName = "OperationTimeOutInMinutes";
        private const string InstallWindowsOSOnlyUpdateName = "InstallWindowsOSOnlyUpdates";
        private const string WUQueryCategoryIdsName = "WUQueryCategoryIds";
        private const string AcceptWindowsUpdateEulaName = "AcceptWindowsUpdateEula";
        
        private const string SettingsFileName = "Settings.xml";
        private const string CopyOfSettingsFileName = "CopyOfSettings.xml";
        private const string TempCopyOfSettingsFileName = "TempCopyOfSettings.xml";

        private const string DefaultWUQuery = "IsInstalled=0";
        private const long DefaultWUOperationRetryCount = 5;
        private const long DefaultWUDelayBetweenRetriesInMinutes = 1;
        private const long DefaultWUOperationTimeOutInMinutes = 90;
        private const long DefaultWURescheduleTimeInMinutes = 30;
        private const long DefaultWURescheduleCount = 5;
        private const string DefaultWUFrequency = "Weekly,Wednesday,7:00:00";
        private const long DefaultOperationTimeOutInMinutes = 5;
        private const bool DefaultDisableAutoUpdateSettingInOS = true;
        private const bool DefaultInstallWindowsOSOnlyUpdates = false;
        private const string DefaultWUQueryCategoryIds = "";
        private const bool DefaultAcceptWindowsUpdateEula = true;        

        private readonly ServiceSettings _serviceSettings;

        /// <summary>
        /// Returns the root folder path.
        /// </summary>
        public string RootFolder { get; private set; }

        /// <summary>
        /// Returns the work folder path.
        /// </summary>
        public string WorkFolder { get; private set; }

        /// <summary>
        /// Returns temp folder path. Temp folder is used for putting temp/dummy files which are created during execution of timer.
        /// </summary>
        public string TempFolder { get; private set; }

        /// <summary>
        /// Returns Data folder path. Data folder is used for putting configuration and data files which are created during execution of timer.
        /// </summary>
        public string DataFolder { get; private set; }

        /// <summary>
        /// Returns Settings.xml file path.
        /// </summary>
        public string SettingsFilePath { get; private set; }

        /// <summary>
        /// Returns CopyOfSettings.xml file path.
        /// </summary>
        public string CopyofSettingsFilePath { get; private set; }

        /// <summary>
        /// Returns TempCopyOfSettings.xml file path.
        /// </summary>
        public string TempCopyofSettingsFilePath { get; private set; }

        public SettingsManager()
        {
            WorkFolder = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            TempFolder = Path.Combine(WorkFolder, "TempDir");
            DataFolder = Path.Combine(WorkFolder, "Data");
            RootFolder = Directory.GetParent(WorkFolder).FullName;

            if (!Directory.Exists(TempFolder))
            {
                Directory.CreateDirectory(TempFolder);
            }

            if (!Directory.Exists(DataFolder))
            {
                Directory.CreateDirectory(DataFolder);
            }

            SettingsFilePath = Path.Combine(WorkFolder, SettingsFileName);
            CopyofSettingsFilePath = Path.Combine(DataFolder, CopyOfSettingsFileName);
            TempCopyofSettingsFilePath = Path.Combine(DataFolder, TempCopyOfSettingsFileName);
            eventSource.InfoMessage("NT Service's work folder : {0}, data folder : {1}", WorkFolder, DataFolder);
            _serviceSettings = this.GetDefaultServiceSettings();
        }

        /// <summary>
        /// Returns ServiceSettings.
        /// </summary>
        /// <returns></returns>
        public ServiceSettings GetSettings()
        {
            return this._serviceSettings;
        }

        /// <summary>
        /// This is called from TimerManager, when the new Settings.xml. If timer task was running then it would be called with the task finishes.
        /// </summary>
        public void UpdateSettings(string filePath)
        {       
            eventSource.InfoMessage("Updating Settings");
            ServiceSettings serviceSettings = ParseServiceSettings(filePath);
            CopyServiceSettings(serviceSettings);
            eventSource.InfoMessage("Loaded new settings : {0}", this._serviceSettings);
        }

        private void CopyServiceSettings(ServiceSettings serviceSettings)
        {
            this._serviceSettings.WUQuery = serviceSettings.WUQuery;
            this._serviceSettings.WUOperationRetryCount = serviceSettings.WUOperationRetryCount;
            this._serviceSettings.WUDelayBetweenRetriesInMinutes = serviceSettings.WUDelayBetweenRetriesInMinutes;
            this._serviceSettings.WUOperationTimeOutInMinutes = serviceSettings.WUOperationTimeOutInMinutes;
            this._serviceSettings.WURescheduleTimeInMinutes = serviceSettings.WURescheduleTimeInMinutes;
            this._serviceSettings.WURescheduleCount = serviceSettings.WURescheduleCount;
            this._serviceSettings.WUFrequency = serviceSettings.WUFrequency;
            this._serviceSettings.DisableWindowsUpdates = serviceSettings.DisableWindowsUpdates;
            this._serviceSettings.OperationTimeOutInMinutes = serviceSettings.OperationTimeOutInMinutes;
            this._serviceSettings.WUQueryCategoryIds = serviceSettings.WUQueryCategoryIds;
            this._serviceSettings.InstallWindowsOSOnlyUpdates = serviceSettings.InstallWindowsOSOnlyUpdates;
            this._serviceSettings.AcceptWindowsUpdateEula = serviceSettings.AcceptWindowsUpdateEula;            

            this._serviceSettings.ParseSettings();
        }
        
        private ServiceSettings ParseServiceSettings(string path)
        {                       
            if (!File.Exists(path))
            {
                eventSource.InfoMessage("Not able to find Settings.xml on path : {0}, loading default settings.", path);
                return this.GetDefaultServiceSettings();
            }

            eventSource.InfoMessage("Parsing Settings.xml from path : {0}", path);            
            XmlDocument doc = new XmlDocument();
            doc.Load(path);
            XmlNode node = doc.DocumentElement.SelectSingleNode("//*[local-name()='Section' and @Name='NTServiceSettings']");

            if (node == null)
            {
                return this.GetDefaultServiceSettings();
            }

            return this.GetServiceSettings(node);              
        }

        private ServiceSettings GetServiceSettings(XmlNode node)
        {
            ServiceSettings serviceSettings = new ServiceSettings();
            serviceSettings.WUQuery = this.GetParameter<string>(node, WUQueryName, DefaultWUQuery);
            serviceSettings.WUOperationRetryCount = this.GetParameter<long>(node, WUOperationRetryCountName, DefaultWUOperationRetryCount);
            serviceSettings.WUDelayBetweenRetriesInMinutes = this.GetParameter<long>(node, WUDelayBetweenRetriesInMinutesName, DefaultWUDelayBetweenRetriesInMinutes);
            serviceSettings.WUOperationTimeOutInMinutes = this.GetParameter<long>(node, WUOperationTimeOutInMinutesName, DefaultWUOperationTimeOutInMinutes);
            serviceSettings.WURescheduleTimeInMinutes = this.GetParameter<long>(node, WURescheduleTimeInMinutesName, DefaultWURescheduleTimeInMinutes);
            serviceSettings.WURescheduleCount = this.GetParameter<long>(node, WURescheduleCountName, DefaultWURescheduleCount);
            serviceSettings.WUFrequency = this.GetParameter<string>(node, WUFrequencyName, DefaultWUFrequency);
            serviceSettings.DisableWindowsUpdates = this.GetParameter<bool>(node, DisableAutoUpdateSettingInOSName, DefaultDisableAutoUpdateSettingInOS);
            serviceSettings.OperationTimeOutInMinutes = this.GetParameter<long>(node, OperationTimeOutInMinutesName, DefaultOperationTimeOutInMinutes);
            serviceSettings.InstallWindowsOSOnlyUpdates = this.GetParameter<bool>(node, InstallWindowsOSOnlyUpdateName, DefaultInstallWindowsOSOnlyUpdates);
            serviceSettings.WUQueryCategoryIds = this.GetParameter<string>(node, WUQueryCategoryIdsName, DefaultWUQueryCategoryIds);
            serviceSettings.AcceptWindowsUpdateEula = this.GetParameter<bool>(node, AcceptWindowsUpdateEulaName, DefaultAcceptWindowsUpdateEula);            

            serviceSettings.ParseSettings();

            return serviceSettings;
        }
        
        private T GetParameter<T>(XmlNode node, string parameterName, T defaultValue )
        {            
            XmlNode parameterNode = node.SelectSingleNode("//*[local-name()='Parameter' and @Name='"+ parameterName + "']");
            if(parameterNode == null)
            {
                return defaultValue;
            }

            XmlAttribute attr = parameterNode.Attributes["Value"];

            if (attr == null)
            {
                return defaultValue;
            }

            return (T) Convert.ChangeType(attr.Value, typeof(T));
        }

        private ServiceSettings GetDefaultServiceSettings()
        {
            ServiceSettings serviceSettings = new ServiceSettings();
            serviceSettings.WUQuery = DefaultWUQuery;
            serviceSettings.WUOperationRetryCount = DefaultWUOperationRetryCount;
            serviceSettings.WUDelayBetweenRetriesInMinutes = DefaultWUDelayBetweenRetriesInMinutes;
            serviceSettings.WUOperationTimeOutInMinutes = DefaultWUOperationTimeOutInMinutes;
            serviceSettings.WURescheduleTimeInMinutes = DefaultWURescheduleTimeInMinutes;
            serviceSettings.WURescheduleCount = DefaultWURescheduleCount;
            serviceSettings.WUFrequency = DefaultWUFrequency;
            serviceSettings.DisableWindowsUpdates = DefaultDisableAutoUpdateSettingInOS;
            serviceSettings.OperationTimeOutInMinutes = DefaultOperationTimeOutInMinutes;
            serviceSettings.InstallWindowsOSOnlyUpdates = DefaultInstallWindowsOSOnlyUpdates;
            serviceSettings.WUQueryCategoryIds = DefaultWUQueryCategoryIds;
            serviceSettings.AcceptWindowsUpdateEula = DefaultAcceptWindowsUpdateEula;            

            serviceSettings.ParseSettings();

            return serviceSettings;
        }
    }
}
