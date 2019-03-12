---
services: service-fabric
platforms: .NET, windows
author: raunakpandya, brkhande
---

# Service-Fabric-POA
[![Build Status](https://dev.azure.com/ms/Service-Fabric-POA/_apis/build/status/Microsoft.Service-Fabric-POA?branchName=master)](https://dev.azure.com/ms/Service-Fabric-POA/_build/latest?definitionId=80&branchName=master)

Patch Orchestration Application(POA) is an Azure Service Fabric application that automates operating system patching on a Service Fabric cluster without downtime. This repo only contains code for orchestrating Windows operating system updates.

The patch orchestration app provides the following features:

- **Automatic operating system update installation**. Operating system updates are automatically downloaded and installed. Cluster nodes are rebooted as needed without cluster downtime.

- **Cluster-aware patching and health integration**. While applying updates, the patch orchestration app monitors the health of the cluster nodes. Cluster nodes are upgraded one node at a time or one upgrade domain at a time. If the health of the cluster goes down due to the patching process, patching is stopped to prevent aggravating the problem.

## Internal details of the app

The patch orchestration app is composed of the following subcomponents:

- **Coordinator Service**: This stateful service is responsible for:
    - Coordinating the Windows Update job on the entire cluster.
    - Storing the result of completed Windows Update operations.
- **Node Agent Service**: This stateless service runs on all Service Fabric cluster nodes. The service is responsible for:
    - Bootstrapping the Node Agent NTService.
    - Monitoring the Node Agent NTService.
- **Node Agent NTService**: This Windows NT service runs at a higher-level privilege (SYSTEM). In contrast, the Node Agent Service and the Coordinator Service run at a lower-level privilege (NETWORK SERVICE). The service is responsible for performing the following Windows Update jobs on all the cluster nodes:
    - Disabling automatic Windows Update on the node.
    - Downloading and installing Windows Update according to the policy the user has provided.
    - Restarting the machine post Windows Update installation.
    - Uploading the results of Windows updates to the Coordinator Service.
    - Reporting health reports in case an operation has failed after exhausting all retries.

For more details, please visit this [link](https://docs.microsoft.com/azure/service-fabric/service-fabric-patch-orchestration-application)

## Developer Help & Documentation

### Build Application
To build the application you need to first setup the machine for Service Fabric application development. 

[Setup your development environment with Visual Studio 2017](https://docs.microsoft.com/azure/service-fabric/service-fabric-get-started).

Once setup, make a clone of this repo. Then, open PowerShell command prompt and move to the root of this repo and run `build.ps1` script.

```PowerShell
PS E:\SF-POS> .\build.ps1
```

It should produce an output like below.
```
Source root is E:\SF-POS\src\PatchOrchestrationApplication\PatchOrchestrationApplication
Restoring NuGet package Microsoft.VisualStudio.Azure.Fabric.MSBuild.1.6.7.
Adding package 'Microsoft.VisualStudio.Azure.Fabric.MSBuild.1.6.7' to folder 'E:\SF-POS\packages'
Added package 'Microsoft.VisualStudio.Azure.Fabric.MSBuild.1.6.7' to folder 'E:\SF-POS\packages'

NuGet Config files used:
    C:\Users\brkhande\AppData\Roaming\NuGet\NuGet.Config

Feeds used:
    C:\Users\brkhande\AppData\Local\NuGet\Cache
    C:\Users\brkhande\.nuget\packages\
    https://api.nuget.org/v3/index.json

Installed:
    1 package(s) to packages.config projects
Changing the working directory to E:\SF-POS\src\PatchOrchestrationApplication\PatchOrchestrationApplication
Using msbuild from C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise\MSBuild\15.0\Bin\MSBuild.exe
  Restoring packages for E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentSFUtility\src\NodeAgentSFUtility.csproj...
  Restoring packages for E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentService\src\NodeAgentService.csproj...
  Restoring packages for E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentNTService\src\NodeAgentNTService.csproj...
  Restoring packages for E:\SF-POS\src\PatchOrchestrationApplication\CoordinatorService\src\CoordinatorService.csproj...
  Restoring packages for E:\SF-POS\src\PatchOrchestrationApplication\TelemetryLib\src\TelemetryLib.csproj...
  Installing Newtonsoft.Json 10.0.2.
  Installing Microsoft.VisualStudio.Azure.Fabric.MSBuild 1.6.7.
  Installing Microsoft.ServiceFabric 5.4.145.
  Installing Microsoft.ApplicationInsights 2.2.0.
  Installing Microsoft.ServiceFabric.Data 2.4.145.
  Installing Microsoft.ServiceFabric.Services 2.4.145.
  Installing Microsoft.AspNet.WebApi.Client 5.2.3.
  Installing Unity 4.0.1.
  Installing Microsoft.AspNet.WebApi.Core 5.2.3.
  Installing Unity.WebAPI 5.2.3.
  Installing Owin 1.0.
  Installing Microsoft.Owin.Hosting 2.0.2.
  Installing Microsoft.Owin 2.0.2.
  Installing CommonServiceLocator 1.3.
  Installing Microsoft.AspNet.WebApi.Owin 5.2.3.
  Installing Microsoft.Owin.Host.HttpListener 2.0.2.
  Installing Newtonsoft.Json 6.0.4.
  Generating MSBuild file E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentService\src\obj\NodeAgentService.csproj.nuget.g.props.
  Generating MSBuild file E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentSFUtility\src\obj\NodeAgentSFUtility.csproj.nuget.g.props.
  Generating MSBuild file E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentNTService\src\obj\NodeAgentNTService.csproj.nuget.g.props.
  Generating MSBuild file E:\SF-POS\src\PatchOrchestrationApplication\CoordinatorService\src\obj\CoordinatorService.csproj.nuget.g.props.
  Generating MSBuild file E:\SF-POS\src\PatchOrchestrationApplication\TelemetryLib\src\obj\TelemetryLib.csproj.nuget.g.props.
  Generating MSBuild file E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentService\src\obj\NodeAgentService.csproj.nuget.g.targets.
  Generating MSBuild file E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentSFUtility\src\obj\NodeAgentSFUtility.csproj.nuget.g.targets.
  Generating MSBuild file E:\SF-POS\src\PatchOrchestrationApplication\CoordinatorService\src\obj\CoordinatorService.csproj.nuget.g.targets.
  Generating MSBuild file E:\SF-POS\src\PatchOrchestrationApplication\TelemetryLib\src\obj\TelemetryLib.csproj.nuget.g.targets.
  Generating MSBuild file E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentNTService\src\obj\NodeAgentNTService.csproj.nuget.g.targets.
  Restore completed in 3.77 sec for E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentService\src\NodeAgentService.csproj.
  Restore completed in 3.77 sec for E:\SF-POS\src\PatchOrchestrationApplication\CoordinatorService\src\CoordinatorService.csproj.
  Restore completed in 3.77 sec for E:\SF-POS\src\PatchOrchestrationApplication\TelemetryLib\src\TelemetryLib.csproj.
  Restore completed in 3.77 sec for E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentNTService\src\NodeAgentNTService.csproj.
  Restore completed in 3.77 sec for E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentSFUtility\src\NodeAgentSFUtility.csproj.
  Restore completed in 6.53 ms for E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentSFUtility\src\NodeAgentSFUtility.csproj.
  Restore completed in 6.57 ms for E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentService\src\NodeAgentService.csproj.
  Restore completed in 6.12 ms for E:\SF-POS\src\PatchOrchestrationApplication\TelemetryLib\src\TelemetryLib.csproj.
  Restore completed in 6.56 ms for E:\SF-POS\src\PatchOrchestrationApplication\CoordinatorService\src\CoordinatorService.csproj.
  Restore completed in 6.59 ms for E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentNTService\src\NodeAgentNTService.csproj.
  TelemetryLib -> E:\SF-POS\src\PatchOrchestrationApplication\TelemetryLib\src\bin\Release\TelemetryLib.dll
  CoordinatorService -> E:\SF-POS\src\PatchOrchestrationApplication\CoordinatorService\src\bin\Release\CoordinatorService.exe
  NodeAgentNTService -> E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentNTService\src\bin\Release\NodeAgentNTService.exe
  NodeAgentSFUtilityDll -> E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentSFUtility\src\bin\Release\CommandProcessor.dll
  NodeAgentSFUtility -> E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentSFUtility\src\bin\Release\NodeAgentSFUtility.exe
  NodeAgentService -> E:\SF-POS\src\PatchOrchestrationApplication\NodeAgentService\src\bin\Release\NodeAgentService.exe
  PatchOrchestrationApplication -> E:\SF-POS\src\PatchOrchestrationApplication\PatchOrchestrationApplication\pkg\Release
```

By default the script will create a `release` package of the application in `out\Release` folder. 

### Deploy Application

- Open PowerShell command prompt and go to the root of the repository.

- Connect to the Service Fabric Cluster where you want to deploy the application using [`Connect-ServiceFabricCluster`](https://docs.microsoft.com/en-us/powershell/module/servicefabric/connect-servicefabriccluster?view=azureservicefabricps) PowerShell command. 

- Deploy the application using the following PowerShell command.

  ```PowerShell
  . out\Release\Deploy.ps1 -ApplicationPackagePath 'out\Release\PatchOrchestrationApplication' -ApplicationParameter @{ }
  ```

- Deploy the application using the following PowerShell command, in case you want to change the application parameters default values. You can do that as shown below:

  ```PowerShell
  . out\Release\Deploy.ps1 -ApplicationPackagePath 'out\Release\PatchOrchestrationApplication'  -ApplicationParameter @{ 'WURescheduleCount'='10'; 'WUFrequency'= 'Weekly, Tuesday, 12:22:32'; }
  ```
> [!NOTE]
> The above deployment procedure should only be used in case one wants to test changes made to this application. For production/test environment, one should always use the officially released version of the application. Application along with installation scripts can be downloaded from [Archive link](https://go.microsoft.com/fwlink/?linkid=869566). Deployment steps for this application can be found [here](https://docs.microsoft.com/azure/service-fabric/service-fabric-patch-orchestration-application#deploy-the-app)


# Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.microsoft.com.

When you submit a pull request, a CLA-bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Ideas and Improvements

We encourage community feedback and contribution to improve this application. To make any contribution, our contribution guidelines needs to be followed. If you have any new idea, please file an issue for that.

### Contribution Guidelines:
Please create a branch and push your changes to that and then, create a pull request for that change.
These is the check list that would be required to complete, for pushing your change to master branch.

1. Create Service Fabric cluster on Azure. You can find the steps for creating Service Fabric cluster on Azure [here](https://docs.microsoft.com/azure/service-fabric/service-fabric-cluster-creation-via-portal)
2. Build the application with your change.
3. Deploy the application and validate that the application is healthy and working as expected.
4. Resolve all the comments from owners.
