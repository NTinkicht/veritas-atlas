param(
    [string]$BaseUrl = "http://localhost:5091",
    [string]$Username = "admin1",
    [string]$Password = "password123"
)

$ErrorActionPreference = "Stop"

function Get-StringValue {
    param($Object, [string[]]$Names)
    if ($null -eq $Object) { return "" }

    foreach ($n in $Names) {
        $prop = $Object.PSObject.Properties[$n]
        if ($null -ne $prop) {
            $v = $prop.Value
            if ($null -ne $v -and "$v".Trim() -ne "") { return "$v" }
        }
    }
    return ""
}

function Add-Line {
    param([string]$Text)
    Write-Host $Text
}

function Invoke-Step {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Url,
        [hashtable]$Headers
    )

    try {
        $result = Invoke-RestMethod -Method $Method -Uri $Url -Headers $Headers
        Add-Line "$Name - PASS - $Method $Url (200)"
        return $result
    }
    catch {
        Add-Line "$Name - FAIL - $Method $Url"
        $resp = $_.Exception.Response
        if ($null -ne $resp) {
            try {
                $stream = $resp.GetResponseStream()
                if ($null -ne $stream) {
                    $reader = New-Object System.IO.StreamReader($stream)
                    $body = $reader.ReadToEnd()
                    Add-Line $body
                }
            }
            catch { }
        }
        throw
    }
}

function Login-And-GetHeaders {
    param(
        [string]$BaseUrl,
        [string]$Username,
        [string]$Password
    )

    $loginUrl = "$BaseUrl/api/v1/auth/login"
    $loginBody = @{
        username = $Username
        password = $Password
    }

    $loginResponse = Invoke-RestMethod -Method POST -Uri $loginUrl -Body ($loginBody | ConvertTo-Json -Depth 5) -ContentType "application/json"
    $token = Get-StringValue $loginResponse @("accessToken","token")
    if (-not $token) {
        throw "Login succeeded but accessToken was not returned."
    }

    Add-Line "Login SUCCESS"

    return @{
        Authorization = "Bearer $token"
        Accept = "application/json"
    }
}

function Get-AuditJson {
    param([string]$BaseUrl, [hashtable]$Headers)
    return Invoke-RestMethod -Method GET -Uri "$BaseUrl/api/v1/workflow-audit/entries" -Headers $Headers
}

function Get-SnapshotJson {
    param([string]$BaseUrl, [hashtable]$Headers)
    return Invoke-RestMethod -Method GET -Uri "$BaseUrl/api/v1/persistence/snapshot" -Headers $Headers
}

function Assert-Contains {
    param(
        [string]$Name,
        [string]$Haystack,
        [string]$Needle
    )

    if ($Haystack -match [regex]::Escape($Needle)) {
        Add-Line "$Name - PASS - found '$Needle'"
    }
    else {
        Add-Line "$Name - FAIL - missing '$Needle'"
    }
}

Add-Line "=== VERITAS WORKFLOW EXTENDED TEST ==="

$headers = Login-And-GetHeaders -BaseUrl $BaseUrl -Username $Username -Password $Password

Invoke-Step "ClearWorkflowAudit" "POST" "$BaseUrl/api/v1/workflow-audit/clear" $headers | Out-Null
Invoke-Step "ResetPersistence" "POST" "$BaseUrl/api/v1/persistence/reset" $headers | Out-Null

# Scenario 1: Reject path
Add-Line ""
Add-Line "=== Scenario 1: Reject path ==="
$seed1 = Invoke-Step "SeedLifecycle(Reject)" "POST" "$BaseUrl/api/v1/actions/seed/lifecycle" $headers
$caseId1 = Get-StringValue $seed1 @("caseId","CaseId")

Invoke-Step "SubmitCase(Reject)" "POST" "$BaseUrl/api/v1/actions/cases/$caseId1/submit" $headers | Out-Null
Invoke-Step "RejectCase" "POST" "$BaseUrl/api/v1/actions/cases/$caseId1/reject" $headers | Out-Null

