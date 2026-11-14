param(
    [string]$Repository = 'Liis17/BarkFluff'
)

# Applies every ruleset from .github/rulesets: nightly accepts any PR that passes "Tests passed";
# dev and master accept only promotion PRs (the source branch is checked by the same check).
$ErrorActionPreference = 'Stop'
$rulesetFiles = Get-ChildItem (Join-Path $PSScriptRoot '../rulesets') -Filter '*.json'
$configs = @($rulesetFiles | ForEach-Object { Get-Content $_.FullName -Raw | ConvertFrom-Json })

# The required check must already exist in every protected branch, otherwise PRs block forever.
$checkSources = @('.github/workflows/ci.yml', '.github/workflows/pr-tests.yml')
foreach ($ref in ($configs | ForEach-Object { $_.conditions.ref_name.include } | Sort-Object -Unique)) {
    $branch = $ref -replace '^refs/heads/', ''
    $found = $false
    foreach ($path in $checkSources) {
        gh api "repos/$Repository/contents/${path}?ref=$branch" --silent 2>$null
        if ($LASTEXITCODE -eq 0) { $found = $true; break }
    }
    $global:LASTEXITCODE = 0
    if (-not $found) {
        throw "No workflow producing 'Tests passed' in $branch. No rules were changed."
    }
}

$rulesets = gh api "repos/$Repository/rulesets" --paginate --slurp | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Cannot read repository rulesets.' }
$rulesets = @($rulesets | ForEach-Object { $_ })

foreach ($file in $rulesetFiles) {
    $name = (Get-Content $file.FullName -Raw | ConvertFrom-Json).name
    $existing = @($rulesets | Where-Object { $_.name -eq $name })
    if ($existing.Count -gt 1) { throw "More than one '$name' ruleset exists." }
    if ($existing.Count -eq 1) {
        gh api --method PUT "repos/$Repository/rulesets/$($existing[0].id)" --input $file.FullName --silent
    } else {
        gh api --method POST "repos/$Repository/rulesets" --input $file.FullName --silent
    }
    if ($LASTEXITCODE -ne 0) { throw "Cannot apply ruleset '$name'." }
}

foreach ($branch in @('nightly', 'dev', 'master')) {
    gh api "repos/$Repository/rules/branches/$branch" --jq '[.[] | .type] | join(",")'
    if ($LASTEXITCODE -ne 0) { throw "Cannot verify rules for $branch." }
}
