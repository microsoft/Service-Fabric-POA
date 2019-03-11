// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.Common
{
    using System;
    using System.Runtime.Serialization;

    /// <summary>
    /// Executor Data is used to store the state of an executing repair task.
    /// </summary>
    [DataContract]
    public class ExecutorDataForRmTask
    {
        [DataMember] public NodeAgentSfUtilityExitCodes ExecutorSubState;
        [DataMember] public int ExecutorTimeoutInMinutes;
        [DataMember] public DateTime RestartRequestedTime;
    }

    /// <summary>
    /// Executor Data which would be passed on to Nt Service
    /// </summary>
    [DataContract]
    public class ExecutorDataForNtService
    {
        [DataMember]
        public DateTime? ApprovedDateTime;
        [DataMember]
        public int ExecutorTimeoutInMinutes;
    }
}