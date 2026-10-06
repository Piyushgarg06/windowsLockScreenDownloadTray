##############################################################################
# DownloadTray -- Download Simulator
##############################################################################

$env:PATH = "C:\Users\gargp\.dotnet;" + $env:PATH
dotnet run --project "$PSScriptRoot\src\DownloadTray.ConsoleDemo" -- --simulate
