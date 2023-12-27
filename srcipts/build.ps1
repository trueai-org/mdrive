param (
	[Parameter()]
	[ValidateNotNullOrEmpty()]
	[string]
	$OutputPath = '.\bin\Client'
)

Write-Host 'Building'

dotnet publish ..\src\MDriveSync.Client.API\MDriveSync.Client.API.csproj -c Release --self-contained false -p:PublishReadyToRun=true -p:PublishSingleFile=true -o $OutputPath

if ( Test-Path -Path .\bin\Client ) {
    rm -Force "$OutputPath\*.pdb"
    rm -Force "$OutputPath\*.xml"
}

Write-Host 'Build done'

ls $OutputPath
7z a MDrive.zip $OutputPath
exit 0