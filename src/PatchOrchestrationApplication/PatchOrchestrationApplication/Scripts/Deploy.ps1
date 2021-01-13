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
    $ApplicationVersion = "1.4.9",
	
    [hashtable]
    $ApplicationParameters = @{}
)

Copy-ServiceFabricApplicationPackage -ApplicationPackagePath $ApplicationPackagePath -ImageStoreConnectionString $ImageStoreConnectionString
Register-ServiceFabricApplicationType PatchOrchestrationApplication
New-ServiceFabricApplication fabric:/PatchOrchestrationApplication PatchOrchestrationApplicationType $ApplicationVersion -ApplicationParameter $ApplicationParameters
