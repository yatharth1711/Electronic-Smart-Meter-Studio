param([Parameter(Mandatory = $true)][string]$BaseUrl)
$ErrorActionPreference = 'Stop'
# Run against a development instance. Only the meter created below is mutated/removed.
function Call-Api([string]$Method, [string]$Path, $Body = $null) {
    $arguments = @{ Method = $Method; Uri = "$BaseUrl$Path"; ContentType = 'application/json' }
    if ($null -ne $Body) { $arguments.Body = ConvertTo-Json -InputObject $Body -Depth 16 -Compress }
    Invoke-RestMethod @arguments
}
function Assert-True([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Expect-Status([int]$Expected, [scriptblock]$Action) {
    try { & $Action | Out-Null } catch {
        if ([int]$_.Exception.Response.StatusCode -eq $Expected) { return }
        throw
    }
    throw "Expected HTTP $Expected but operation succeeded."
}
$page = Invoke-WebRequest -Uri "$BaseUrl/objects" -UseBasicParsing
Assert-True ($page.StatusCode -eq 200 -and $page.Content.Contains('CLASS DIRECTORY')) 'Directory page did not render.'
$css = Invoke-WebRequest -Uri "$BaseUrl/SmartMeterStudio.Web.styles.css" -UseBasicParsing
Assert-True ($css.StatusCode -eq 200 -and $css.Content.Contains('.directory-layout')) 'Directory stylesheet is missing.'
$catalog = Call-Api GET '/api/cosem/classes'
Assert-True (@($catalog | Where-Object id -eq 170).Count -eq 1) 'Edition 17 Attestation catalog entry is missing.'
$smokeName = 'COSEM-smoke-' + [guid]::NewGuid().ToString('N')
$created = Call-Api POST '/api/meters' @{ name = $smokeName; nominalVoltage = 230; baseLoadKw = 12; nominalPowerFactor = 0.95; phaseMode = 'ThreePhase' }
$meterId = $created.definition.id
Assert-True ($created.definition.name -eq $smokeName -and $meterId) 'Test meter creation failed.'
$root = "/api/meters/$meterId/cosem"
try {
    Call-Api POST "/api/meters/$meterId/stop" | Out-Null
    $registerLn = '0.128.1.0.0.255'
    $profileLn = '0.128.99.1.1.255'
    $scriptLn = '0.128.10.0.0.255'
    $daysLn = '0.128.11.0.0.255'
    Call-Api POST "$root/objects" @{ classId = 3; logicalName = $registerLn; name = 'Smoke voltage'; value = 23000; scaler = -2; unit = 35 } | Out-Null
    Call-Api POST "$root/objects" @{ classId = 7; logicalName = $profileLn; name = 'Smoke capture' } | Out-Null
    Call-Api PUT "$root/profiles/$profileLn/configuration" @{ captureObjects = @(@{ classId = 3; logicalName = $registerLn; attributeIndex = 2; dataIndex = 0 }); capturePeriod = 0; capacity = 3 } | Out-Null
    Call-Api POST "$root/objects/$profileLn/methods/2" @{ parameter = 0 } | Out-Null
    Call-Api PUT "$root/objects/$registerLn/attributes/2" @{ value = 24000 } | Out-Null
    Call-Api POST "$root/objects/$profileLn/methods/2" @{ parameter = 0 } | Out-Null
    $rows = Call-Api GET "$root/profiles/$profileLn/rows?start=1&count=100"
    Assert-True ($rows.Count -eq 2 -and $rows[0].values[0] -eq 23000 -and $rows[1].values[0] -eq 24000) ("Profile snapshots failed over HTTP: " + (ConvertTo-Json -InputObject $rows -Depth 8 -Compress))
    $buffer = Call-Api GET "$root/objects/$profileLn/attributes/2"
    Assert-True ($buffer.value.Count -eq 2) 'Profile buffer attribute read failed.'
    Expect-Status 422 { Call-Api PUT "$root/objects/0.0.96.1.0.255/attributes/2" @{ value = 'overwrite' } }
    Expect-Status 400 { Call-Api PUT "$root/objects/$registerLn/attributes/2" @{ value = 'not numeric' } }
    Expect-Status 400 { Call-Api DELETE "$root/objects/$registerLn" }
    Expect-Status 404 { Call-Api GET '/api/meters/missing/cosem/objects' }
    Expect-Status 400 { Call-Api POST "$root/objects/0.0.1.0.0.255/methods/5" @{} }
    Call-Api POST "$root/objects" @{ classId = 9; logicalName = $scriptLn; name = 'Smoke script' } | Out-Null
    Call-Api PUT "$root/objects/$scriptLn/attributes/2" @{ value = @(@{ scriptIdentifier = 1; actions = @(@{ serviceId = 1; classId = 3; logicalName = $registerLn; index = 2; parameter = 25000 }) }) } | Out-Null
    Call-Api POST "$root/objects/$scriptLn/methods/1" @{ parameter = 1 } | Out-Null
    $registerValue = Call-Api GET "$root/objects/$registerLn/attributes/2"
    Assert-True ($registerValue.value -eq 25000) 'Script execution failed over HTTP.'
    Call-Api POST "$root/objects" @{ classId = 11; logicalName = $daysLn; name = 'Smoke holidays' } | Out-Null
    Call-Api POST "$root/objects/$daysLn/methods/1" @{ parameter = @{ index = 1; specialdayDate = '2026-12-25'; dayId = 3 } } | Out-Null
    $days = Call-Api GET "$root/objects/$daysLn/attributes/2"
    Assert-True ($days.value.Count -eq 1 -and $days.value[0].dayId -eq 3) 'Special-day insertion failed over HTTP.'
    Write-Output "PASS HTTP: directory, CSS, $($catalog.Count) class IDs, object CRUD, buffers, validation, scripts and special days."
}
finally {
    $cleanupMeter = Call-Api GET "/api/meters/$meterId"
    if ($cleanupMeter.definition.name -eq $smokeName) { Call-Api DELETE "/api/meters/$meterId" | Out-Null; Write-Output "Removed test-only meter $meterId ($smokeName)." }
}
