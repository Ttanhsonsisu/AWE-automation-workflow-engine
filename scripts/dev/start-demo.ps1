[CmdletBinding()]
param(
    [switch]$StopInfrastructureOnExit
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$frontendRoot = (Resolve-Path (Join-Path $repoRoot '..\FE')).Path
$envFile = Join-Path $repoRoot '.env'

if (-not (Test-Path $envFile)) {
    Copy-Item (Join-Path $repoRoot '.env.example') $envFile
    Write-Host 'Created backend .env from .env.example.'
}

function Import-EnvFile([string]$Path) {
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }

        $parts = $trimmed -split '=', 2
        if ($parts.Count -eq 2) {
            Set-Item -Path "Env:$($parts[0].Trim())" -Value $parts[1].Trim()
        }
    }
}

Import-EnvFile $envFile

$redisDevPassword = if ($env:REDIS_DEV_PASSWORD) { $env:REDIS_DEV_PASSWORD } else { 'change_me' }

# Local .NET processes connect through the isolated dev stack's host ports.
Set-Item Env:DOTNET_ENVIRONMENT 'Development'
Set-Item Env:ASPNETCORE_ENVIRONMENT 'Development'
Set-Item Env:ASPNETCORE_URLS "http://localhost:$env:API_GATEWAY_HTTP_PORT"
Set-Item Env:ConnectionStrings__postgres "Host=localhost;Port=$env:POSTGRES_PORT;Database=awe_db;Username=$env:POSTGRES_USER;Password=$env:POSTGRES_PASSWORD"
Set-Item Env:ConnectionStrings__Redis "localhost:$env:REDIS_PORT,password=$redisDevPassword,abortConnect=false"
Set-Item Env:RabbitMq__Host 'localhost'
Set-Item Env:RabbitMq__Port $env:RABBITMQ_AMQP_PORT
Set-Item Env:RabbitMq__VirtualHost $env:RABBITMQ_VHOST
Set-Item Env:RabbitMq__Username $env:RABBITMQ_USERNAME
Set-Item Env:RabbitMq__Password $env:RABBITMQ_PASSWORD
Set-Item Env:Minio__Endpoint "localhost:$env:MINIO_PORT"
Set-Item Env:Minio__AccessKey $env:MINIO_ROOT_USER
Set-Item Env:Minio__SecretKey $env:MINIO_ROOT_PASSWORD
Set-Item Env:Keycloak__Authority "http://localhost:$env:KEYCLOAK_PORT/realms/awe-auth"
Set-Item Env:Keycloak__ValidIssuer "http://localhost:$env:KEYCLOAK_PORT/realms/awe-auth"

# Vite gives existing process variables precedence over FE/.env.
Set-Item Env:VITE_API_URL "http://localhost:$env:API_GATEWAY_HTTP_PORT/api"
Set-Item Env:VITE_SIGNALR_URL "http://localhost:$env:API_GATEWAY_HTTP_PORT/hubs/workflow"
Set-Item Env:VITE_OIDC_AUTHORITY "http://localhost:$env:KEYCLOAK_PORT/realms/awe-auth"
Set-Item Env:VITE_OIDC_CLIENT_ID 'awe-fe'
Set-Item Env:VITE_OIDC_REDIRECT_URI 'http://localhost:5173/'
Set-Item Env:VITE_OIDC_POST_LOGOUT_REDIRECT_URI 'http://localhost:5173/'
Set-Item Env:VITE_OIDC_SCOPE 'openid profile email roles'

Push-Location $repoRoot
$processes = @()
try {
    Write-Host 'Starting isolated development infrastructure...'
    docker compose up -d
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose failed to start.' }

    Write-Host 'Waiting for development infrastructure ports...'
    foreach ($port in @(
        $env:POSTGRES_PORT,
        $env:RABBITMQ_AMQP_PORT,
        $env:REDIS_PORT,
        $env:MINIO_PORT,
        $env:KEYCLOAK_PORT
    )) {
        $ready = $false
        for ($attempt = 1; $attempt -le 90; $attempt++) {
            if (Test-NetConnection -ComputerName localhost -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue) {
                $ready = $true
                break
            }
            Start-Sleep -Seconds 1
        }
        if (-not $ready) { throw "Timed out waiting for localhost:$port." }
    }

    Write-Host 'Building backend...'
    dotnet build AWE-automation-workflow-engine.slnx
    if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }

    $dotnet = (Get-Command dotnet).Source
    $npm = (Get-Command npm.cmd).Source

    $processes += Start-Process $dotnet -NoNewWindow -PassThru -WorkingDirectory $repoRoot -ArgumentList @(
        'run', '--no-build', '--no-launch-profile', '--project', 'src/Presentation/AWE.ApiGateway/AWE.ApiGateway.csproj'
    )
    $processes += Start-Process $dotnet -NoNewWindow -PassThru -WorkingDirectory $repoRoot -ArgumentList @(
        'run', '--no-build', '--no-launch-profile', '--project', 'src/Workers/AWE.Wokrer.Engine/AWE.Wokrer.Engine.csproj'
    )
    $processes += Start-Process $dotnet -NoNewWindow -PassThru -WorkingDirectory $repoRoot -ArgumentList @(
        'run', '--no-build', '--no-launch-profile', '--project', 'src/Workers/AWE.Worker/AWE.Worker.csproj'
    )
    $processes += Start-Process $npm -NoNewWindow -PassThru -WorkingDirectory $frontendRoot -ArgumentList @('run', 'dev')

    Write-Host ''
    Write-Host 'Development demo is running:'
    Write-Host '  FE       http://localhost:5173'
    Write-Host "  API      http://localhost:$env:API_GATEWAY_HTTP_PORT"
    Write-Host "  Keycloak http://localhost:$env:KEYCLOAK_PORT"
    Write-Host 'Press Ctrl+C to stop the development applications.'

    while ($true) {
        Start-Sleep -Seconds 1
        $failed = $processes | Where-Object { $_.HasExited }
        if ($failed) { throw "A development process exited (PID $($failed[0].Id))." }
    }
}
finally {
    $processes | Where-Object { $_ -and -not $_.HasExited } | Stop-Process -Force -ErrorAction SilentlyContinue
    if ($StopInfrastructureOnExit) {
        docker compose down
    }
    Pop-Location
}
