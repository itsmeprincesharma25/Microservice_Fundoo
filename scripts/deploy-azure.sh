#!/bin/bash
# ============================================================
# Fundoo Microservices – Full Rebuild + Redeploy Script
# Run on Azure VM as: bash scripts/deploy-azure.sh
# ============================================================
set -e

DOCKER_USER="princesharma25"
REPO_DIR="$HOME/Microservice_Fundoo"
TAG="live"

echo "======================================================"
echo "  Step 1: Pull latest code from GitHub"
echo "======================================================"
cd "$REPO_DIR"
git pull origin main

echo ""
echo "======================================================"
echo "  Step 2: Build Docker images"
echo "======================================================"

# User Service
docker build \
  --build-arg PROJECT_PATH="Fundoo.User.Api/Fundoo.User.Api.csproj" \
  -t "$DOCKER_USER/fundoo-user:$TAG" \
  -f Dockerfile .
echo "✅ fundoo-user:live built"

# Notes Service
docker build \
  --build-arg PROJECT_PATH="Fundoo.Notes.Api/Fundoo.Notes.Api.csproj" \
  -t "$DOCKER_USER/fundoo-notes:$TAG" \
  -f Dockerfile .
echo "✅ fundoo-notes:live built"

# Notification Service
docker build \
  --build-arg PROJECT_PATH="Fundoo.Notification.Api/Fundoo.Notification.Api.csproj" \
  -t "$DOCKER_USER/fundoo-notification:$TAG" \
  -f Dockerfile .
echo "✅ fundoo-notification:live built"

# Gateway
docker build \
  --build-arg PROJECT_PATH="WebApplication1/WebApplication1.csproj" \
  -t "$DOCKER_USER/fundoo-gateway:$TAG" \
  -f Dockerfile .
echo "✅ fundoo-gateway:live built"

echo ""
echo "======================================================"
echo "  Step 3: Push images to Docker Hub"
echo "======================================================"
docker push "$DOCKER_USER/fundoo-user:$TAG"
docker push "$DOCKER_USER/fundoo-notes:$TAG"
docker push "$DOCKER_USER/fundoo-notification:$TAG"
docker push "$DOCKER_USER/fundoo-gateway:$TAG"
echo "✅ All images pushed to Docker Hub"

echo ""
echo "======================================================"
echo "  Step 4: Apply K8s manifests (idempotent)"
echo "======================================================"
kubectl apply -f k8s/

echo ""
echo "======================================================"
echo "  Step 5: Rollout restart all deployments"
echo "======================================================"
kubectl rollout restart deployment/user-deploy \
                             deployment/notes-deploy \
                             deployment/notification-deploy \
                             deployment/gateway-deploy

echo ""
echo "======================================================"
echo "  Step 6: Wait for rollouts to complete"
echo "======================================================"
kubectl rollout status deployment/user-deploy
kubectl rollout status deployment/notes-deploy
kubectl rollout status deployment/notification-deploy
kubectl rollout status deployment/gateway-deploy

echo ""
echo "======================================================"
echo "  Step 7: Show pod status"
echo "======================================================"
kubectl get pods -o wide

echo ""
echo "======================================================"
echo "  Step 8: Quick smoke test"
echo "======================================================"
sleep 3

echo "--- GET /health ---"
curl -sf http://localhost:30080/health && echo ""

echo "--- POST /api/auth/register ---"
curl -si -X POST http://localhost:30080/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"fullName":"Deploy Test","email":"deploytest@fundoo.local","password":"Password@123"}' | head -5

echo ""
echo "✅ Deploy complete! Run the full test suite with: bash scripts/test-endpoints.sh"
