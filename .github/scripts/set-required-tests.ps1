param(
    [string]$Repository = 'Liis17/BarkFluff'
)

$ErrorActionPreference = 'Stop'
$rulesetPath = Join-Path $PSScriptRoot '../rulesets/required-tests.json'
$config = Get-Content $rulesetPath -Raw | ConvertFrom-Json

# Install workflows in every target branch before requiring their check.
foreach ($ref in $config.conditions.ref_name.include) {
    $branch = $ref -replace '^refs/heads/', ''
    foreach ($workflow in @('pr-tests.yml', 'test-backend.yml', 'test-clients.yml', 'test-scripts.yml')) {
        gh api "repos/$Repository/contents/.github/workflows/${workflow}?ref=$branch" --silent
        if ($LASTEXITCODE -ne 0) {
            throw "Install $workflow in $branch before enabling required tests. No rules were changed."
        }
    }
}

$rulesets = gh api "repos/$Repository/rulesets" --paginate --slurp | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Cannot read repository rulesets.' }
$existing = @($rulesets | ForEach-Object { $_ } | Where-Object { $_.name -eq $config.name })
if ($existing.Count -gt 1) { throw 'More than one Required PR tests ruleset exists.' }

if ($existing.Count -eq 1) {
    gh api --method PUT "repos/$Repository/rulesets/$($existing[0].id)" --input $rulesetPath --silent
} else {
    gh api --method POST "repos/$Repository/rulesets" --input $rulesetPath --silent
}
if ($LASTEXITCODE -ne 0) { throw 'Cannot apply required tests ruleset.' }

foreach ($branch in @('nightly', 'dev', 'master')) {
    gh api "repos/$Repository/rules/branches/$branch" --jq '.[] | select(.type == "required_status_checks") | .parameters'
    if ($LASTEXITCODE -ne 0) { throw "Cannot verify rules for $branch." }
}
