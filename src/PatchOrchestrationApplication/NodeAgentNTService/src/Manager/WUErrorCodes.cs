// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentNTService.Manager
{
    struct WUErrorCodes
    {
        #region Download Errors
        /// <summary>
        /// The Windows Update Agent (WUA) is not initialized.
        /// </summary>
        public const uint WU_E_NOT_INITIALIZED = 0x80240004;

        /// <summary>
        /// The Windows Update Agent (WUA) does not have updates in the collection.
        /// </summary>
        public const uint WU_E_NO_UPDATE = 0x80240024;

        /// <summary>
        /// The computer cannot access the update site.
        /// </summary>
        public const uint WU_E_INVALID_OPERATION = 0x80240036;
        #endregion        
    }
}
