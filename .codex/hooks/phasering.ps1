$ErrorActionPreference = "Stop"
$Utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding = $Utf8Strict
[Console]::OutputEncoding = $Utf8NoBom

$HookVersion = "0.1.3"
$MaxModeBytes = 32768
$ProjectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$RuntimeRoot = Join-Path $ProjectRoot ".phasering\runtime"
$HealthPath = Join-Path $RuntimeRoot "health.json"
$CurrentPath = Join-Path $RuntimeRoot "current.json"
$ModesRoot = [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot ".phasering\modes"))

$ErrorCode = "UNEXPECTED"
$ModeFile = $null
$Revision = $null
$ContentSha256 = $null
$TurnId = $null

function Write-AtomicUtf8 {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $Directory = Split-Path -Parent $Path
    [System.IO.Directory]::CreateDirectory($Directory) | Out-Null
    $TemporaryPath = "$Path.$PID.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [System.IO.File]::WriteAllText($TemporaryPath, $Content, $Utf8NoBom)
        Move-Item -LiteralPath $TemporaryPath -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $TemporaryPath) {
            Remove-Item -LiteralPath $TemporaryPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-PreviousSuccessTime {
    if (-not (Test-Path -LiteralPath $HealthPath -PathType Leaf)) {
        return $null
    }
    try {
        $Previous = [System.IO.File]::ReadAllText($HealthPath, $Utf8Strict) | ConvertFrom-Json
        if ($null -ne $Previous.lastSuccessAt) {
            return [string]$Previous.lastSuccessAt
        }
    }
    catch {
        return $null
    }
    return $null
}

function Write-Health {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("success", "error")][string]$Status,
        [Parameter(Mandatory = $true)][string]$AttemptAt,
        [AllowNull()][string]$SuccessAt,
        [AllowNull()][hashtable]$ErrorValue
    )

    $Health = [ordered]@{
        schemaVersion = 1
        hookVersion = $HookVersion
        status = $Status
        modeFile = $ModeFile
        revision = $Revision
        contentSha256 = $ContentSha256
        lastAttemptAt = $AttemptAt
        lastSuccessAt = $SuccessAt
        turnId = $TurnId
        error = $ErrorValue
    }
    Write-AtomicUtf8 -Path $HealthPath -Content (($Health | ConvertTo-Json -Depth 8 -Compress) + "`n")
}

function Write-HookJson {
    param([Parameter(Mandatory = $true)][hashtable]$Value)
    [Console]::Out.WriteLine(($Value | ConvertTo-Json -Depth 8 -Compress))
}

try {
    $ErrorCode = "INPUT_INVALID"
    $RawInput = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($RawInput)) {
        throw "Hook did not receive JSON input"
    }
    $HookInput = $RawInput | ConvertFrom-Json
    if ([string]$HookInput.hook_event_name -ne "UserPromptSubmit") {
        throw "Hook event is not UserPromptSubmit"
    }
    if ($null -ne $HookInput.turn_id) {
        $TurnId = [string]$HookInput.turn_id
    }

    $ErrorCode = "CURRENT_INVALID"
    if (-not (Test-Path -LiteralPath $CurrentPath -PathType Leaf)) {
        throw "Missing .phasering/runtime/current.json"
    }
    $Current = [System.IO.File]::ReadAllText($CurrentPath, $Utf8Strict) | ConvertFrom-Json
    if ([int]$Current.schemaVersion -ne 1) {
        throw "current.json has an unsupported schema version"
    }
    $ModeFile = [string]$Current.modeFile
    $Revision = [long]$Current.revision
    if ([string]::IsNullOrWhiteSpace($ModeFile) -or $Revision -lt 1) {
        throw "current.json contains an invalid selection"
    }

    $ErrorCode = "MODE_PATH_INVALID"
    $RelativeMode = $ModeFile.Replace("/", [System.IO.Path]::DirectorySeparatorChar)
    if ([System.IO.Path]::IsPathRooted($RelativeMode) -or -not $RelativeMode.StartsWith("modes$([System.IO.Path]::DirectorySeparatorChar)", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The selected phase path must be inside the modes directory"
    }
    $ModePath = [System.IO.Path]::GetFullPath((Join-Path (Join-Path $ProjectRoot ".phasering") $RelativeMode))
    $ModesPrefix = $ModesRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $ModePath.StartsWith($ModesPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The selected phase path escapes the modes directory"
    }
    if ([System.IO.Path]::GetExtension($ModePath) -ne ".md") {
        throw "The selected phase file must use the .md extension"
    }

    $ErrorCode = "MODE_INVALID"
    if (-not (Test-Path -LiteralPath $ModePath -PathType Leaf)) {
        throw "The selected phase file does not exist"
    }
    $ModeItem = Get-Item -LiteralPath $ModePath -Force
    if (($ModeItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The selected phase file cannot be a symlink or reparse point"
    }
    if ($ModeItem.Length -le 0) {
        throw "The selected phase file is empty"
    }
    if ($ModeItem.Length -gt $MaxModeBytes) {
        throw "The selected phase file exceeds the 32768 byte limit"
    }

    $Content = [System.IO.File]::ReadAllText($ModePath, $Utf8Strict)
    $FirstContent = ($Content -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1).Trim()
    if ($FirstContent -notmatch '^#\s+\S') {
        throw "The first content in the selected phase file must be an H1"
    }

    $Hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        $HashBytes = $Hasher.ComputeHash($Utf8NoBom.GetBytes($Content))
        $ContentSha256 = "sha256:" + ([System.BitConverter]::ToString($HashBytes).Replace("-", "").ToLowerInvariant())
    }
    finally {
        $Hasher.Dispose()
    }

    $AttemptAt = [DateTime]::UtcNow.ToString("o")
    try {
        Write-Health -Status "success" -AttemptAt $AttemptAt -SuccessAt $AttemptAt -ErrorValue $null
    }
    catch {
        [Console]::Error.WriteLine("PhaseRing health write failed: $($_.Exception.Message)")
    }

    $AdditionalContext = @"
[PhaseRing]
The following stage document is active for this turn.
It overrides earlier PhaseRing stage documents in this conversation.

$Content
"@
    Write-HookJson -Value @{
        hookSpecificOutput = @{
            hookEventName = "UserPromptSubmit"
            additionalContext = $AdditionalContext
        }
    }
}
catch {
    $AttemptAt = [DateTime]::UtcNow.ToString("o")
    $SafeMessage = $_.Exception.Message
    $PreviousSuccessAt = Get-PreviousSuccessTime
    try {
        Write-Health -Status "error" -AttemptAt $AttemptAt -SuccessAt $PreviousSuccessAt -ErrorValue @{
            code = $ErrorCode
            message = $SafeMessage
        }
    }
    catch {
        [Console]::Error.WriteLine("PhaseRing health write failed: $($_.Exception.Message)")
    }
    Write-HookJson -Value @{
        decision = "block"
        reason = "PhaseRing [$ErrorCode]: $SafeMessage"
    }
}
