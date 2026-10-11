<#
    Builds the whole solution and runs the tests.

    Outputs build results plus the MSTest summary. Used for the day-to-day build-and-test loop.

    Depends on the .NET 10 SDK: https://dotnet.microsoft.com/download
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

# Build
dotnet build (Join-Path $root 'MagicBuds.sln') -c $Configuration -p:Platform=$Platform -nologo -v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Build failed ($LASTEXITCODE)."
}

# Tests only run on x64 (the build machine's architecture)
if ($Platform -ne 'x64') {
    Write-Output 'Skipping tests for ARM64.'
    return
}

# Tests
dotnet test (Join-Path $root 'src\MagicBuds.Presentation.Tests\MagicBuds.Presentation.Tests.csproj') --no-build -c $Configuration -p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    throw 'Tests failed.'
}

Write-Output 'Build and tests passed.'
