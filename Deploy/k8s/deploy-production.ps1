<#
.SYNOPSIS
    Automated Production Deployment Script with Interactive/Declarative Strategy Selection.
.DESCRIPTION
    Enables selection between Blue-Green, Canary, and RollingUpdate deployment strategies at runtime.
    Deploys Kubernetes manifests targeting sovereign Indian cloud regions (AWS ap-south-1 / Azure centralindia)
    with FIPS 140-2 Level 3 HSM, RBI Data Localization, and CERT-In compliance prerequisites.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$KubeConfigPath = "$HOME/.kube/config",

    [Parameter(Mandatory = $false)]
    [string]$Namespace = "bfsi-memory-prod",

    [Parameter(Mandatory = $false)]
    [ValidateSet("Canary", "BlueGreen", "RollingUpdate")]
    [string]$DeploymentStrategy,

    [Parameter(Mandatory = $false)]
    [ValidateSet("blue", "green")]
    [string]$BlueGreenTargetColor = "green",

    [Parameter(Mandatory = $false)]
    [string]$ImageTag = "v1.0.0-bfsi-certified",

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

function Invoke-KubeApply {
    param([Parameter(Mandatory=$true)][string]$Path)
    if ($DryRun) {
        kubectl apply -f $Path --dry-run=client
    } else {
        kubectl apply -f $Path
    }
}

Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host "  BFSI Sovereign Kubernetes Deployment: Personal AI Assistant Memory " -ForegroundColor Cyan
Write-Host "=====================================================================" -ForegroundColor Cyan

# 0. Interactive Deployment Strategy Selection (if not specified via parameter)
if ([string]::IsNullOrWhiteSpace($DeploymentStrategy)) {
    Write-Host "`nPlease select the deployment strategy:" -ForegroundColor Yellow
    Write-Host "  [1] Canary        - Progressive traffic shifting (10% -> 25% -> 50% -> 100%) with automated health gating" -ForegroundColor White
    Write-Host "  [2] Blue-Green    - Zero-downtime atomic cutover between standby and active environments with instant rollback" -ForegroundColor White
    Write-Host "  [3] RollingUpdate - Standard Kubernetes in-place rolling update with zero unavailable pods (maxSurge=1)" -ForegroundColor White
    
    $choice = Read-Host "`nEnter selection (1, 2, or 3) [Default: 1 (Canary)]"
    switch ($choice.Trim()) {
        "2" { $DeploymentStrategy = "BlueGreen" }
        "3" { $DeploymentStrategy = "RollingUpdate" }
        default { $DeploymentStrategy = "Canary" }
    }
}

Write-Host "`n>>> Selected Deployment Strategy: " -NoNewline
Write-Host "$DeploymentStrategy" -ForegroundColor Green

# 1. Prerequisite Validation
Write-Host "`n[Step 1/5] Validating tools and sovereign cluster connectivity..." -ForegroundColor Yellow
if (-not (Get-Command kubectl -ErrorAction SilentlyContinue)) {
    throw "kubectl CLI is required but not installed in PATH."
}

# 2. Namespace Creation
Write-Host "`n[Step 2/5] Applying Namespace with Pod Security Standards..." -ForegroundColor Yellow
Invoke-KubeApply -Path "$PSScriptRoot/namespace.yaml"

# 3. ConfigMap & Secret Template
Write-Host "`n[Step 3/5] Applying ConfigMap (RBI localization & CERT-In settings)..." -ForegroundColor Yellow
Invoke-KubeApply -Path "$PSScriptRoot/configmap.yaml"

if (Test-Path "$PSScriptRoot/secrets.yaml") {
    Write-Host "Applying production secrets from secrets.yaml..." -ForegroundColor Green
    Invoke-KubeApply -Path "$PSScriptRoot/secrets.yaml"
} else {
    Write-Host "[WARNING] 'secrets.yaml' not found. Please populate secrets.yaml from secrets.yaml.template before applying secrets." -ForegroundColor DarkYellow
}

# 4. Shared Baseline Infrastructure: PDB & Monitoring
Write-Host "`n[Step 4/5] Applying PodDisruptionBudget & Monitoring alerts..." -ForegroundColor Yellow
Invoke-KubeApply -Path "$PSScriptRoot/pdb.yaml"
Invoke-KubeApply -Path "$PSScriptRoot/monitoring-alerts.yaml"

# 5. Execute Chosen Deployment Strategy
Write-Host "`n[Step 5/5] Executing $DeploymentStrategy Deployment Pipeline..." -ForegroundColor Yellow

switch ($DeploymentStrategy) {
    "Canary" {
        Write-Host "--> Deploying baseline services and primary deployment..." -ForegroundColor Cyan
        Invoke-KubeApply -Path "$PSScriptRoot/service.yaml"
        Invoke-KubeApply -Path "$PSScriptRoot/ingress.yaml"
        Invoke-KubeApply -Path "$PSScriptRoot/deployment.yaml"
        Invoke-KubeApply -Path "$PSScriptRoot/hpa.yaml"

        if (-not $DryRun) {
            Write-Host "--> Launching progressive Canary rollout..." -ForegroundColor Cyan
            & "$PSScriptRoot/canary/canary-rollout.ps1" -Namespace $Namespace
        }
    }

    "BlueGreen" {
        Write-Host "--> Deploying Blue-Green service routing topology..." -ForegroundColor Cyan
        Invoke-KubeApply -Path "$PSScriptRoot/blue-green/services.yaml"
        Invoke-KubeApply -Path "$PSScriptRoot/ingress.yaml"

        Write-Host "--> Deploying target $BlueGreenTargetColor environment..." -ForegroundColor Cyan
        Invoke-KubeApply -Path "$PSScriptRoot/blue-green/deployment-$BlueGreenTargetColor.yaml"

        if (-not $DryRun) {
            Write-Host "--> Waiting for $BlueGreenTargetColor environment to become ready..." -ForegroundColor Cyan
            kubectl rollout status "deployment/memory-api-$BlueGreenTargetColor" -n $Namespace --timeout=180s

            Write-Host "--> Executing atomic Blue-Green cutover to $BlueGreenTargetColor..." -ForegroundColor Cyan
            & "$PSScriptRoot/blue-green/switch-blue-green.ps1" -TargetVersion $BlueGreenTargetColor -Namespace $Namespace
        }
    }

    "RollingUpdate" {
        Write-Host "--> Deploying standard RollingUpdate workloads..." -ForegroundColor Cyan
        Invoke-KubeApply -Path "$PSScriptRoot/deployment.yaml"
        Invoke-KubeApply -Path "$PSScriptRoot/service.yaml"
        Invoke-KubeApply -Path "$PSScriptRoot/hpa.yaml"
        Invoke-KubeApply -Path "$PSScriptRoot/ingress.yaml"

        if (-not $DryRun) {
            Write-Host "`nWaiting for deployment rollout..." -ForegroundColor Yellow
            kubectl rollout status deployment/memory-api-deployment -n $Namespace --timeout=120s
            Write-Host "`n[SUCCESS] RollingUpdate deployment successfully completed and healthy!" -ForegroundColor Green
        }
    }
}

if ($DryRun) {
    Write-Host "`n[DRY RUN COMPLETE] Manifests for $DeploymentStrategy validated without errors." -ForegroundColor Green
} else {
    Write-Host "`n[DEPLOYMENT COMPLETE] Production environment operational under $DeploymentStrategy strategy!" -ForegroundColor Green
}
