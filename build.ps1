##
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.
##

##
#  Builds the source code and generates application package.
#  You can also open the solution file in Visual Studio 2017 and build.
##

param
(
    # Configuration to build.
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = "Release",

    # Platform to build for. 
    [ValidateSet('clean', 'rebuild')]
    [string]$Target = "rebuild",

    # msbuild verbosity level.
    [ValidateSet('quiet','minimal', 'normal', 'detailed', 'diagnostic')]
    [string]$Verbosity = 'minimal',

    # path to msbuild
    [string]$MSBuildFullPath,

    #CreateNugetPackage
    [switch]$CreateNugetPackageOnly,

    # AppInsightsKey
    [string]$AppInsightsKey = "",

    # DelaySign
    [bool]$DelaySign
)

$presentWorkingDirectory= Get-Location
$ErrorActionPreference = "Stop"
$PSScriptRoot = Split-Path -Parent -Path $MyInvocation.MyCommand.Definition
$NugetFullPath = join-path $PSScriptRoot "nuget.exe"
$SrcRoot = join-path $PSScriptRoot "src\PatchOrchestrationApplication\PatchOrchestrationApplication"
$PackageConfigPath = join-path $PSScriptRoot "src\PatchOrchestrationApplication\PatchOrchestrationApplication\packages.config"
$packagesDirectory = join-path $PSScriptRoot "packages"
$nuprojPackagesConfigPath = join-path $PSScriptRoot "src\PatchOrchestrationApplication\PatchOrchestrationApplication\NugetPackage\packages.config"
$nuprojPath = join-path $PSScriptRoot "src\PatchOrchestrationApplication\PatchOrchestrationApplication\NugetPackage"
$nugetConfigFilePath = join-path $PSScriptRoot "nuget.config"


if ($Target -eq "rebuild") {
    $buildTarget = "restore;clean;rebuild;package"
} elseif ($Target -eq "clean") {
    $buildTarget = "clean"
}

if($MSBuildFullPath -ne "")
{
    if (!(Test-Path $MSBuildFullPath))
    {
        throw "Unable to find MSBuild at the specified path, run the script again with correct path to msbuild."
    }
}

# msbuild path not provided, find msbuild for VS2017
if($MSBuildFullPath -eq "")
{
    if (${env:VisualStudioVersion} -eq "15.0" -and ${env:VSINSTALLDIR} -ne "")
    {
        $MSBuildFullPath = join-path ${env:VSINSTALLDIR} "MSBuild\15.0\Bin\MSBuild.exe"
    }
}

if($MSBuildFullPath -eq "")
{
    if (Test-Path "env:\ProgramFiles(x86)")
    {
        $progFilesPath =  ${env:ProgramFiles(x86)}
    }
    elseif (Test-Path "env:\ProgramFiles")
    {
        $progFilesPath =  ${env:ProgramFiles}
    }

    $VS2017InstallPath = join-path $progFilesPath "Microsoft Visual Studio\2017"
    $versions = 'Community', 'Professional', 'Enterprise'

    foreach ($version in $versions)
    {
        $VS2017VersionPath = join-path $VS2017InstallPath $version
        $MSBuildFullPath = join-path $VS2017VersionPath "MSBuild\15.0\Bin\MSBuild.exe"

        if (Test-Path $MSBuildFullPath)
        {
            break
        }
    }

    if (!(Test-Path $MSBuildFullPath))
    {
        Write-Host "Visual Studio 2017 installation not found in ProgramFiles, trying to find install path from registry."
        if(Test-Path -Path HKLM:\SOFTWARE\WOW6432Node)
        {
            $VS2017VersionPath = Get-ItemProperty (Get-ItemProperty -Path HKLM:\SOFTWARE\WOW6432Node\Microsoft\VisualStudio\SxS\VS7 -Name "15.0")."15.0"
        }
        else
        {
            $VS2017VersionPath = Get-ItemProperty (Get-ItemProperty -Path HKLM:\SOFTWARE\Microsoft\VisualStudio\SxS\VS7 -Name "15.0")."15.0"
        }

        $MSBuildFullPath = join-path $VS2017VersionPath "MSBuild\15.0\Bin\MSBuild.exe"
    }
}

if (!(Test-Path $MSBuildFullPath))
{
    throw "Unable to find MSBuild installed on this machine. Please install Visual Studio 2017 or if its installed at non-default location, provide the full ppath to msbuild using -MSBuildFullPath parameter."
}

