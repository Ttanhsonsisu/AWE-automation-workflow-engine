[CmdletBinding()]
param(
    [ValidateSet("local", "selfhost")]
    [string]$Environment = "local",

    [string]$ApiBaseUrl,
    [string]$KeycloakBaseUrl,
    [string]$FrontendBaseUrl,
    [string]$Realm = "awe-auth",

    [string]$AccessToken,
    [string]$KeycloakAdminUser,
    [string]$KeycloakAdminPassword,

    [string]$SeederUsername = "awe-demo-seeder",
    [string]$SeederPassword = "AweDemo@12345",
    [string]$SeederClientId = "awe-demo-seeder-cli",

    [string]$PluginVersion,
    [switch]$SkipBuild,
    [switch]$Publish,
    [switch]$KeepExistingDemoWorkflows,
    [string]$WorkflowName = "DEMO - Parallel Google Sheet Application Review",
    [string]$WebhookWorkflowName = "DEMO - Webhook Application Intake",
    [string]$CronWorkflowName = "DEMO - Cron Google Sheet Check Every 20 Seconds",
    [string]$WebhookRoutePath = "demo/application-intake",
    [string]$CronExpression = "*/20 * * * * *",
    [string]$CronTimeZoneId = "Asia/Ho_Chi_Minh",
    [bool]$PublishTriggerDemos = $true,

    [string]$SampleSheetUrl = "https://docs.google.com/spreadsheets/d/YOUR_SHEET_ID/edit#gid=0",
    [string]$SampleGid = "0",
    [string]$AppsScriptWebhookUrl = "",
    [bool]$DryRun = $false,

    # ── Approval node config ─────────────────────────────────────────────────
    # Email giảng viên / người cần phê duyệt
    [string]$ApproverEmail = "lecturer@example.edu.vn",

    # Kênh gửi thông báo: "Email", "Telegram", hoặc cả hai
    [string[]]$ApprovalChannels = @("Email"),

    # URL công khai của API Gateway — build link trong email approval.
    # Local:     http://localhost:8080
    # Self-host: https://your-domain.com
    [string]$ApprovalApiBaseUrl = "",

    # ── SMTP config (node-level — ưu tiên hơn appsettings của server) ────────
    # Tất cả các field SMTP dưới đây sẽ được bake vào workflow node config.
    # Server sẽ dùng các giá trị này để gửi email (không cần config env var SMTP_*).
    # Nếu để rỗng (không truyền) → server fallback về appsettings SmtpEmail của nó.
    #
    # Gmail setup:
    #   1. Bật 2-Factor Authentication tại https://myaccount.google.com/security
    #   2. Tạo App Password tại https://myaccount.google.com/apppasswords
    #   3. Dùng App Password (16 ký tự) làm $SmtpPassword
    #
    # Mailtrap (sandbox, không cần domain thật):
    #   $SmtpHost = "sandbox.smtp.mailtrap.io"
    #   $SmtpPort = 587
    #   $SmtpUsername / $SmtpPassword lấy từ https://mailtrap.io
    # ─────────────────────────────────────────────────────────────────────────
    [string]$SmtpHost        = "",
    [int]$SmtpPort           = 587,
    [string]$SmtpUsername    = "",
    [string]$SmtpPassword    = "",
    [string]$SmtpFromName    = "AWE Workflow System",
    [string]$SmtpFromAddress = "",
    [bool]$SmtpUseSsl        = $true
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Net.Http

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$EngineRoot = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path
$RepoRoot = (Resolve-Path (Join-Path $EngineRoot "..")).Path

function Read-DotEnv {
    param([string]$Path)

    $map = @{}
    if (-not (Test-Path $Path)) {
        return $map
    }

    foreach ($rawLine in Get-Content $Path) {
        $line = $rawLine.Trim()
        if (-not $line -or $line.StartsWith("#")) {
            continue
        }

        $idx = $line.IndexOf("=")
        if ($idx -le 0) {
            continue
        }

        $key = $line.Substring(0, $idx).Trim()
        $value = $line.Substring($idx + 1).Trim()
        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        $map[$key] = $value
    }

    return $map
}

function Get-EnvValue {
    param(
        [hashtable]$Map,
        [string]$Key,
        [string]$Fallback
    )

    if ($Map.ContainsKey($Key) -and -not [string]::IsNullOrWhiteSpace([string]$Map[$Key])) {
        return [string]$Map[$Key]
    }

    return $Fallback
}

$envFile = if ($Environment -eq "selfhost") {
    Join-Path $RepoRoot "AWE-self-host\.env"
} else {
    Join-Path $EngineRoot ".env"
}

if (-not (Test-Path $envFile)) {
    $envFile = if ($Environment -eq "selfhost") {
        Join-Path $RepoRoot "AWE-self-host\.env.example"
    } else {
        Join-Path $EngineRoot ".env.example"
    }
}

$envMap = Read-DotEnv $envFile

if (-not $ApiBaseUrl) {
    $apiPort = Get-EnvValue $envMap "API_GATEWAY_HTTP_PORT" "8080"
    $ApiBaseUrl = "http://localhost:$apiPort/api"
}

if (-not $KeycloakBaseUrl) {
    $keycloakPort = Get-EnvValue $envMap "KEYCLOAK_PORT" "8081"
    $KeycloakBaseUrl = "http://localhost:$keycloakPort"
}

if (-not $FrontendBaseUrl) {
    if ($Environment -eq "selfhost") {
        $frontendPort = Get-EnvValue $envMap "FRONTEND_PORT" "80"
        $FrontendBaseUrl = "http://localhost:$frontendPort"
    } else {
        $FrontendBaseUrl = "http://localhost:5173"
    }
}

if (-not $KeycloakAdminUser) {
    $KeycloakAdminUser = Get-EnvValue $envMap "KEYCLOAK_ADMIN_USER" "admin"
}

if (-not $KeycloakAdminPassword) {
    $defaultAdminPassword = if ($Environment -eq "local") { "admin" } else { "change_me" }
    $KeycloakAdminPassword = Get-EnvValue $envMap "KEYCLOAK_ADMIN_PASSWORD" $defaultAdminPassword
}

if (-not $PluginVersion) {
    $versionStamp = [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')
    $versionNonce = [Guid]::NewGuid().ToString("N").Substring(0, 8)
    $PluginVersion = "1.0.$versionStamp-$versionNonce"
}

$ApiBaseUrl = $ApiBaseUrl.TrimEnd("/")
$KeycloakBaseUrl = $KeycloakBaseUrl.TrimEnd("/")
$FrontendBaseUrl = $FrontendBaseUrl.TrimEnd("/")

function Get-JsonProperty {
    param(
        [object]$Object,
        [string[]]$Names
    )

    if ($null -eq $Object) {
        return $null
    }

    foreach ($name in $Names) {
        if ($Object.PSObject.Properties.Name -contains $name) {
            return $Object.$name
        }
    }

    return $null
}

function Get-JsonArrayItems {
    param([object]$Response)

    $data = Get-JsonProperty $Response @("data", "Data")
    if ($null -eq $data) {
        $data = $Response
    }

    $items = Get-JsonProperty $data @("items", "Items")
    if ($items) {
        return @($items)
    }

    if ($data -is [array]) {
        return @($data)
    }

    return @()
}

function Invoke-ApiJson {
    param(
        [string]$Method,
        [string]$Url,
        [string]$Token,
        [object]$Body = $null
    )

    $headers = @{ Authorization = "Bearer $Token" }

    try {
        if ($null -ne $Body) {
            $json = $Body | ConvertTo-Json -Depth 100
            return Invoke-RestMethod -Method $Method -Uri $Url -Headers $headers -ContentType "application/json; charset=utf-8" -Body $json
        }

        return Invoke-RestMethod -Method $Method -Uri $Url -Headers $headers
    } catch {
        $message = $_.Exception.Message
        if ($_.Exception.Response -and $_.Exception.Response.GetResponseStream()) {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $bodyText = $reader.ReadToEnd()
            if ($bodyText) {
                $message = "$message`n$bodyText"
            }
        }

        throw $message
    }
}

function Invoke-KeycloakToken {
    param(
        [string]$TokenRealm,
        [string]$Username,
        [string]$Password,
        [string]$ClientId = "admin-cli"
    )

    $tokenUrl = "$KeycloakBaseUrl/realms/$TokenRealm/protocol/openid-connect/token"
    $body = @{
        grant_type = "password"
        client_id = $ClientId
        username = $Username
        password = $Password
    }

    $response = Invoke-RestMethod -Method Post -Uri $tokenUrl -ContentType "application/x-www-form-urlencoded" -Body $body
    return $response.access_token
}

function Invoke-KeycloakAdmin {
    param(
        [string]$Method,
        [string]$Path,
        [string]$AdminToken,
        [object]$Body = $null
    )

    $headers = @{ Authorization = "Bearer $AdminToken" }
    $url = "$KeycloakBaseUrl$Path"

    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 30
        return Invoke-RestMethod -Method $Method -Uri $url -Headers $headers -ContentType "application/json; charset=utf-8" -Body $json
    }

    return Invoke-RestMethod -Method $Method -Uri $url -Headers $headers
}

function Assert-HttpEndpointReachable {
    param(
        [string]$Name,
        [string]$Url,
        [string]$Hint
    )

    try {
        Invoke-WebRequest -Method Get -Uri $Url -TimeoutSec 5 -UseBasicParsing | Out-Null
    } catch {
        if ($_.Exception.Response) {
            return
        }

        throw "$Name is not reachable at $Url. $Hint"
    }
}

function Ensure-SeederClient {
    param([string]$AdminToken)

    Write-Host "Ensuring Keycloak seed client '$SeederClientId' in realm '$Realm'..."

    $encodedClientId = [System.Uri]::EscapeDataString($SeederClientId)
    $clients = Invoke-KeycloakAdmin -Method Get -Path "/admin/realms/$Realm/clients?clientId=$encodedClientId" -AdminToken $AdminToken

    if (-not $clients -or $clients.Count -eq 0) {
        Invoke-KeycloakAdmin -Method Post -Path "/admin/realms/$Realm/clients" -AdminToken $AdminToken -Body @{
            clientId = $SeederClientId
            name = "AWE Demo Seeder CLI"
            protocol = "openid-connect"
            enabled = $true
            publicClient = $true
            standardFlowEnabled = $false
            directAccessGrantsEnabled = $true
            serviceAccountsEnabled = $false
            fullScopeAllowed = $true
            redirectUris = @("*")
            webOrigins = @("*")
        } | Out-Null

        $clients = Invoke-KeycloakAdmin -Method Get -Path "/admin/realms/$Realm/clients?clientId=$encodedClientId" -AdminToken $AdminToken
    }

    $clientId = $clients[0].id

    $clientProfile = $clients[0]
    $clientProfile.enabled = $true
    $clientProfile.publicClient = $true
    $clientProfile.standardFlowEnabled = $false
    $clientProfile.directAccessGrantsEnabled = $true
    $clientProfile.serviceAccountsEnabled = $false
    $clientProfile.fullScopeAllowed = $true
    Invoke-KeycloakAdmin -Method Put -Path "/admin/realms/$Realm/clients/$clientId" -AdminToken $AdminToken -Body $clientProfile | Out-Null

    $mappers = Invoke-KeycloakAdmin -Method Get -Path "/admin/realms/$Realm/clients/$clientId/protocol-mappers/models" -AdminToken $AdminToken
    $hasRealmRoleMapper = $false
    if ($mappers) {
        $hasRealmRoleMapper = @($mappers | Where-Object {
            $_.protocolMapper -eq "oidc-usermodel-realm-role-mapper" -and
            $_.config.'claim.name' -eq "realm_access.roles"
        }).Count -gt 0
    }

    if (-not $hasRealmRoleMapper) {
        Invoke-KeycloakAdmin -Method Post -Path "/admin/realms/$Realm/clients/$clientId/protocol-mappers/models" -AdminToken $AdminToken -Body @{
            name = "awe realm roles"
            protocol = "openid-connect"
            protocolMapper = "oidc-usermodel-realm-role-mapper"
            consentRequired = $false
            config = @{
                "user.attribute" = "foo"
                "claim.name" = "realm_access.roles"
                "jsonType.label" = "String"
                "multivalued" = "true"
                "access.token.claim" = "true"
                "id.token.claim" = "true"
                "userinfo.token.claim" = "true"
                "introspection.token.claim" = "true"
            }
        } | Out-Null
    }

    return $clientId
}

function Ensure-SeederUser {
    Write-Host "Ensuring Keycloak seeder user '$SeederUsername' in realm '$Realm'..."

    $adminToken = Invoke-KeycloakToken -TokenRealm "master" -Username $KeycloakAdminUser -Password $KeycloakAdminPassword
    Ensure-SeederClient -AdminToken $adminToken | Out-Null

    $encodedUser = [System.Uri]::EscapeDataString($SeederUsername)
    $seederEmail = "$SeederUsername@example.local"
    $users = Invoke-KeycloakAdmin -Method Get -Path "/admin/realms/$Realm/users?username=$encodedUser&exact=true" -AdminToken $adminToken

    if (-not $users -or $users.Count -eq 0) {
        Invoke-KeycloakAdmin -Method Post -Path "/admin/realms/$Realm/users" -AdminToken $adminToken -Body @{
            username = $SeederUsername
            enabled = $true
            email = $seederEmail
            emailVerified = $true
            firstName = "AWE"
            lastName = "Demo Seeder"
            requiredActions = @()
        } | Out-Null

        $users = Invoke-KeycloakAdmin -Method Get -Path "/admin/realms/$Realm/users?username=$encodedUser&exact=true" -AdminToken $adminToken
    }

    $userId = $users[0].id

    $userProfile = @{
        id = $userId
        username = $SeederUsername
        enabled = $true
        email = $seederEmail
        emailVerified = $true
        firstName = "AWE"
        lastName = "Demo Seeder"
        requiredActions = @()
    }

    Invoke-KeycloakAdmin -Method Put -Path "/admin/realms/$Realm/users/$userId" -AdminToken $adminToken -Body $userProfile | Out-Null

    Invoke-KeycloakAdmin -Method Put -Path "/admin/realms/$Realm/users/$userId/reset-password" -AdminToken $adminToken -Body @{
        type = "password"
        temporary = $false
        value = $SeederPassword
    } | Out-Null

    Invoke-KeycloakAdmin -Method Put -Path "/admin/realms/$Realm/users/$userId" -AdminToken $adminToken -Body $userProfile | Out-Null

    $roleObjects = @()
    foreach ($roleName in @("admin", "editor", "operator")) {
        $roleObjects += Invoke-KeycloakAdmin -Method Get -Path "/admin/realms/$Realm/roles/$roleName" -AdminToken $adminToken
    }

    Invoke-KeycloakAdmin -Method Post -Path "/admin/realms/$Realm/users/$userId/role-mappings/realm" -AdminToken $adminToken -Body $roleObjects | Out-Null

    return Invoke-KeycloakToken -TokenRealm $Realm -Username $SeederUsername -Password $SeederPassword -ClientId $SeederClientId
}

function Get-DemoWorkflowNames {
    return @(
        $WorkflowName,
        $WebhookWorkflowName,
        $CronWorkflowName
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
}

function Remove-ExistingDemoWorkflows {
    param([string]$Token)

    if ($KeepExistingDemoWorkflows) {
        Write-Host "Keeping existing demo workflows because -KeepExistingDemoWorkflows was specified."
        return
    }

    $demoWorkflowNames = @(Get-DemoWorkflowNames)
    Write-Host "Removing existing demo workflows: $($demoWorkflowNames -join ', ')..."

    $deleted = 0
    foreach ($demoWorkflowName in $demoWorkflowNames) {
        $encodedName = [System.Uri]::EscapeDataString($demoWorkflowName)
        $response = Invoke-ApiJson -Method Get -Url "$ApiBaseUrl/workflows/definitions?pageSize=200&pageNo=1&groupVersion=false&name=$encodedName" -Token $Token
        $items = Get-JsonArrayItems -Response $response

        foreach ($item in $items) {
            $name = Get-JsonProperty $item @("name", "Name")
            $id = Get-JsonProperty $item @("id", "Id")

            if ($id -and $name -eq $demoWorkflowName) {
                try {
                    Invoke-ApiJson -Method Delete -Url "$ApiBaseUrl/workflows/definitions/$id" -Token $Token | Out-Null
                    $deleted++
                } catch {
                    Write-Warning "Could not delete old demo workflow $id. $($_.Exception.Message)"
                }
            }
        }
    }

    Write-Host "Removed $deleted old demo workflow definition(s)."
}

function Invoke-UploadPluginVersion {
    param(
        [string]$Url,
        [string]$Token,
        [string]$Version,
        [string]$DllPath
    )

    $client = New-Object System.Net.Http.HttpClient
    $content = New-Object System.Net.Http.MultipartFormDataContent
    $stream = $null

    try {
        $client.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", $Token)
        $content.Add((New-Object System.Net.Http.StringContent($Version)), "Version")
        $content.Add((New-Object System.Net.Http.StringContent("awe-plugins")), "Bucket")
        $content.Add((New-Object System.Net.Http.StringContent("Google Sheet review demo workflow plugin")), "ReleaseNotes")

        $stream = [System.IO.File]::OpenRead($DllPath)
        $fileContent = New-Object System.Net.Http.StreamContent($stream)
        $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/octet-stream")
        $content.Add($fileContent, "File", [System.IO.Path]::GetFileName($DllPath))

        $response = $client.PostAsync($Url, $content).Result
        $body = $response.Content.ReadAsStringAsync().Result
        if (-not $response.IsSuccessStatusCode) {
            throw "Upload failed ($($response.StatusCode)): $body"
        }

        if ([string]::IsNullOrWhiteSpace($body)) {
            return $null
        }

        return $body | ConvertFrom-Json
    } finally {
        if ($stream) { $stream.Dispose() }
        if ($content) { $content.Dispose() }
        if ($client) { $client.Dispose() }
    }
}

function Get-PackageByUniqueName {
    param(
        [string]$UniqueName,
        [string]$Token
    )

    $encoded = [System.Uri]::EscapeDataString($UniqueName)
    $response = Invoke-ApiJson -Method Get -Url "$ApiBaseUrl/plugins/packages?size=200&search=$encoded" -Token $Token
    $data = Get-JsonProperty $response @("data", "Data")
    $items = Get-JsonProperty $data @("items", "Items")
    if (-not $items) {
        return $null
    }

    return $items | Where-Object {
        (Get-JsonProperty $_ @("uniqueName", "UniqueName")) -eq $UniqueName
    } | Select-Object -First 1
}

function Ensure-PluginPackage {
    param(
        [hashtable]$Spec,
        [string]$Token
    )

    $package = Get-PackageByUniqueName -UniqueName $Spec.UniqueName -Token $Token
    if ($package) {
        return $package
    }

    Write-Host "Creating plugin package $($Spec.UniqueName)..."
    $response = Invoke-ApiJson -Method Post -Url "$ApiBaseUrl/plugins/packages" -Token $Token -Body @{
        UniqueName = $Spec.UniqueName
        DisplayName = $Spec.DisplayName
        ExecutionMode = 1
        Category = "Google Sheet Review"
        Icon = $Spec.Icon
        Description = $Spec.Description
    }

    return Get-JsonProperty $response @("data", "Data")
}

function Build-Plugin {
    param([hashtable]$Spec)

    if (-not $SkipBuild) {
        Write-Host "Building $($Spec.Project)..."
        $buildArgs = @(
            "build",
            $Spec.Project,
            "-c",
            "Release",
            "--no-incremental",
            "/p:Deterministic=false",
            "/p:InformationalVersion=$PluginVersion"
        )

        & dotnet @buildArgs | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed for $($Spec.Project)"
        }
    }

    $projectDir = Split-Path -Parent $Spec.Project
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($Spec.Project)
    $dllPath = Join-Path $projectDir "bin\Release\net10.0\$projectName.dll"
    if (-not (Test-Path $dllPath)) {
        throw "Plugin DLL not found: $dllPath"
    }

    return (Resolve-Path $dllPath).Path
}

function Register-Plugin {
    param(
        [hashtable]$Spec,
        [string]$Token
    )

    $dllPath = [string](Build-Plugin -Spec $Spec | Select-Object -Last 1)
    $package = Ensure-PluginPackage -Spec $Spec -Token $Token
    $packageId = Get-JsonProperty $package @("id", "Id")
    if (-not $packageId) {
        throw "Could not resolve package id for $($Spec.UniqueName)."
    }

    Write-Host "Uploading $($Spec.UniqueName) version $PluginVersion..."
    $upload = Invoke-UploadPluginVersion -Url "$ApiBaseUrl/plugins/packages/$packageId/versions" -Token $Token -Version $PluginVersion -DllPath $dllPath
    $versionData = Get-JsonProperty $upload @("data", "Data")
    $versionId = Get-JsonProperty $versionData @("id", "Id")
    $executionMetadata = Get-JsonProperty $versionData @("executionMetadata", "ExecutionMetadata")
    $configSchema = Get-JsonProperty $versionData @("configSchema", "ConfigSchema")
    $version = Get-JsonProperty $versionData @("version", "Version")

    if ($versionId) {
        Write-Host "Activating $($Spec.UniqueName) version $version..."
        Invoke-ApiJson -Method Post -Url "$ApiBaseUrl/plugins/versions/$versionId/activate" -Token $Token | Out-Null
    }

    return @{
        UniqueName = $Spec.UniqueName
        DisplayName = $Spec.DisplayName
        Category = "Google Sheet Review"
        Icon = $Spec.Icon
        PackageId = $packageId
        Version = $version
        ExecutionMetadata = $executionMetadata
        InputSchema = $configSchema
        VersionId = $versionId
    }
}

function New-WorkflowNode {
    param(
        [string]$Id,
        [string]$PluginName,
        [string]$DisplayName,
        [string]$Category,
        [string]$ExecutionMode,
        [object]$ExecutionMetadata,
        [object]$PackageId,
        [object]$Version,
        [object]$InputSchema,
        [hashtable]$Inputs,
        [int]$X,
        [int]$Y,
        [bool]$IsStart = $false
    )

    $pluginMetadata = @{
        name = $PluginName
        displayName = $DisplayName
        category = $Category
        description = ""
        icon = $null
        executionMode = $ExecutionMode
        version = $Version
        executionMetadata = $ExecutionMetadata
        packageId = $PackageId
        inputSchema = $InputSchema
    }

    return @{
        id = $Id
        type = if ($IsStart) { "startNode" } else { "actionNode" }
        position = @{ x = $X; y = $Y }
        data = @{
            pluginMetadata = $pluginMetadata
            config = @{
                inputs = $Inputs
                stepId = $Id
                isConfigured = $true
            }
            uiState = @{ isValid = $true }
            status = "idle"
        }
    }
}

function New-WorkflowEdge {
    param(
        [string]$Id,
        [string]$Source,
        [string]$Target,
        [string]$Condition = $null
    )

    $edge = @{
        id = $Id
        source = $Source
        target = $Target
        type = "customEdge"
        animated = $false
    }

    if ($Condition) {
        $edge.data = @{ condition = $Condition }
    }

    return $edge
}

function New-GoogleSheetReviewWorkflowPayload {
    param(
        [hashtable]$ReadPlugin,
        [hashtable]$QualityPlugin,
        [hashtable]$AnalyzePlugin,
        [hashtable]$WriteBackPlugin
    )

    $manualInputs = @{}
    $readInputs = @{
        SheetUrl = "{{workflow.input.sheetUrl}}"
        Gid = "{{workflow.input.gid}}"
        MaxRows = "{{workflow.input.maxRows}}"
        HasHeader = "{{workflow.input.hasHeader}}"
    }
    $qualityInputs = @{
        RowsJson = "{{steps.read_google_sheet.Output.RowsJson}}"
        RequiredColumnsCsv = "applicantName,major,gpa,englishScore,experienceMonths,documentText"
    }
    $analyzeInputs = @{
        RowsJson = "{{steps.read_google_sheet.Output.RowsJson}}"
        MinimumGpa = "{{workflow.input.minimumGpa}}"
        MinimumEnglishScore = "{{workflow.input.minimumEnglishScore}}"
        TargetExperienceMonths = "{{workflow.input.targetExperienceMonths}}"
    }
    $delayInputs = @{ Seconds = 2 }
    $writeBackInputs = @{
        SheetUrl = "{{workflow.input.sheetUrl}}"
        ResultsJson = "{{steps.analyze_sheet.Output.ResultsJson}}"
        QualityIsValid = "{{steps.sheet_quality_check.Output.IsValid}}"
        QualitySummary = "{{steps.sheet_quality_check.Output.Summary}}"
        AppsScriptWebhookUrl = "{{workflow.input.appsScriptWebhookUrl}}"
        DryRun = "{{workflow.input.dryRun}}"
        WaitForWebhookResponse = $false
        WebhookTimeoutSeconds = 8
    }
    $ifInputs = @{
        Value1 = "{{steps.write_back_results.Output.DecisionMode}}"
        Operator = "=="
        Value2 = "APPROVAL_REQUIRED"
    }
    $approvalInputs = @{
        # ── Notification ──────────────────────────────────────────────────────
        Channels      = $ApprovalChannels
        ApproverEmail = $ApproverEmail
        Title         = "Google Sheet applications need review"
        Message       = "Sheet {{workflow.input.sheetUrl}} needs manual review. {{steps.write_back_results.Output.Summary}}"

        # ── Approval URL ──────────────────────────────────────────────────────
        # Build từ tham số -ApprovalApiBaseUrl, fallback về $ApiBaseUrl (bỏ /api suffix)
        ApiBaseUrl    = if ($ApprovalApiBaseUrl) { $ApprovalApiBaseUrl } else { $ApiBaseUrl.TrimEnd("/api").TrimEnd("/") }

        # ── SMTP config (node-level — ưu tiên hơn appsettings server) ─────────
        # Nếu field rỗng → server tự fallback về appsettings SmtpEmail của nó.
        SmtpHost        = $SmtpHost
        SmtpPort        = $SmtpPort
        SmtpUsername    = $SmtpUsername
        SmtpPassword    = $SmtpPassword
        SmtpFromName    = $SmtpFromName
        SmtpFromAddress = $SmtpFromAddress
        SmtpUseSsl      = $SmtpUseSsl
    }


    $steps = @(
        @{
            Id = "manual_start"
            Type = "ManualTrigger"
            ExecutionMode = "BuiltIn"
            IsConfigured = $true
            Inputs = $manualInputs
        },
        @{
            Id = "read_google_sheet"
            Type = $ReadPlugin.UniqueName
            ExecutionMode = "DynamicDll"
            IsConfigured = $true
            ExecutionMetadata = $ReadPlugin.ExecutionMetadata
            Inputs = $readInputs
        },
        @{
            Id = "sheet_quality_check"
            Type = $QualityPlugin.UniqueName
            ExecutionMode = "DynamicDll"
            IsConfigured = $true
            ExecutionMetadata = $QualityPlugin.ExecutionMetadata
            Inputs = $qualityInputs
        },
        @{
            Id = "analyze_sheet"
            Type = $AnalyzePlugin.UniqueName
            ExecutionMode = "DynamicDll"
            IsConfigured = $true
            ExecutionMetadata = $AnalyzePlugin.ExecutionMetadata
            Inputs = $analyzeInputs
        },
        @{
            Id = "external_review_wait"
            Type = "Delay"
            ExecutionMode = "BuiltIn"
            IsConfigured = $true
            Inputs = $delayInputs
        },
        @{
            Id = "sheet_review_join"
            Type = "Join"
            ExecutionMode = "BuiltIn"
            IsConfigured = $true
            Inputs = @{}
        },
        @{
            Id = "write_back_results"
            Type = $WriteBackPlugin.UniqueName
            ExecutionMode = "DynamicDll"
            IsConfigured = $true
            ExecutionMetadata = $WriteBackPlugin.ExecutionMetadata
            Inputs = $writeBackInputs
        },
        @{
            Id = "if_needs_approval"
            Type = "If"
            ExecutionMode = "BuiltIn"
            IsConfigured = $true
            Inputs = $ifInputs
        },
        @{
            Id = "approval_sheet_review"
            Type = "Approval"
            ExecutionMode = "BuiltIn"
            IsConfigured = $true
            Inputs = $approvalInputs
        },
        @{
            Id = "log_auto_complete"
            Type = "Log"
            ExecutionMode = "BuiltIn"
            IsConfigured = $true
            Inputs = @{
                Msg = "AUTO COMPLETE - {{steps.write_back_results.Output.Summary}}"
            }
        },
        @{
            Id = "log_approved"
            Type = "Log"
            ExecutionMode = "BuiltIn"
            IsConfigured = $true
            Inputs = @{
                Msg = "APPROVED by human reviewer. Reason: {{steps.approval_sheet_review.Output.Reason}}"
            }
        },
        @{
            Id = "log_rejected"
            Type = "Log"
            ExecutionMode = "BuiltIn"
            IsConfigured = $true
            Inputs = @{
                Msg = "REJECTED by human reviewer. Reason: {{steps.approval_sheet_review.Output.Reason}}"
            }
        }
    )

    $transitions = @(
        @{ Id = "t_manual_read"; Source = "manual_start"; Target = "read_google_sheet" },
        @{ Id = "t_read_quality"; Source = "read_google_sheet"; Target = "sheet_quality_check" },
        @{ Id = "t_read_analyze"; Source = "read_google_sheet"; Target = "analyze_sheet" },
        @{ Id = "t_read_wait"; Source = "read_google_sheet"; Target = "external_review_wait" },
        @{ Id = "t_quality_join"; Source = "sheet_quality_check"; Target = "sheet_review_join" },
        @{ Id = "t_analyze_join"; Source = "analyze_sheet"; Target = "sheet_review_join" },
        @{ Id = "t_wait_join"; Source = "external_review_wait"; Target = "sheet_review_join" },
        @{ Id = "t_join_write"; Source = "sheet_review_join"; Target = "write_back_results" },
        @{ Id = "t_write_if"; Source = "write_back_results"; Target = "if_needs_approval" },
        @{ Id = "t_if_approval"; Source = "if_needs_approval"; Target = "approval_sheet_review"; Condition = "{{steps.if_needs_approval.Output.IsMatch}} === true" },
        @{ Id = "t_if_auto"; Source = "if_needs_approval"; Target = "log_auto_complete"; Condition = "{{steps.if_needs_approval.Output.IsMatch}} === false" },
        @{ Id = "t_approval_approved"; Source = "approval_sheet_review"; Target = "log_approved"; Condition = "{{steps.approval_sheet_review.Output.IsApproved}} === true" },
        @{ Id = "t_approval_rejected"; Source = "approval_sheet_review"; Target = "log_rejected"; Condition = "{{steps.approval_sheet_review.Output.IsApproved}} === false" }
    )

    $nodes = @(
        New-WorkflowNode -Id "manual_start" -PluginName "ManualTrigger" -DisplayName "Manual Trigger" -Category "Trigger" -ExecutionMode "BuiltIn" -ExecutionMetadata $null -PackageId $null -Version "Built-in" -InputSchema $null -Inputs $manualInputs -X 0 -Y 180 -IsStart $true
        New-WorkflowNode -Id "read_google_sheet" -PluginName $ReadPlugin.UniqueName -DisplayName $ReadPlugin.DisplayName -Category "Google Sheet Review" -ExecutionMode "DynamicDll" -ExecutionMetadata $ReadPlugin.ExecutionMetadata -PackageId $ReadPlugin.PackageId -Version $ReadPlugin.Version -InputSchema $ReadPlugin.InputSchema -Inputs $readInputs -X 310 -Y 180
        New-WorkflowNode -Id "sheet_quality_check" -PluginName $QualityPlugin.UniqueName -DisplayName $QualityPlugin.DisplayName -Category "Google Sheet Review" -ExecutionMode "DynamicDll" -ExecutionMetadata $QualityPlugin.ExecutionMetadata -PackageId $QualityPlugin.PackageId -Version $QualityPlugin.Version -InputSchema $QualityPlugin.InputSchema -Inputs $qualityInputs -X 640 -Y 20
        New-WorkflowNode -Id "analyze_sheet" -PluginName $AnalyzePlugin.UniqueName -DisplayName $AnalyzePlugin.DisplayName -Category "Google Sheet Review" -ExecutionMode "DynamicDll" -ExecutionMetadata $AnalyzePlugin.ExecutionMetadata -PackageId $AnalyzePlugin.PackageId -Version $AnalyzePlugin.Version -InputSchema $AnalyzePlugin.InputSchema -Inputs $analyzeInputs -X 640 -Y 180
        New-WorkflowNode -Id "external_review_wait" -PluginName "Delay" -DisplayName "External Review Wait" -Category "Core" -ExecutionMode "BuiltIn" -ExecutionMetadata $null -PackageId $null -Version "Built-in" -InputSchema $null -Inputs $delayInputs -X 640 -Y 340
        New-WorkflowNode -Id "sheet_review_join" -PluginName "Join" -DisplayName "Join Parallel Checks" -Category "Logic" -ExecutionMode "BuiltIn" -ExecutionMetadata $null -PackageId $null -Version "Built-in" -InputSchema $null -Inputs @{} -X 980 -Y 180
        New-WorkflowNode -Id "write_back_results" -PluginName $WriteBackPlugin.UniqueName -DisplayName $WriteBackPlugin.DisplayName -Category "Google Sheet Review" -ExecutionMode "DynamicDll" -ExecutionMetadata $WriteBackPlugin.ExecutionMetadata -PackageId $WriteBackPlugin.PackageId -Version $WriteBackPlugin.Version -InputSchema $WriteBackPlugin.InputSchema -Inputs $writeBackInputs -X 1310 -Y 180
        New-WorkflowNode -Id "if_needs_approval" -PluginName "If" -DisplayName "Needs Approval?" -Category "Logic" -ExecutionMode "BuiltIn" -ExecutionMetadata $null -PackageId $null -Version "Built-in" -InputSchema $null -Inputs $ifInputs -X 1640 -Y 180
        New-WorkflowNode -Id "approval_sheet_review" -PluginName "Approval" -DisplayName "Lecturer Approval" -Category "Human Interaction" -ExecutionMode "BuiltIn" -ExecutionMetadata $null -PackageId $null -Version "Built-in" -InputSchema $null -Inputs $approvalInputs -X 1970 -Y 60
        New-WorkflowNode -Id "log_auto_complete" -PluginName "Log" -DisplayName "Log Auto Complete" -Category "Core" -ExecutionMode "BuiltIn" -ExecutionMetadata $null -PackageId $null -Version "Built-in" -InputSchema $null -Inputs $steps[9].Inputs -X 1970 -Y 300
        New-WorkflowNode -Id "log_approved" -PluginName "Log" -DisplayName "Log Approved" -Category "Core" -ExecutionMode "BuiltIn" -ExecutionMetadata $null -PackageId $null -Version "Built-in" -InputSchema $null -Inputs $steps[10].Inputs -X 2300 -Y 20
        New-WorkflowNode -Id "log_rejected" -PluginName "Log" -DisplayName "Log Rejected" -Category "Core" -ExecutionMode "BuiltIn" -ExecutionMetadata $null -PackageId $null -Version "Built-in" -InputSchema $null -Inputs $steps[11].Inputs -X 2300 -Y 140
    )

    $edges = @(
        New-WorkflowEdge -Id "edge-manual-read" -Source "manual_start" -Target "read_google_sheet"
        New-WorkflowEdge -Id "edge-read-quality" -Source "read_google_sheet" -Target "sheet_quality_check"
        New-WorkflowEdge -Id "edge-read-analyze" -Source "read_google_sheet" -Target "analyze_sheet"
        New-WorkflowEdge -Id "edge-read-wait" -Source "read_google_sheet" -Target "external_review_wait"
        New-WorkflowEdge -Id "edge-quality-join" -Source "sheet_quality_check" -Target "sheet_review_join"
        New-WorkflowEdge -Id "edge-analyze-join" -Source "analyze_sheet" -Target "sheet_review_join"
        New-WorkflowEdge -Id "edge-wait-join" -Source "external_review_wait" -Target "sheet_review_join"
        New-WorkflowEdge -Id "edge-join-write" -Source "sheet_review_join" -Target "write_back_results"
        New-WorkflowEdge -Id "edge-write-if" -Source "write_back_results" -Target "if_needs_approval"
        New-WorkflowEdge -Id "edge-if-approval" -Source "if_needs_approval" -Target "approval_sheet_review" -Condition "{{steps.if_needs_approval.Output.IsMatch}} === true"
        New-WorkflowEdge -Id "edge-if-auto" -Source "if_needs_approval" -Target "log_auto_complete" -Condition "{{steps.if_needs_approval.Output.IsMatch}} === false"
        New-WorkflowEdge -Id "edge-approval-approved" -Source "approval_sheet_review" -Target "log_approved" -Condition "{{steps.approval_sheet_review.Output.IsApproved}} === true"
        New-WorkflowEdge -Id "edge-approval-rejected" -Source "approval_sheet_review" -Target "log_rejected" -Condition "{{steps.approval_sheet_review.Output.IsApproved}} === false"
    )

    return @{
        Name = $WorkflowName
        Description = "Manual-trigger workflow demo that reads a cloud Google Sheet, runs parallel custom checks, joins them, optionally writes back, and routes to human approval."
        DefinitionJson = @{
            Steps = $steps
            Transitions = $transitions
        }
        UiJson = @{
            nodes = $nodes
            edges = $edges
        }
    }
}

function Get-NormalizedWebhookRoutePath {
    param([string]$RoutePath)

    $route = if ($RoutePath) { $RoutePath.Trim() } else { "" }
    $route = $route.Trim("/")

    if ([string]::IsNullOrWhiteSpace($route)) {
        return "demo/application-intake"
    }

    return $route
}

function New-WebhookApplicationIntakeWorkflowPayload {
    $routePath = Get-NormalizedWebhookRoutePath -RoutePath $WebhookRoutePath

    $webhookInputs = @{
        RoutePath = $routePath
        SecretToken = ""
        IdempotencyKeyPath = "applicationId"
    }
    $logReceivedInputs = @{
        Msg = "WEBHOOK RECEIVED - {{workflow.input.applicationId}} | {{workflow.input.applicantName}} | {{workflow.input.major}} | GPA {{workflow.input.gpa}}"
    }
    $ifInputs = @{
        Value1 = "{{workflow.input.priority}}"
        Operator = "=="
        Value2 = "urgent"
    }
    $logUrgentInputs = @{
        Msg = "URGENT APPLICATION - route to lecturer immediately: {{workflow.input.applicationId}} / {{workflow.input.applicantName}}"
    }
    $logNormalInputs = @{
        Msg = "NORMAL APPLICATION - queued for standard review: {{workflow.input.applicationId}} / {{workflow.input.applicantName}}"
    }

    $steps = @(
        @{
            Id = "webhook_application_form"
            Type = "WebhookTrigger"
            ExecutionMode = "BuiltIn"
            IsConfigured = $true
            Inputs = $webhookInputs
        },
        @{
            Id = "log_received_application"
            Type = "Log"
            ExecutionMode = "BuiltIn"
            IsConfigured = $true
            Inputs = $logReceivedInputs
        },
        @{
            Id = "if_urgent_application"
            Type = "If"
            ExecutionMode = "BuiltIn"
            IsConfigured = $true
            Inputs = $ifInputs
        },
        @{
            Id = "log_urgent_application"
            Type = "Log"
            ExecutionMode = "BuiltIn"
            IsConfigured = $true
            Inputs = $logUrgentInputs
        },
        @{
            Id = "log_normal_application"
            Type = "Log"
            ExecutionMode = "BuiltIn"
            IsConfigured = $true
            Inputs = $logNormalInputs
        }
    )

    $transitions = @(
        @{ Id = "t_webhook_log"; Source = "webhook_application_form"; Target = "log_received_application" },
        @{ Id = "t_log_if"; Source = "log_received_application"; Target = "if_urgent_application" },
        @{ Id = "t_if_urgent"; Source = "if_urgent_application"; Target = "log_urgent_application"; Condition = "{{steps.if_urgent_application.Output.IsMatch}} === true" },
        @{ Id = "t_if_normal"; Source = "if_urgent_application"; Target = "log_normal_application"; Condition = "{{steps.if_urgent_application.Output.IsMatch}} === false" }
    )

    $nodes = @(
        New-WorkflowNode -Id "webhook_application_form" -PluginName "WebhookTrigger" -DisplayName "Webhook Trigger - Form/Web App" -Category "Trigger" -ExecutionMode "BuiltIn" -ExecutionMetadata $null -PackageId $null -Version "Built-in" -InputSchema $null -Inputs $webhookInputs -X 0 -Y 120 -IsStart $true
        New-WorkflowNode -Id "log_received_application" -PluginName "Log" -DisplayName "Log Received Application" -Category "Core" -ExecutionMode "BuiltIn" -ExecutionMetadata $null -PackageId $null -Version "Built-in" -InputSchema $null -Inputs $logReceivedInputs -X 330 -Y 120
        New-WorkflowNode -Id "if_urgent_application" -PluginName "If" -DisplayName "Priority Is Urgent?" -Category "Logic" -ExecutionMode "BuiltIn" -ExecutionMetadata $null -PackageId $null -Version "Built-in" -InputSchema $null -Inputs $ifInputs -X 660 -Y 120
        New-WorkflowNode -Id "log_urgent_application" -PluginName "Log" -DisplayName "Log Urgent Route" -Category "Core" -ExecutionMode "BuiltIn" -ExecutionMetadata $null -PackageId $null -Version "Built-in" -InputSchema $null -Inputs $logUrgentInputs -X 990 -Y 20
        New-WorkflowNode -Id "log_normal_application" -PluginName "Log" -DisplayName "Log Standard Queue" -Category "Core" -ExecutionMode "BuiltIn" -ExecutionMetadata $null -PackageId $null -Version "Built-in" -InputSchema $null -Inputs $logNormalInputs -X 990 -Y 220
    )

    $edges = @(
        New-WorkflowEdge -Id "edge-webhook-log" -Source "webhook_application_form" -Target "log_received_application"
        New-WorkflowEdge -Id "edge-log-if" -Source "log_received_application" -Target "if_urgent_application"
        New-WorkflowEdge -Id "edge-if-urgent" -Source "if_urgent_application" -Target "log_urgent_application" -Condition "{{steps.if_urgent_application.Output.IsMatch}} === true"
        New-WorkflowEdge -Id "edge-if-normal" -Source "if_urgent_application" -Target "log_normal_application" -Condition "{{steps.if_urgent_application.Output.IsMatch}} === false"
    )

    return @{
        Name = $WebhookWorkflowName
        Description = "Webhook-trigger workflow demo that receives an application profile from a form or web app and routes it by priority."
        DefinitionJson = @{
            Steps = $steps
            Transitions = $transitions
        }
        UiJson = @{
            nodes = $nodes
            edges = $edges
        }
    }
}

function New-CronSheetCheckWorkflowPayload {
    param(
        [hashtable]$ReadPlugin,
        [hashtable]$QualityPlugin,
        [hashtable]$AnalyzePlugin
    )

    $cronInputs = @{
        CronExpression = $CronExpression
        TimeZoneId = $CronTimeZoneId
    }
    $readInputs = @{
        SheetUrl = $SampleSheetUrl
        Gid = $SampleGid
        MaxRows = 100
        HasHeader = $true
    }
    $qualityInputs = @{
        RowsJson = "{{steps.read_google_sheet_on_schedule.Output.RowsJson}}"
        RequiredColumnsCsv = "applicantName,major,gpa,englishScore,experienceMonths,documentText"
    }
    $analyzeInputs = @{
        RowsJson = "{{steps.read_google_sheet_on_schedule.Output.RowsJson}}"
        MinimumGpa = 2.8
        MinimumEnglishScore = 550
        TargetExperienceMonths = 6
    }
    $logInputs = @{
        Msg = "CRON SHEET CHECK - {{steps.read_google_sheet_on_schedule.Output.Summary}} | {{steps.sheet_quality_check_on_schedule.Output.Summary}} | {{steps.analyze_sheet_on_schedule.Output.Summary}}"
    }

    $steps = @(
        @{
            Id = "cron_sheet_check"
            Type = "CronTrigger"
            ExecutionMode = "BuiltIn"
            IsConfigured = $true
            Inputs = $cronInputs
        },
        @{
            Id = "read_google_sheet_on_schedule"
            Type = $ReadPlugin.UniqueName
            ExecutionMode = "DynamicDll"
            IsConfigured = $true
            ExecutionMetadata = $ReadPlugin.ExecutionMetadata
            Inputs = $readInputs
        },
        @{
            Id = "sheet_quality_check_on_schedule"
            Type = $QualityPlugin.UniqueName
            ExecutionMode = "DynamicDll"
            IsConfigured = $true
            ExecutionMetadata = $QualityPlugin.ExecutionMetadata
            Inputs = $qualityInputs
        },
        @{
            Id = "analyze_sheet_on_schedule"
            Type = $AnalyzePlugin.UniqueName
            ExecutionMode = "DynamicDll"
            IsConfigured = $true
            ExecutionMetadata = $AnalyzePlugin.ExecutionMetadata
            Inputs = $analyzeInputs
        },
        @{
            Id = "log_cron_sheet_result"
            Type = "Log"
            ExecutionMode = "BuiltIn"
            IsConfigured = $true
            Inputs = $logInputs
        }
    )

    $transitions = @(
        @{ Id = "t_cron_read"; Source = "cron_sheet_check"; Target = "read_google_sheet_on_schedule" },
        @{ Id = "t_read_quality"; Source = "read_google_sheet_on_schedule"; Target = "sheet_quality_check_on_schedule" },
        @{ Id = "t_quality_analyze"; Source = "sheet_quality_check_on_schedule"; Target = "analyze_sheet_on_schedule" },
        @{ Id = "t_analyze_log"; Source = "analyze_sheet_on_schedule"; Target = "log_cron_sheet_result" }
    )

    $nodes = @(
        New-WorkflowNode -Id "cron_sheet_check" -PluginName "CronTrigger" -DisplayName "Cron Trigger - Every 20 Seconds" -Category "Trigger" -ExecutionMode "BuiltIn" -ExecutionMetadata $null -PackageId $null -Version "Built-in" -InputSchema $null -Inputs $cronInputs -X 0 -Y 160 -IsStart $true
        New-WorkflowNode -Id "read_google_sheet_on_schedule" -PluginName $ReadPlugin.UniqueName -DisplayName $ReadPlugin.DisplayName -Category "Google Sheet Review" -ExecutionMode "DynamicDll" -ExecutionMetadata $ReadPlugin.ExecutionMetadata -PackageId $ReadPlugin.PackageId -Version $ReadPlugin.Version -InputSchema $ReadPlugin.InputSchema -Inputs $readInputs -X 330 -Y 160
        New-WorkflowNode -Id "sheet_quality_check_on_schedule" -PluginName $QualityPlugin.UniqueName -DisplayName $QualityPlugin.DisplayName -Category "Google Sheet Review" -ExecutionMode "DynamicDll" -ExecutionMetadata $QualityPlugin.ExecutionMetadata -PackageId $QualityPlugin.PackageId -Version $QualityPlugin.Version -InputSchema $QualityPlugin.InputSchema -Inputs $qualityInputs -X 660 -Y 160
        New-WorkflowNode -Id "analyze_sheet_on_schedule" -PluginName $AnalyzePlugin.UniqueName -DisplayName $AnalyzePlugin.DisplayName -Category "Google Sheet Review" -ExecutionMode "DynamicDll" -ExecutionMetadata $AnalyzePlugin.ExecutionMetadata -PackageId $AnalyzePlugin.PackageId -Version $AnalyzePlugin.Version -InputSchema $AnalyzePlugin.InputSchema -Inputs $analyzeInputs -X 990 -Y 160
        New-WorkflowNode -Id "log_cron_sheet_result" -PluginName "Log" -DisplayName "Log Scheduled Check Result" -Category "Core" -ExecutionMode "BuiltIn" -ExecutionMetadata $null -PackageId $null -Version "Built-in" -InputSchema $null -Inputs $logInputs -X 1320 -Y 160
    )

    $edges = @(
        New-WorkflowEdge -Id "edge-cron-read" -Source "cron_sheet_check" -Target "read_google_sheet_on_schedule"
        New-WorkflowEdge -Id "edge-read-quality-cron" -Source "read_google_sheet_on_schedule" -Target "sheet_quality_check_on_schedule"
        New-WorkflowEdge -Id "edge-quality-analyze-cron" -Source "sheet_quality_check_on_schedule" -Target "analyze_sheet_on_schedule"
        New-WorkflowEdge -Id "edge-analyze-log-cron" -Source "analyze_sheet_on_schedule" -Target "log_cron_sheet_result"
    )

    return @{
        Name = $CronWorkflowName
        Description = "Cron-trigger workflow demo that checks a Google Sheet automatically. The schedule is shortened to every 20 seconds for classroom demo."
        DefinitionJson = @{
            Steps = $steps
            Transitions = $transitions
        }
        UiJson = @{
            nodes = $nodes
            edges = $edges
        }
    }
}

function Get-SampleInputData {
    return @{
        sheetUrl = $SampleSheetUrl
        gid = $SampleGid
        maxRows = 200
        hasHeader = $true
        minimumGpa = 2.8
        minimumEnglishScore = 550
        targetExperienceMonths = 6
        dryRun = $DryRun
        appsScriptWebhookUrl = $AppsScriptWebhookUrl
    }
}

function Get-SampleWebhookInputData {
    return @{
        applicationId = "HS-DEMO-001"
        applicantName = "Nguyen Van A"
        email = "nguyenvana@example.edu.vn"
        major = "Artificial Intelligence"
        gpa = 3.45
        englishScore = 720
        experienceMonths = 8
        priority = "urgent"
        note = "Demo payload from external form/web app."
    }
}

function Get-SampleCronInputData {
    return @{
        cronExpression = $CronExpression
        timeZoneId = $CronTimeZoneId
        sheetUrl = $SampleSheetUrl
        gid = $SampleGid
    }
}

function New-DemoWorkflowDefinition {
    param(
        [hashtable]$Payload,
        [hashtable]$InputData,
        [bool]$PublishWorkflow
    )

    $name = [string]$Payload.Name
    Write-Host "Creating workflow definition: $name..."
    $createResponse = Invoke-ApiJson -Method Post -Url "$ApiBaseUrl/workflows/definitions" -Token $AccessToken -Body $Payload
    $workflow = Get-JsonProperty $createResponse @("data", "Data")
    $definitionId = Get-JsonProperty $workflow @("id", "Id")

    if (-not $definitionId) {
        throw "Could not resolve workflow definition id for $name."
    }

    if ($InputData) {
        Write-Host "Saving default input data for: $name..."
        Invoke-ApiJson -Method Put -Url "$ApiBaseUrl/workflows/definitions/$definitionId/input-data" -Token $AccessToken -Body @{ InputData = $InputData } | Out-Null
    }

    if ($PublishWorkflow) {
        Write-Host "Publishing workflow definition: $name..."
        Invoke-ApiJson -Method Post -Url "$ApiBaseUrl/workflows/definitions/$definitionId/publish" -Token $AccessToken | Out-Null
    }

    return [pscustomobject]@{
        Name = $name
        DefinitionId = $definitionId
        UiUrl = "$FrontendBaseUrl/workflows/$definitionId/edit"
        ApiUrl = "$ApiBaseUrl/workflows/$definitionId"
        Published = $PublishWorkflow
    }
}

Write-Host "AWE Google Sheet Review workflow seed"
Write-Host "Environment: $Environment"
Write-Host "API: $ApiBaseUrl"
Write-Host "Keycloak: $KeycloakBaseUrl"
Write-Host "Frontend: $FrontendBaseUrl"
Write-Host "Env file: $envFile"
Write-Host ""

Assert-HttpEndpointReachable `
    -Name "Keycloak" `
    -Url "$KeycloakBaseUrl/realms/$Realm/.well-known/openid-configuration" `
    -Hint "Start Keycloak first or pass -KeycloakBaseUrl with the correct URL."

Assert-HttpEndpointReachable `
    -Name "AWE API" `
    -Url $ApiBaseUrl `
    -Hint "Start the API Gateway first or pass -ApiBaseUrl with the correct URL."

if (-not $AccessToken) {
    $AccessToken = Ensure-SeederUser
}

Remove-ExistingDemoWorkflows -Token $AccessToken

$pluginRoot = Join-Path $EngineRoot "test\demo-google-sheet-review"
$pluginSpecs = @(
    @{
        UniqueName = "AWE.Demo.GoogleSheetReview.ReadGoogleSheetPlugin"
        DisplayName = "Demo - Read Google Sheet"
        Description = "Reads a public Google Sheet through CSV export."
        Icon = "lucide-database"
        Project = Join-Path $pluginRoot "AWE.Demo.GoogleSheetRead\AWE.Demo.GoogleSheetRead.csproj"
    },
    @{
        UniqueName = "AWE.Demo.GoogleSheetReview.SheetQualityCheckPlugin"
        DisplayName = "Demo - Sheet Quality Check"
        Description = "Checks required columns and basic data quality."
        Icon = "lucide-filter"
        Project = Join-Path $pluginRoot "AWE.Demo.GoogleSheetQualityCheck\AWE.Demo.GoogleSheetQualityCheck.csproj"
    },
    @{
        UniqueName = "AWE.Demo.GoogleSheetReview.AnalyzeSheetApplicationsPlugin"
        DisplayName = "Demo - Analyze Sheet Applications"
        Description = "Scores application rows and creates decisions."
        Icon = "lucide-clipboard-check"
        Project = Join-Path $pluginRoot "AWE.Demo.GoogleSheetAnalyze\AWE.Demo.GoogleSheetAnalyze.csproj"
    },
    @{
        UniqueName = "AWE.Demo.GoogleSheetReview.WriteBackSheetResultsPlugin"
        DisplayName = "Demo - Write Back Sheet Results"
        Description = "Prepares results and optionally posts them to Google Apps Script."
        Icon = "lucide-send"
        Project = Join-Path $pluginRoot "AWE.Demo.GoogleSheetWriteBack\AWE.Demo.GoogleSheetWriteBack.csproj"
    }
)

$registered = @{}
foreach ($spec in $pluginSpecs) {
    $plugin = Register-Plugin -Spec $spec -Token $AccessToken
    $registered[$spec.UniqueName] = $plugin
}

$workflowPayload = New-GoogleSheetReviewWorkflowPayload `
    -ReadPlugin $registered["AWE.Demo.GoogleSheetReview.ReadGoogleSheetPlugin"] `
    -QualityPlugin $registered["AWE.Demo.GoogleSheetReview.SheetQualityCheckPlugin"] `
    -AnalyzePlugin $registered["AWE.Demo.GoogleSheetReview.AnalyzeSheetApplicationsPlugin"] `
    -WriteBackPlugin $registered["AWE.Demo.GoogleSheetReview.WriteBackSheetResultsPlugin"]

$webhookWorkflowPayload = New-WebhookApplicationIntakeWorkflowPayload
$cronWorkflowPayload = New-CronSheetCheckWorkflowPayload `
    -ReadPlugin $registered["AWE.Demo.GoogleSheetReview.ReadGoogleSheetPlugin"] `
    -QualityPlugin $registered["AWE.Demo.GoogleSheetReview.SheetQualityCheckPlugin"] `
    -AnalyzePlugin $registered["AWE.Demo.GoogleSheetReview.AnalyzeSheetApplicationsPlugin"]

$createdWorkflows = @()
$createdWorkflows += New-DemoWorkflowDefinition `
    -Payload $workflowPayload `
    -InputData (Get-SampleInputData) `
    -PublishWorkflow $Publish.IsPresent

$createdWorkflows += New-DemoWorkflowDefinition `
    -Payload $webhookWorkflowPayload `
    -InputData (Get-SampleWebhookInputData) `
    -PublishWorkflow ($Publish.IsPresent -or $PublishTriggerDemos)

$createdWorkflows += New-DemoWorkflowDefinition `
    -Payload $cronWorkflowPayload `
    -InputData (Get-SampleCronInputData) `
    -PublishWorkflow ($Publish.IsPresent -or $PublishTriggerDemos)

$routePath = Get-NormalizedWebhookRoutePath -RoutePath $WebhookRoutePath
$webhookUrl = "$ApiBaseUrl/webhooks/catch/$routePath"
$sampleWebhookBody = @{
    applicationId = "HS-DEMO-$([DateTime]::UtcNow.ToString('HHmmss'))"
    applicantName = "Nguyen Van A"
    email = "nguyenvana@example.edu.vn"
    major = "Artificial Intelligence"
    gpa = 3.45
    englishScore = 720
    experienceMonths = 8
    priority = "urgent"
    note = "Demo payload from external form/web app."
} | ConvertTo-Json -Compress

Write-Host ""
Write-Host "Seed completed."
Write-Host "Plugin version uploaded: $PluginVersion"
Write-Host ""
Write-Host "Created workflows:"
foreach ($created in $createdWorkflows) {
    Write-Host "- $($created.Name)"
    Write-Host "  Definition id: $($created.DefinitionId)"
    Write-Host "  UI url: $($created.UiUrl)"
    Write-Host "  API url: $($created.ApiUrl)"
    Write-Host "  Published: $($created.Published)"
}
Write-Host ""
Write-Host "Webhook demo URL:"
Write-Host $webhookUrl
Write-Host "Webhook sample form: $EngineRoot\samples\demo\webhook-application-form.html"
Write-Host ""
Write-Host "Sample webhook POST:"
Write-Host "Invoke-RestMethod -Method Post -Uri `"$webhookUrl`" -ContentType `"application/json`" -Body '$sampleWebhookBody'"
Write-Host ""
Write-Host "Cron demo schedule: $CronExpression ($CronTimeZoneId)"

if ($SampleSheetUrl -like "*YOUR_SHEET_ID*") {
    Write-Host ""
    Write-Host "Reminder: replace SampleSheetUrl with a real public Google Sheets link before running the workflow."
    Write-Host "Sample CSV to import into Google Sheets: $RepoRoot\AWE-automation-workflow-engine\samples\demo\google-sheet-applications.csv"
}
