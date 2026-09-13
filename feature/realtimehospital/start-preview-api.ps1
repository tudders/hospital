param(
    [switch]$TrustDevelopmentCertificate,
    [ValidateRange(1024, 65535)]
    [int]$Port = 5026
)

$ErrorActionPreference = 'Stop'
$hospitalProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$hospitalEnvPath = Join-Path $hospitalProjectRoot 'frontend/.env'
$hospitalEnvLine = Get-Content -LiteralPath $hospitalEnvPath |
    Where-Object { $_ -match '^\s*SQL_SERVE_CONNECTION_STRING=' } |
    Select-Object -First 1
if (-not $hospitalEnvLine) { throw 'SQL_SERVE_CONNECTION_STRING is missing from frontend/.env.' }

$hospitalConnectionValue = ($hospitalEnvLine -split '=', 2)[1].Trim().Trim('"').Trim("'")
$hospitalConnectionValue = $hospitalConnectionValue -replace '^ConnectionString\s*=\s*', ''
if ($TrustDevelopmentCertificate) {
    # Explicit local-development opt-in. Retain TLS encryption; bypass certificate validation
    # only for this API process. Do not use this switch for production connections.
    $hospitalConnectionValue = $hospitalConnectionValue.TrimEnd(';') + ';Encrypt=True;TrustServerCertificate=True;'
}

$hospitalPreviousConnection = [Environment]::GetEnvironmentVariable('ConnectionStrings__Hospital', 'Process')
try {
    [Environment]::SetEnvironmentVariable('ConnectionStrings__Hospital', $hospitalConnectionValue, 'Process')
    & dotnet run --no-build --project (Join-Path $hospitalProjectRoot 'backend/src/Alcidion.Api') `
        --launch-profile http --urls "http://localhost:$Port"
    if ($LASTEXITCODE -ne 0) { throw "Preview API exited with code $LASTEXITCODE." }
}
finally {
    [Environment]::SetEnvironmentVariable('ConnectionStrings__Hospital', $hospitalPreviousConnection, 'Process')
}
