// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.Common
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.Serialization;
    using System.Xml;

    /// <summary>
    /// Type of WindowsUpdate operation
    /// </summary>
    public enum WindowsUpdateOperationType
    {
        SearchAndDownload = 0,
        Installation
    }


    /// <summary>
    /// Windows update details for a single update
    /// </summary>
    [DataContract]
    public struct WindowsUpdateDetail
    {
        /// <summary>
        /// Unique identifier for a windows update.
        /// </summary>
        [DataMember]
        public string UpdateId;

        /// <summary>
        /// Title of windows update
        /// </summary>
        [DataMember]
        public string Title;

        /// <summary>
        /// Description of the windows update
        /// </summary>
        [DataMember]
        public string Description;

        /// <summary>
        /// Operation result for the windows update
        /// </summary>
        [DataMember]
        public WuOperationResult ResultCode;
        
        /// <summary>
        /// Verbose description of <see cref="WindowsUpdateDetail"/>.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return String.Format("UpdateId : {0}, Title : {1} , Description : {2}, ResultCode : {3}", UpdateId, Title, Description, ResultCode);
        }
    }

    /// <summary>
    /// Result status for windows update operation
    /// </summary>
    public enum WuOperationResult
    {
        Succeeded,
        SucceededWithErrors,
        Failed,
        Aborted,
        AbortedWithTimeout
    }

    /// <summary>
    /// Windows update operation result which captures node details,
    /// type of operation,time of operation, result of operation and 
    /// brief description of all the windows update associated with the operation
    /// </summary>
    [DataContract]
    public class WindowsUpdateOperationResult
    {
        public WindowsUpdateOperationResult(String nodeName, DateTime operationTime, WuOperationResult operationResult, IList<WindowsUpdateDetail> updateDetails, WindowsUpdateOperationType operationType, string windowsUpdateQuery, string windowsUpdateFrequency, bool rebootRequired)
        {
            this.NodeName = nodeName;
            this.OperationTime = operationTime;
            this.UpdateDetails = updateDetails;
            this.OperationType = operationType;
            this.OperationResult = operationResult;
            this.WindowsUpdateFrequency = windowsUpdateFrequency;
            this.WindowsUpdateQuery = windowsUpdateQuery;
            this.RebootRequired = rebootRequired;
        }

        [DataMember]
        public WuOperationResult OperationResult { get; private set; }

        [DataMember]
        public String NodeName { get; set; }

        [DataMember]
        public DateTime OperationTime { get; private set; }

        [DataMember]
        public IList<WindowsUpdateDetail> UpdateDetails { get; private set; }

        [DataMember]
        public WindowsUpdateOperationType OperationType { get; private set; }

        [DataMember]
        public string WindowsUpdateQuery { get; private set; }

        [DataMember]
        public string WindowsUpdateFrequency { get; private set; }

        [DataMember]
        public bool RebootRequired { get; private set; }

        /// <summary>
        /// Utility to serialize the current operation result to a file
        /// </summary>
        /// <param name="filePath">Path of file where result is to be stored</param>
        public void Serialize(string filePath)
        {           
            using (FileStream writer = new FileStream(filePath, FileMode.Create))
            {
                DataContractSerializer ser =
                    new DataContractSerializer(typeof(WindowsUpdateOperationResult));
                ser.WriteObject(writer, this);
                writer.Close();
            }
        }

        /// <summary>
        /// Utility to deserialize the Windows update result stored in a file
        /// </summary>
        /// <param name="filePath">Path of the file which contains <see cref="WindowsUpdateOperationResult"/></param>
        /// <returns>Deserialized <see cref="WindowsUpdateOperationResult"/> object</returns>
        public static WindowsUpdateOperationResult Deserialize(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            FileStream fs = null;
            try
            {
                fs = new FileStream(filePath, FileMode.Open);
                using (XmlDictionaryReader reader = XmlDictionaryReader.CreateTextReader(fs, new XmlDictionaryReaderQuotas()))
                {
                    DataContractSerializer ser = new DataContractSerializer(typeof(WindowsUpdateOperationResult));

                    // Deserialize the data and read it from the instance.
                    WindowsUpdateOperationResult result = (WindowsUpdateOperationResult)ser.ReadObject(reader, true);
                    reader.Close();
                    fs.Close();
                    return result;
                }
            }
            finally
            {
                if (fs != null)
                {
                    fs.Dispose();
                }
            }
        }
        
        /// <summary>
        /// Verbose description of <see cref="WindowsUpdateOperationResult"/>.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string windowsUpdateDetails = string.Join(",\n", UpdateDetails.Select(i => i.ToString()).ToArray());
            return string.Format("WindowsUpdateOperationResult : OperationResult {0} , OperationTime {1}, OperationType {2}, UpdateDetails {3}, NodeName {4}, WindowsUpdateQuery {5}, WindowsUpdateFrequency {6}, RebootRequired {7}", 
                                  OperationResult, OperationTime, OperationType, windowsUpdateDetails, NodeName, WindowsUpdateQuery, WindowsUpdateFrequency, RebootRequired);
        }
    }
}
