param(
    [string]$BaseRef = $null
)

# Determine base ref
if (-not $BaseRef) {
    if ($env:GITHUB_BASE_REF) {
        $BaseRef = $env:GITHUB_BASE_REF
    } elseif ($env:GITHUB_EVENT_PATH) {
        # For PR, parse the event to get base.sha
        $event = Get-Content $env:GITHUB_EVENT_PATH | ConvertFrom-Json
        if ($event.pull_request) {
            $BaseRef = $event.pull_request.base.sha
        }
    }
    if (-not $BaseRef) {
        $BaseRef = "HEAD~1"
    }
}

# Check if contracts have changed and require updates to docs and goldens

$contractsChanged = $false
$docsChanged = $false
$goldensChanged = $false

# Get changed files since base
$changedFiles = git diff --name-only $BaseRef

foreach ($file in $changedFiles) {
    if ($file -like "contracts/src/Contracts/*" -or $file -like "contracts/Contracts.csproj") {
        $contractsChanged = $true
    }
    if ($file -eq "CONTRACTS.md" -or $file -eq "ARCHITECTURE.md") {
        $docsChanged = $true
    }
    if ($file -like "contracts.Tests/Fixtures/*.json") {
        $goldensChanged = $true
    }
}

if ($contractsChanged) {
    if (-not $docsChanged) {
        Write-Host "ERROR: Contracts have been modified but CONTRACTS.md or ARCHITECTURE.md has not been updated."
        Write-Host "Please update the documentation with date and reason for the changes."
        exit 1
    }
    if (-not $goldensChanged) {
        Write-Host "ERROR: Contracts have been modified but golden test fixtures have not been updated."
        Write-Host "Please regenerate the JSON fixtures in contracts.Tests/Fixtures/ to match the new contracts."
        exit 1
    }
    Write-Host "Contracts changed, docs and goldens updated - proceeding."
} else {
    Write-Host "No contract changes detected."
}

exit 0