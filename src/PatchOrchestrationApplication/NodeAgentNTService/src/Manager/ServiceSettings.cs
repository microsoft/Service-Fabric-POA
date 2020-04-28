// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentNTService.Manager
{
    using System;
    using System.Linq;    
    using System.Collections.Generic;

    enum Frequency
    {
        None = 0,
        Once = 1,
        Daily = 2,
        Weekly = 3,
        Monthly = 4,
        Hourly = 5,
        MonthlyByWeekAndDay = 6
    }

    /// <summary>
    /// This class contains all the settings parsed from Settings.xml.
    /// </summary>
    class ServiceSettings
    {
        private const int MaxSupportedDayOfMonth = 28;
        private const string WindowsOSOnlyCategoryId = "6964aab4-c5b5-43bd-a17d-ffb4346a8e1d";
        /// <summary>
        /// True: Disable automatic windows updates on system.
        /// False: Keep the settings as it is.
        /// By default this is set to true.
        /// </summary>
        public bool DisableWindowsUpdates { get; set; }

        /// <summary>
        /// This query is needed by UpdateSearcher API of WindowsUpdateAgent. 
        /// </summary>
        public string WUQuery { get; set; }

        /// <summary>
        /// The retry count for operations search/download/install.
        /// </summary>
        public long WUOperationRetryCount { get; set; }

        /// <summary>
        /// The delay between reties.
        /// </summary>
        public long WUDelayBetweenRetriesInMinutes { get; set; }

        /// <summary>
        /// The timeout period for operations search/download/install.
        /// </summary>
        public long WUOperationTimeOutInMinutes { get; set; }

        /// <summary>
        /// The time period between rescheduling search-download-install operation for retries.
        /// </summary>
        public long WURescheduleTimeInMinutes { get; set; }

        /// <summary>
        /// The rescheduling count.
        /// </summary>
        public long WURescheduleCount { get; set; }

        /// <summary>
        /// This timeout is used for interacting with SF utility process.
        /// </summary>
        public long OperationTimeOutInMinutes { get; set; }

        /// <summary>
        /// This flag will only allow windows OS updates to be installed.
        /// </summary>
        public bool InstallWindowsOSOnlyUpdates { get; set; }

        /// <summary>
        /// The specific category of updates that need to be installed.
        /// </summary>
        public string WUQueryCategoryIds { get; set; }

        /// <summary>
        /// The frequency for installing Windows Update.
        /// The format and Possible Values : 
        /// 1. Monthly,5,12:22:32
        /// 2. MonthlyByWeekAndDay,2,Friday,21:00:00
        /// 3. Weekly,Tuesday,12:22:32
        /// 4. Daily,12:22:32 
        /// 5. Once,12-12-2017,12:22:32
        /// 6. None
        /// </summary>   
        public string WUFrequency { get; set; }

        /// <summary>
        /// By setting this flag, we'll accept Eula for windows update on behalf of owner of machine.
        /// </summary>
        public bool AcceptWindowsUpdateEula { get; set; }
        
        /// <summary>
        /// The frequency of installing Windows Updates.
        /// </summary>
        public Frequency Frequency { get; private set; }

        public DayOfWeek DayOfWeek { get; private set; }

        public int WeekOfMonth { get; private set; }

        public TimeSpan Time { get; private set; }

        public DateTime Date { get; private set; }

        public bool IsLastDayOfMonth { get; private set; }

        /// <summary>
        /// Whitelist of category ID's, setting this will restrict POA to install updates only for these CategoryID's
        /// Null or Empty list implies this filter is not set, and its ok to install all updates.
        /// </summary>
        public List<string> CategoryIds { get; private set; }

        public long HourlyFrequencyInMinutes { get; private set; }        

        public void ParseSettings()
        {
            ParseWUFrequency();
            ParseCategoryIds();
        }

        private void ParseCategoryIds()
        {
            CategoryIds = WUQueryCategoryIds.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries).Select(str => str.Trim()).ToList();
            if (InstallWindowsOSOnlyUpdates)
            {
                CategoryIds.Add(WindowsOSOnlyCategoryId);
            }
        }

        private void ParseWUFrequency()
        {
            string[] arr = WUFrequency.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries).Select(str => str.Trim()).ToArray();
            if(arr.Count() == 0)
            {
                throw new ArgumentException("Illegal WUFrequency Parameter : "+ WUFrequency);
            }

            this.Frequency = (Frequency)Enum.Parse(typeof(Frequency), arr[0]);
            DateTime currentDateTime = DateTime.UtcNow;

            switch (this.Frequency)
            {
                case Frequency.Monthly:
                    if (arr.Count() != 3)
                    {
                        throw new ArgumentException("Illegal WUFrequency Parameter : " + WUFrequency);
                    }

                    if (arr[1].Trim().Equals("Last", StringComparison.InvariantCultureIgnoreCase))
                    {
                        this.IsLastDayOfMonth = true;
                    }
                    else
                    {
                        int dayOfMonth = (int) Convert.ChangeType(arr[1], typeof(int));
                        if (dayOfMonth > MaxSupportedDayOfMonth)
                        {
                            throw new ArgumentException("Illegal WUFrequency Parameter : " + WUFrequency + ". The day of month should be between 1 to 28.");
                        }
                        this.Date = new DateTime(currentDateTime.Year, currentDateTime.Month, dayOfMonth, 0, 0, 0);
                        this.Date = this.Date.Date + TimeSpan.Parse(arr[2]);
                    }
                    break;

                case Frequency.MonthlyByWeekAndDay:
                    if (arr.Count() != 4)
                    {
                        throw new ArgumentException("Illegal WUFrequency Parameter : " + WUFrequency);
                    }

                    this.WeekOfMonth = (int)Convert.ChangeType(arr[1], typeof(int));
                    this.DayOfWeek = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), arr[2]);
                    this.Time = TimeSpan.Parse(arr[3]);

                    if (this.WeekOfMonth < 1 || this.WeekOfMonth > 4)
                    {
                        throw new ArgumentException("Illegal WUFrequency Parameter : " + WUFrequency + ". The WeekOfMonth should be between 1 to 4.");
                    }
                    break;

                case Frequency.Weekly:
                    if (arr.Count() != 3)
                    {
                        throw new ArgumentException("Illegal WUFrequency Parameter : " + WUFrequency);
                    }
                    this.DayOfWeek = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), arr[1]);
                    this.Date = currentDateTime;
                    this.Date = this.Date.Date + TimeSpan.Parse(arr[2]);                    
                    break;

                case Frequency.Daily:
                    if (arr.Count() != 2)
                    {
                        throw new ArgumentException("Illegal WUFrequency Parameter : " + WUFrequency);
                    }
                    this.Date = currentDateTime;
                    this.Date = this.Date.Date + TimeSpan.Parse(arr[1]);                    
                    break;

                case Frequency.Once:
                    if (arr.Count() != 3)
                    {
                        throw new ArgumentException("Illegal WUFrequency Parameter : " + WUFrequency);
                    }
                    this.Date = DateTime.Parse(arr[1]);
                    this.Date = this.Date.Date + TimeSpan.Parse(arr[2]);
                    break;

                case Frequency.None:
                    if (arr.Count() != 1)
                    {
                        throw new ArgumentException("Illegal WUFrequency Parameter : " + WUFrequency);
                    }
                    break;

                case Frequency.Hourly:
                    if (arr.Count() != 2)
                    {
                        throw new ArgumentException("Illegal WUFrequency Parameter : " + WUFrequency);
                    }                    
                    this.HourlyFrequencyInMinutes = (long)Convert.ChangeType(arr[1], typeof(long));
                    break;

                default:
                    throw new ArgumentException("Illegal WUFrequency Parameter : " + WUFrequency);
            }
        }
        
        public override string ToString()
        {
            return "WUQuery = " + WUQuery + " , WUOperationRetryCount = " + WUOperationRetryCount +
                " , WUDelayBetweenRetriesInMinutes = " + WUDelayBetweenRetriesInMinutes + " WUOperationTimeOutInMinutes = " + WUOperationTimeOutInMinutes +
                " , WURescheduleTimeInMinutes = " + WURescheduleTimeInMinutes + " , WUFrequency = " + WUFrequency +
                " , InstallWindowsOSOnlyUpdates = " + InstallWindowsOSOnlyUpdates + " , WUQueryCategoryIds = " + WUQueryCategoryIds +
                " , AcceptWindowsUpdateEula = " + AcceptWindowsUpdateEula + " , OperationTimeOutInMinutes = " + OperationTimeOutInMinutes +
                " , WURescheduleCount = " + WURescheduleCount + " , DisableWindowsUpdates = " + DisableWindowsUpdates;
        }     
    }
}
