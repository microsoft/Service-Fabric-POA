// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.CoordinatorService.WebService
{
    using System.Web.Http;
    using Microsoft.Practices.Unity;
    using Microsoft.ServiceFabric.Data;
    using Unity.WebApi;

    /// <summary>
    /// Configures dependency injection for Controllers using a Unity container. 
    /// </summary>
    public static class UnityConfig
    {
        public static void RegisterComponents(HttpConfiguration config, IReliableStateManager stateManager)
        {
            UnityContainer container = new UnityContainer();
            container.RegisterType<DefaultController>(
                new TransientLifetimeManager(),
                new InjectionConstructor(stateManager));
            config.DependencyResolver = new UnityDependencyResolver(container);
        }
    }
}
