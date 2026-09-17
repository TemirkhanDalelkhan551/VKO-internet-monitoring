$ErrorActionPreference = 'Stop'

$windowsRegistry = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$version = [Environment]::OSVersion.Version
$isWindows10OrNewer = $version.Major -ge 10
$is64Bit = [Environment]::Is64BitOperatingSystem
[PSCustomObject]@{
    ComputerName = $env:COMPUTERNAME
    WindowsCaption = $windowsRegistry.ProductName
    WindowsVersion = $version.ToString()
    WindowsBuild = $windowsRegistry.CurrentBuildNumber
    Architecture = if ($is64Bit) { '64-bit' } else { '32-bit' }
    Windows10OrNewer = $isWindows10OrNewer
    Is64Bit = $is64Bit
    SelfContainedPackage = $true
    DotNetRuntimeRequired = $false
    ReadyForCurrentAgent = $isWindows10OrNewer -and $is64Bit
}