$audit1 = Get-AuditJson -BaseUrl $BaseUrl -Headers $headers | ConvertTo-Json -Depth 20
$snapshot1 = Get-SnapshotJson -BaseUrl $BaseUrl -Headers $headers

Assert-Contains "Audit contains SubmitCase" $audit1 "SubmitCase"
Assert-Contains "Audit contains RejectCase" $audit1 "RejectCase"

$caseStatus1 = Get-StringValue $snapshot1 @("caseStatus","CaseStatus")
if ($caseStatus1 -eq "Rejected") {
    Add-Line "Snapshot after reject - PASS - caseStatus=Rejected"
}
else {
    Add-Line "Snapshot after reject - FAIL - caseStatus=$caseStatus1"
}

# Scenario 2: Hold path
Add-Line ""
Add-Line "=== Scenario 2: Hold path ==="
Invoke-Step "ClearWorkflowAudit(Hold)" "POST" "$BaseUrl/api/v1/workflow-audit/clear" $headers | Out-Null
Invoke-Step "ResetPersistence(Hold)" "POST" "$BaseUrl/api/v1/persistence/reset" $headers | Out-Null

$seed2 = Invoke-Step "SeedLifecycle(Hold)" "POST" "$BaseUrl/api/v1/actions/seed/lifecycle" $headers
$caseId2 = Get-StringValue $seed2 @("caseId","CaseId")

Invoke-Step "SubmitCase(Hold)" "POST" "$BaseUrl/api/v1/actions/cases/$caseId2/submit" $headers | Out-Null
Invoke-Step "HoldCase" "POST" "$BaseUrl/api/v1/actions/cases/$caseId2/hold" $headers | Out-Null

$audit2 = Get-AuditJson -BaseUrl $BaseUrl -Headers $headers | ConvertTo-Json -Depth 20
$snapshot2 = Get-SnapshotJson -BaseUrl $BaseUrl -Headers $headers

Assert-Contains "Audit contains SubmitCase(Hold)" $audit2 "SubmitCase"
Assert-Contains "Audit contains HoldCase" $audit2 "HoldCase"

$caseStatus2 = Get-StringValue $snapshot2 @("caseStatus","CaseStatus")
if ($caseStatus2 -eq "OnHold") {
    Add-Line "Snapshot after hold - PASS - caseStatus=OnHold"
}
else {
    Add-Line "Snapshot after hold - FAIL - caseStatus=$caseStatus2"
}

# Scenario 3: Contradiction resolution path
Add-Line ""
Add-Line "=== Scenario 3: Contradiction path ==="
Invoke-Step "ClearWorkflowAudit(Contradiction)" "POST" "$BaseUrl/api/v1/workflow-audit/clear" $headers | Out-Null
Invoke-Step "ResetPersistence(Contradiction)" "POST" "$BaseUrl/api/v1/persistence/reset" $headers | Out-Null

$seed3 = Invoke-Step "SeedLifecycle(Contradiction)" "POST" "$BaseUrl/api/v1/actions/seed/lifecycle" $headers
$contradictionId3 = Get-StringValue $seed3 @("contradictionId","ContradictionId")

Invoke-Step "ResolveContradiction" "POST" "$BaseUrl/api/v1/actions/contradictions/$contradictionId3/resolve" $headers | Out-Null

$audit3 = Get-AuditJson -BaseUrl $BaseUrl -Headers $headers | ConvertTo-Json -Depth 20
$snapshot3 = Get-SnapshotJson -BaseUrl $BaseUrl -Headers $headers

Assert-Contains "Audit contains ResolveContradiction" $audit3 "ResolveContradiction"

$contradictionStatus3 = Get-StringValue $snapshot3 @("contradictionStatus","ContradictionStatus")
if ($contradictionStatus3 -eq "Resolved") {
    Add-Line "Snapshot after contradiction - PASS - contradictionStatus=Resolved"
}
else {
    Add-Line "Snapshot after contradiction - FAIL - contradictionStatus=$contradictionStatus3"
}

Add-Line ""
Add-Line "=== DONE ==="
