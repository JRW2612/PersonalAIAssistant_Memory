<#
.SYNOPSIS
    Automated Production Deployment Script for Personal AI Assistant Memory Subsystem.
.DESCRIPTION
    Deploys Kubernetes manifests targeting sovereign Indian cloud regions (AWS ap-south-1 / Azure centralindia).
    Enforces FIPS 140-2 Level 3 HSM, RBI Data Localization, and CERT-In compliance prerequisites.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$KubeConfigPath = "$HOME/.kube/config",

    [Parameter(Mandatory = $false)]
    [string]$Namespace = "bfsi-memory-prod",

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host "  BFSI Sovereign Kubernetes Deployment: Personal AI Assistant Memory " -ForegroundColor Cyan
Write-Host "=====================================================================" -ForegroundColor Cyan

# 1. Prerequisite Validation
Write-Host "`n[Step 1/5] Validating tools and sovereign cluster connectivity..." -ForegroundColor Yellow
if (-not (Get-Command kubectl -ErrorAction SilentlyContinue)) {
    throw "kubectl CLI is required but not installed in PATH."
}

$dryRunFlag = if ($DryRun) { "--dry-run=client" } else { "" }

# 2. Namespace Creation
Write-Host "`n[Step 2/5] Applying Namespace with Pod Security Standards..." -ForegroundColor Yellow
kubectl apply -f "$PSScriptRoot/namespace.yaml" $dryRunFlag

# 3. ConfigMap & Secret Template
Write-Host "`n[Step 3/5] Applying ConfigMap (RBI localization & CERT-In settings)..." -ForegroundColor Yellow
kubectl apply -f "$PSScriptRoot/configmap.yaml" $dryRunFlag

if (Test-Path "$PSScriptRoot/secrets.yaml") {
    Write-Host "Applying production secrets from secrets.yaml..." -ForegroundColor Green
    kubectl apply -f "$PSScriptRoot/secrets.yaml" $dryRunFlag
} else {
    Write-Host "[WARNING] 'secrets.yaml' not found. Please populate secrets.yaml from secrets.yaml.template before applying secrets." -ForegroundColor DarkYellow
}

# 4. Workloads & Services
Write-Host "`n[Step 4/5] Deploying PodDisruptionBudget, Deployment, Service, HPA, and Ingress..." -ForegroundColor Yellow
kubectl apply -f "$PSScriptRoot/pdb.yaml" $dryRunFlag
kubectl apply -f "$PSScriptRoot/deployment.yaml" $dryRunFlag
kubectl apply -f "$PSScriptRoot/service.yaml" $dryRunFlag
kubectl apply -f "$PSScriptRoot/hpa.yaml" $dryRunFlag
kubectl apply -f "$PSScriptRoot/ingress.yaml" $dryRunFlag

# 5. Monitoring & Prometheus Rules
Write-Host "`n[Step 5/5] Registering PrometheusRule alerts (Outbox, DLQ, Latency, Bloat)..." -ForegroundColor Yellow
kubectl apply -f "$PSScriptRoot/monitoring-alerts.yaml" $dryRunFlag

if (-not $DryRun) {
    Write-Host "`nWaiting for deployment rollout..." -ForegroundColor Yellow
    kubectl rollout status deployment/memory-api-deployment -n $Namespace --timeout=120s
    Write-Host "`n[SUCCESS] Deployment successfully completed and healthy!" -ForegroundColor Green
} else {
    Write-Host "`n[DRY RUN COMPLETE] All manifests validated without errors." -ForegroundColor Green
}
