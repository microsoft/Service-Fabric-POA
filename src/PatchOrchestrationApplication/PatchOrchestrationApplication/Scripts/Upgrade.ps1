##
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.
##
Param
(
    [String]
    $ApplicationPackagePath = "PatchOrchestrationApplication",

    [String]
    $ImageStoreConnectionString = "fabric:ImageStore",

    [string]
    $ApplicationVersion = "1.4.7",
	
    [hashtable]
    $ApplicationParameters = @{},

    [string]
    $ApplicationInstanceName = "fabric:/PatchOrchestrationApplication"
)

Copy-ServiceFabricApplicationPackage -ApplicationPackagePath $ApplicationPackagePath -ImageStoreConnectionString $ImageStoreConnectionString
Register-ServiceFabricApplicationType PatchOrchestrationApplication
Start-ServiceFabricApplicationUpgrade -ApplicationName $ApplicationInstanceName -ApplicationTypeVersion $ApplicationVersion -FailureAction Rollback -Monitored -ApplicationParameter $ApplicationParameters
