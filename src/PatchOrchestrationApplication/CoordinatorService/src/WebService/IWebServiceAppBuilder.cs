// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.CoordinatorService.WebService
{
    using global::Owin;

    /// <summary>
    /// Interface for configuring the WebService 
    /// </summary>
    public interface IWebServiceAppBuilder
    {
        void ConfigureApp(IAppBuilder appBuilder);
    }
}