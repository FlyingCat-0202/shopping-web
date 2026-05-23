param(
    [string]$HostAddress = "0.0.0.0",
    [int]$Port = 4200
)

$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    throw "npm was not found. Install Node.js LTS, then restart AppHost."
}

if (-not (Test-Path "node_modules")) {
    Write-Host "node_modules not found. Installing frontend dependencies with npm ci..."

    if (Test-Path "package-lock.json") {
        & npm ci
    }
    else {
        & npm install
    }
}

& npm run start -- --host $HostAddress --port $Port
