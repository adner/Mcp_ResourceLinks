$jsonString = az account get-access-token --resource https://org41df0750.crm4.dynamics.com/

$tokenObject = $jsonString | ConvertFrom-Json
$accessToken = $tokenObject.accessToken

Write-Host "Access token: $accessToken"

$headers = @{
    "Accept" = "application/json"
    "Content-Type" = "application/json; charset=utf-8"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
    "Authorization" = "Bearer $accessToken"
}

Write-Host "Installing sample data..."

Invoke-RestMethod -Uri "https://org41df0750.crm4.dynamics.com/api/data/v9.2/InstallSampleData" -Method POST -Headers $headers -Body "{}"