if($CreateNugetPackageOnly)
{
    $nugetNuProjArgs = @(
    "restore",
    "$nuprojPackagesConfigPath",
    "-PackagesDirectory",
    "$packagesDirectory",
    "-ConfigFile",
    "$nugetConfigFilePath")

    & $NugetFullPath $nugetNuProjArgs
    if ($lastexitcode -ne 0) {
        Set-location -Path $PSScriptRoot
        throw ("Failed " + $NugetFullPath + " " + $nugetNuProjArgs)
    }
    Write-Output "Changing the working directory to $nuprojPath"
    Set-location -Path $nuprojPath
    $msbuildArgs = @(
        "/nr:false", 
        "/nologo", 
        "/t:Build", 
        "/verbosity:$verbosity",  
        "/property:RequestedVerbosity=$verbosity",
        "/property:Configuration=$configuration",
        "/property:RestoreConfigFile=$nugetConfigFilePath",
        "/property:DelaySign=$DelaySign",
        $args)
    & $msbuildFullPath $msbuildArgs
}
else {
    Set-location -Path $SrcRoot
    Write-Host "Source root is $srcRoot"

    $nugetArgs = @(
        "restore",
        "$PackageConfigPath",
        "-PackagesDirectory",
        "$packagesDirectory",
        "-ConfigFile",
        "$nugetConfigFilePath")

    & $NugetFullPath $nugetArgs
    if ($lastexitcode -ne 0) {
        Set-location -Path $PSScriptRoot
        throw ("Failed " + $NugetFullPath + " " + $nugetArgs)
    }
    Write-Output "Changing the working directory to $srcRoot"
    Set-location -Path $srcRoot
    Write-Output "Using msbuild from $msbuildFullPath"
    $msbuildArgs = @(
        "/nr:false", 
        "/nologo", 
        "/t:$buildTarget", 
        "/verbosity:$verbosity",  
        "/property:RequestedVerbosity=$verbosity", 
        "/property:Configuration=$configuration",
        "/property:RestoreConfigFile=$nugetConfigFilePath",
        "/property:AppInsightsKey=$AppInsightsKey",
        "/property:RestorePackagesPath=$packagesDirectory",
        "/property:DelaySign=$DelaySign",
        $args)
    & $msbuildFullPath $msbuildArgs
}

# update arm app template files with current version
$commonProps = '.\src\properties\common.props'
$templateJson = '.\arm\template.json'
$templateParametersJson = '.\arm\template.parameters.json'
$buildVersion = $null

if((test-path $commonProps)) {
    write-host "reading $commonProps"
    $xml = [xml]::new()
    $xml.Load($commonProps)
    $buildVersion = $xml.Project.PropertyGroup.VersionPrefix
    $buildVersion = ([string]$buildVersion).trim()
    write-host "build version $buildVersion"
}

if($buildVersion -and (test-path $templateJson) -and (test-path $templateParametersJson)) {
    write-host "reading $templateJson"
    $pattern = "(?:/|_)v(?<version>\d+?\.\d+?\.\d+?)(?:/|\.)"
    $json = get-content -raw $templateJson | convertfrom-json

    if($json.parameters.appPackageUrl.defaultValue -match $pattern -and $matches.version -ne $buildVersion) {
        write-host "updating $templateJson with version $buildVersion"
        $json.parameters.appPackageUrl.defaultValue = $json.parameters.appPackageUrl.defaultValue.replace($matches.version,$buildVersion)
        $json.parameters.applicationTypeVersion.defaultValue = $buildVersion
        $json | convertto-json -Depth 99 | out-file $templateJson
    }

    write-host "reading $templateParametersJson"
    $json = get-content -raw $templateParametersJson | convertfrom-json

    if($json.parameters.appPackageUrl.value -match $pattern -and $matches.version -ne $buildVersion) {
        write-host "updating $templateParametersJson with version $buildVersion"
        $json.parameters.appPackageUrl.value = $json.parameters.appPackageUrl.value.replace($matches.version,$buildVersion)
        $json.parameters.applicationTypeVersion.value = $buildVersion
        $json | convertto-json -Depth 99 | out-file $templateParametersJson
    }
}

Set-location -Path $presentWorkingDirectory
