// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentNTService.Utility
{
    using Microsoft.Win32;
    using System.Text;
    using WUApiLib;
    using System;

    /// <summary>
    /// Utility which helps in getting and settings the Windows Update settings of a Windows based system
    /// </summary>
    public class WindowsAutoUpdateUtility
    {
        static string AURegPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
        private RegistryKey auKey;
        private readonly ServiceEventSource _eventSource = ServiceEventSource.Current;
        public WindowsAutoUpdateUtility()
        {
            if(!IsWindowsServer2012R2OrLower())
            {
                auKey = Registry.LocalMachine.OpenSubKey(AURegPath, true);
                if (auKey == null)
                {
                    _eventSource.InfoMessage("AU Registry did not exist on Machine . Creating new Registry to avoid failures.");
                    auKey = Registry.LocalMachine.CreateSubKey(AURegPath);
                }
            }

        }

        /// <summary>
        /// Sets the Windows automatic update option
        /// </summary>
        /// <param name="AUOptions">Automatic Update option to set<see cref="https://support.microsoft.com/help/328010/how-to-configure-automatic-updates-by-using-group-policy-or-registry-s"/>
        /// Default - 2: Notify of download and installation.
        /// </param>
        public void SetAUOptions(int AUOptions = 2)
        {
            this.auKey.SetValue("NoAutoUpdate", 0);
            this.auKey.SetValue("AUOptions", AUOptions);
        }

        public string LogCurrentAUValues()
        {
            StringBuilder result = new StringBuilder();
            var values = this.auKey.GetValueNames();
            foreach(string value in values)
            {
                result.AppendFormat("{0} = {1}\n", value, this.auKey.GetValue(value));
            }

            return result.ToString();
        }

        public void DisableAUThroughWUApi(AutomaticUpdates updates)
        {
            if (updates.Settings.NotificationLevel == AutomaticUpdatesNotificationLevel.aunlNotConfigured ||
                updates.Settings.NotificationLevel == AutomaticUpdatesNotificationLevel.aunlScheduledInstallation)
            {
                if (!updates.Settings.ReadOnly)
                {
                    updates.Settings.NotificationLevel = AutomaticUpdatesNotificationLevel.aunlNotifyBeforeDownload;
                    updates.Settings.Save();
                    return;
                }
            }
            else
            {
                return;
            }
        }

        public bool IsWindowsServer2012R2OrLower()
        {
            int majorVersion = Environment.OSVersion.Version.Major;
            int minorVersion = Environment.OSVersion.Version.Minor;
            if (majorVersion < 6 || ( majorVersion == 6 && minorVersion <= 3))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
