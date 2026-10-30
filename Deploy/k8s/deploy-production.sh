#!/usr/bin/env bash
# ==============================================================================
# Automated Production Deployment Script with Strategy Selection (Bash)
# Supports Blue-Green, Canary, and RollingUpdate strategies for BFSI Kubernetes.
# ==============================================================================

set -euo pipefail

NAMESPACE="${NAMESPACE:-bfsi-memory-prod}"
DRY_RUN="${DRY_RUN:-false}"
STRATEGY="${1:-}"
BLUE_GREEN_TARGET="${BLUE_GREEN_TARGET:-green}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "====================================================================="
echo "  BFSI Sovereign Kubernetes Deployment: Personal AI Assistant Memory "
echo "====================================================================="

# Interactive strategy prompt if not specified as argument
if [ -z "$STRATEGY" ]; then
    echo ""
    echo "Please select the deployment strategy:"
    echo "  [1] Canary        - Progressive traffic shifting (10% -> 25% -> 50% -> 100%)"
    echo "  [2] Blue-Green    - Zero-downtime atomic cutover with instant rollback"
    echo "  [3] RollingUpdate - Standard Kubernetes rolling update (maxSurge=1, maxUnavailable=0)"
    echo ""
    read -rp "Enter selection (1, 2, or 3) [Default: 1 (Canary)]: " CHOICE
    case "${CHOICE:-1}" in
        2) STRATEGY="BlueGreen" ;;
        3) STRATEGY="RollingUpdate" ;;
        *) STRATEGY="Canary" ;;
    esac
fi

echo -e "\n>>> Selected Deployment Strategy: ${STRATEGY}"

DRY_RUN_FLAG=""
if [ "$DRY_RUN" = "true" ]; then
    DRY_RUN_FLAG="--dry-run=client --validate=false"
fi

# 1. Namespace
echo -e "\n[Step 1/5] Applying Namespace..."
kubectl apply -f "${SCRIPT_DIR}/namespace.yaml" ${DRY_RUN_FLAG}

# 2. ConfigMap & Secrets
echo -e "\n[Step 2/5] Applying ConfigMap & Secrets..."
kubectl apply -f "${SCRIPT_DIR}/configmap.yaml" ${DRY_RUN_FLAG}
if [ -f "${SCRIPT_DIR}/secrets.yaml" ]; then
    kubectl apply -f "${SCRIPT_DIR}/secrets.yaml" ${DRY_RUN_FLAG}
fi

# 3. Baseline Infrastructure (PDB & Alerts)
echo -e "\n[Step 3/5] Applying PDB & Monitoring alerts..."
kubectl apply -f "${SCRIPT_DIR}/pdb.yaml" ${DRY_RUN_FLAG}
kubectl apply -f "${SCRIPT_DIR}/monitoring-alerts.yaml" ${DRY_RUN_FLAG}

# 4. Strategy Execution
echo -e "\n[Step 4/5] Executing ${STRATEGY} deployment pipeline..."
case "$STRATEGY" in
    Canary)
        kubectl apply -f "${SCRIPT_DIR}/service.yaml" ${DRY_RUN_FLAG}
        kubectl apply -f "${SCRIPT_DIR}/ingress.yaml" ${DRY_RUN_FLAG}
        kubectl apply -f "${SCRIPT_DIR}/deployment.yaml" ${DRY_RUN_FLAG}
        kubectl apply -f "${SCRIPT_DIR}/hpa.yaml" ${DRY_RUN_FLAG}
        kubectl apply -f "${SCRIPT_DIR}/canary/deployment-canary.yaml" ${DRY_RUN_FLAG}
        kubectl apply -f "${SCRIPT_DIR}/canary/ingress-canary.yaml" ${DRY_RUN_FLAG}
        echo "[Canary] Deployed canary with 10% traffic weighting."
        ;;
    BlueGreen)
        kubectl apply -f "${SCRIPT_DIR}/blue-green/services.yaml" ${DRY_RUN_FLAG}
        kubectl apply -f "${SCRIPT_DIR}/ingress.yaml" ${DRY_RUN_FLAG}
        kubectl apply -f "${SCRIPT_DIR}/blue-green/deployment-${BLUE_GREEN_TARGET}.yaml" ${DRY_RUN_FLAG}
        if [ "$DRY_RUN" != "true" ]; then
            echo "[Blue-Green] Waiting for ${BLUE_GREEN_TARGET} to become ready..."
            kubectl rollout status "deployment/memory-api-${BLUE_GREEN_TARGET}" -n "${NAMESPACE}" --timeout=180s
            echo "[Blue-Green] Switching active service traffic to ${BLUE_GREEN_TARGET}..."
            kubectl patch service memory-api-service -n "${NAMESPACE}" --type merge -p "{\"spec\":{\"selector\":{\"version\":\"${BLUE_GREEN_TARGET}\"}}}"
        fi
        ;;
    RollingUpdate)
        kubectl apply -f "${SCRIPT_DIR}/deployment.yaml" ${DRY_RUN_FLAG}
        kubectl apply -f "${SCRIPT_DIR}/service.yaml" ${DRY_RUN_FLAG}
        kubectl apply -f "${SCRIPT_DIR}/hpa.yaml" ${DRY_RUN_FLAG}
        kubectl apply -f "${SCRIPT_DIR}/ingress.yaml" ${DRY_RUN_FLAG}
        if [ "$DRY_RUN" != "true" ]; then
            kubectl rollout status deployment/memory-api-deployment -n "${NAMESPACE}" --timeout=120s
        fi
        ;;
    *)
        echo "Unknown strategy: ${STRATEGY}" >&2
        exit 1
        ;;
esac

echo -e "\n[Step 5/5] Deployment completed successfully under ${STRATEGY} strategy!"
