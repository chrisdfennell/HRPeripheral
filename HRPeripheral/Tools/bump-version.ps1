param(
  [switch]$Patch,      # default
  [switch]$Minor,
  [switch]$Major,
  [switch]$NoBump,     # only read the version, don't change it
  [string]$Path = "$PSScriptRoot\version.json"
)

$ErrorActionPreference = "Stop"

if (!(Test-Path $Path)) {
  throw "version.json not found at $Path"
}

$json = Get-Content $Path -Raw | ConvertFrom-Json

if (-not $NoBump) {
  if ($Major) {
    $json.major++
    $json.minor = 0; $json.patch = 0; $json.build = 0
  } elseif ($Minor) {
    $json.minor++
    $json.patch = 0; $json.build = 0
  } else {
    $json.patch++
  }
  $json.build++
  ($json | ConvertTo-Json -Depth 5) | Set-Content -Path $Path -Encoding UTF8
}

# Compose values:
# - ApplicationDisplayVersion: major.minor.patch[-rc]
# - ApplicationVersion (int):  major*1_000_000 + minor*10_000 + patch*100 + build%100
$display = "{0}.{1}.{2}" -f $json.major, $json.minor, $json.patch
$code = [int]($json.major*1000000 + $json.minor*10000 + $json.patch*100 + ($json.build % 100))

[pscustomobject]@{
  Display = $display
  Code    = $code
}