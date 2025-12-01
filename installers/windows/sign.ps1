param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,
    [string]$Description = "",
    [string]$CertFile,
    [string]$Alias,
    [string]$Keystore = "eu-west-1",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [string]$JsignVersion = "6.0"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -Path $FilePath)) {
    throw "File '$FilePath' was not found."
}

$FilePath = [System.IO.Path]::GetFullPath($FilePath)

if (-not $CertFile) {
    $relativeCert = Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath "..\..\installers\windows") -ChildPath "tracebit.crt"
    $CertFile = [System.IO.Path]::GetFullPath($relativeCert)
}

if (-not (Test-Path -Path $CertFile)) {
    throw "Certificate file '$CertFile' was not found."
}

$Alias = if ($Alias) { $Alias } elseif ($env:KMS_KEY_ARN) { $env:KMS_KEY_ARN } else { "" }
if (-not $Alias) {
    throw "KMS alias/key ARN not provided. Pass -Alias or set KMS_KEY_ARN."
}

$javaCommand = Get-Command java -ErrorAction SilentlyContinue
if (-not $javaCommand) {
    throw "Java runtime (java) was not found on PATH."
}

$tempDir = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$jsignJar = Join-Path -Path $tempDir -ChildPath "jsign-$JsignVersion.jar"

if (-not (Test-Path -Path $jsignJar)) {
    if (-not ([System.Net.ServicePointManager]::SecurityProtocol -band [System.Net.SecurityProtocolType]::Tls12)) {
        [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor [System.Net.SecurityProtocolType]::Tls12
    }
    Write-Host "Downloading jsign $JsignVersion..."
    $jsignUri = "https://github.com/ebourg/jsign/releases/download/$JsignVersion/jsign-$JsignVersion.jar"
    Invoke-WebRequest -Uri $jsignUri -OutFile $jsignJar
}

$arguments = @("-jar", $jsignJar)
if ($Description) {
    $arguments += @("--name", $Description)
}
$arguments += @(
    "--storetype", "AWS",
    "--keystore", $Keystore,
    "--alias", $Alias,
    "--tsaurl", $TimestampUrl,
    "--certfile", $CertFile,
    $FilePath
)

Write-Host "Signing $FilePath..."
& $javaCommand.Source $arguments
if ($LASTEXITCODE -ne 0) {
    throw "jsign failed with exit code $LASTEXITCODE."
}

Write-Host "Signed $FilePath."
