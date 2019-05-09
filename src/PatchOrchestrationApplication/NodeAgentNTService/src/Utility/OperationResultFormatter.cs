// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentNTService.Utility
{
    using System;    
    using System.Collections.Generic;
    using Manager;
    using ServiceFabric.PatchOrchestration.Common;
    using WUApiLib;    

    /// <summary>
    /// Formats operation result.
    /// </summary>
    class OperationResultFormatter
    {
        private readonly string _nodeName;
        private readonly ServiceSettings _serviceSettings;

        public OperationResultFormatter(string nodeName, ServiceSettings serviceSettings)
        {
            this._nodeName = nodeName;
            this._serviceSettings = serviceSettings;
        }

        /// <summary>
        /// Formats search and download operation result.
        /// </summary>
        /// <param name="operationResultCode">Operation result code</param>
        /// <param name="updateCollectionWrapper">Collection from which the formatted result will be created.</param>
        /// <returns></returns>
        public WindowsUpdateOperationResult FormatSearchAndDownloadResult(OperationResultCode operationResultCode, WUCollectionWrapper updateCollectionWrapper, DateTime operationStartTime)
        {
            return FormatOperationResult(operationResultCode, updateCollectionWrapper, WindowsUpdateOperationType.SearchAndDownload, this._serviceSettings.WUQuery, this._serviceSettings.WUFrequency, operationStartTime);
        }

        /// <summary>
        /// Create Dummy Operation Result. This result is used when search is completed with zero result.
        /// </summary>        
        /// <returns></returns>
        public WindowsUpdateOperationResult CreateInstallationDummyResult(DateTime operationStartTime)
        {
            return FormatOperationResult(OperationResultCode.orcSucceeded, null, WindowsUpdateOperationType.Installation, this._serviceSettings.WUQuery, this._serviceSettings.WUFrequency, operationStartTime);
        }

        /// <summary>
        /// Create Dummy Operation Result. This result is used when search is completed with zero result.
        /// </summary>        
        /// <returns></returns>
        public WindowsUpdateOperationResult CreateSearchAndDownloadDummyResult(DateTime operationStartTime)
        {
            return FormatOperationResult(OperationResultCode.orcSucceeded, null, WindowsUpdateOperationType.SearchAndDownload, this._serviceSettings.WUQuery, this._serviceSettings.WUFrequency, operationStartTime);
        }

        /// <summary>
        /// Formats installation operation result.
        /// </summary>
        /// <param name="operationResultCode">Operation result code</param>
        /// <param name="updateCollectionWrapper">Collection from which the formatted result will be created.</param>
        /// <returns></returns>

        public WindowsUpdateOperationResult FormatInstallationResult(OperationResultCode operationResultCode, WUCollectionWrapper updateCollectionWrapper, DateTime operationStartTime)
        {
            return FormatOperationResult(operationResultCode, updateCollectionWrapper, WindowsUpdateOperationType.Installation, this._serviceSettings.WUQuery, this._serviceSettings.WUFrequency, operationStartTime);
        }

        private WindowsUpdateOperationResult FormatOperationResult(OperationResultCode operationResultCode, WUCollectionWrapper updateCollectionWrapper, WindowsUpdateOperationType operationType, string wuQuery, string wuFrequency, DateTime operationStartTime)
        {
            bool rebootRequired = false;
            IList<WindowsUpdateDetail> details = new List<WindowsUpdateDetail>();
            if (updateCollectionWrapper != null && updateCollectionWrapper.Collection != null)
            {
                foreach (WUUpdateWrapper item in updateCollectionWrapper.Collection.Values)
                {
                    WindowsUpdateDetail updateDetail = new WindowsUpdateDetail();
                    updateDetail.UpdateId = item.Update.Identity.UpdateID;
                    updateDetail.Title = item.Update.Title;
                    updateDetail.Description = item.Update.Description;
                    bool operation = operationType.Equals(WindowsUpdateOperationType.Installation)
                        ? item.IsInstalled
                        : item.IsDownloaded;

                    updateDetail.ResultCode = MatchOperationResult(operation);
                    updateDetail.HResult = item.HResult;

                    details.Add(updateDetail);

                    rebootRequired = !rebootRequired ? item.Update.RebootRequired : rebootRequired;
                }
            }
            return new WindowsUpdateOperationResult(this._nodeName, DateTime.UtcNow, MatchOperationResult(operationResultCode), details, operationType, wuQuery, wuFrequency, rebootRequired, operationStartTime);
        }

        private WuOperationResult MatchOperationResult(OperationResultCode operationResultCode)
        {
            switch (operationResultCode)
            {
                case OperationResultCode.orcAborted:
                    return WuOperationResult.Aborted;

                case OperationResultCode.orcFailed:
                    return WuOperationResult.Failed;
                                                    
                case OperationResultCode.orcSucceeded:
                    return WuOperationResult.Succeeded;

                case OperationResultCode.orcSucceededWithErrors:
                    return WuOperationResult.SucceededWithErrors;

                default:
                    return WuOperationResult.Failed;
            }            
        }

        private WuOperationResult MatchOperationResult(bool operationResultCode)
        {
            switch (operationResultCode)
            {                
                case false:
                    return WuOperationResult.Failed;

                case true:
                    return WuOperationResult.Succeeded;
                    
                default:
                    return WuOperationResult.Failed;
            }
        }
    }

    class WUCollectionWrapper
    {
        public Dictionary<string, WUUpdateWrapper> Collection { get; private set; }

        public WUCollectionWrapper()
        {
            Collection = new Dictionary<string, WUUpdateWrapper>();            
        }

        public void Add(IUpdate2 update)
        {
            Collection.Add(update.Identity.UpdateID, new WUUpdateWrapper(update, update.IsDownloaded, update.IsInstalled));
        }   
    }

    class WUUpdateWrapper
    {
        public IUpdate2 Update { get; private set; }
        public bool IsDownloaded { get; set; }
        public bool IsInstalled { get; set; }
        public int HResult { get; set; } = 0;

        public WUUpdateWrapper(IUpdate2 update, bool isDownloaded, bool isInstalled)
        {
            Update = update;
            IsDownloaded = isDownloaded;
            IsInstalled = isInstalled;
        }

        public override string ToString()
        {
            return string.Format("update object: {0} , IsDownloaded : {1}, IsInstalled : {2}, HResult", Update, IsDownloaded, IsInstalled, HResult);
        }
    }
}
