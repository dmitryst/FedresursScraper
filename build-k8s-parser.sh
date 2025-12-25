#!/bin/bash

IMAGE_NAME="dmitryst/s-lot-parser"
DEPLOYMENT_NAME="parser-deployment"
VERSION=$1

if [ -z "$VERSION" ]; then
  echo "❌ Error: Version argument is required."
  exit 1
fi

echo "🏗️ Building Parser v$VERSION..."

# Build & Tag
docker build --build-arg BUILD_VERSION="$VERSION" -t $IMAGE_NAME:"$VERSION" .
if [ $? -ne 0 ]; then
    echo "❌ Build failed!"
    exit 1
fi
docker tag $IMAGE_NAME:"$VERSION" $IMAGE_NAME:latest

# Push
echo "🚀 Pushing images..."
docker push $IMAGE_NAME:"$VERSION"
docker push $IMAGE_NAME:latest

# Deploy
echo "🔄 Rolling out restart for $DEPLOYMENT_NAME..."
kubectl rollout restart deployment/$DEPLOYMENT_NAME

echo "✅ Done! v$VERSION deployed."
