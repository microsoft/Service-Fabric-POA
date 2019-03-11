// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentNTService
{
    using System.ServiceProcess;
    using Service;

    class Program
    {
        static void Main(string[] args)
        {            
            string nodeName = args[0];
            string applicationName = args[1];
            ServiceBase.Run(new POAService(nodeName, applicationName));
        }
    }
}
