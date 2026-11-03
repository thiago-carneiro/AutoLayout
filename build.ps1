# Copyright 2026 The AutoLayout authors
# SPDX-License-Identifier: Apache-2.0
#
# Builds and tests AutoLayout. Needs the .NET 10 SDK (winget install Microsoft.DotNet.SDK.10);
# -Store also needs the Visual Studio C++ build tools, for Native AOT (see README).
#
#   .\build.ps1           build and test; the app is in src\AutoLayout\bin\Release\...\win-x64
#   .\build.ps1 -Store    also build the Microsoft Store packages (x64 and ARM64) in dist\store

param(
    [switch] $Store
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

# Native AOT (Release publish) finds the C++ tools with vswhere, which is not on the PATH
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if ((Test-Path $installer) -and -not ($env:Path -split ';' -contains $installer)) {
    $env:Path += ";$installer"
}

# The XAML compiler fails on paths longer than 260 characters
if ($PSScriptRoot.Length -gt 100) {
    Write-Warning "The folder path is long ($($PSScriptRoot.Length) characters). If the build fails with WMC9999, clone to a shorter path."
}

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed" }
}

Invoke-DotNet test (Join-Path $PSScriptRoot 'tests\AutoLayout.Core.Tests\AutoLayout.Core.Tests.csproj') -c Release
Invoke-DotNet build (Join-Path $PSScriptRoot 'src\AutoLayout\AutoLayout.csproj') -c Release

if ($Store) {
    foreach ($runtime in 'win-x64', 'win-arm64') {
        Invoke-DotNet publish (Join-Path $PSScriptRoot 'src\AutoLayout\AutoLayout.csproj') -c Release -r $runtime '-p:Packaged=true'
    }
    Get-ChildItem (Join-Path $PSScriptRoot 'dist\store') -Recurse -Filter *.msix | ForEach-Object { Write-Output $_.FullName }
}
