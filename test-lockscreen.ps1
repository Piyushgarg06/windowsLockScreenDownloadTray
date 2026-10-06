##############################################################################
# Lock Screen Notification POC Launcher
##############################################################################

$env:PATH = "C:\Users\gargp\.dotnet;" + $env:PATH
dotnet run --project "$PSScriptRoot\src\LockScreenPOC" -c Release
