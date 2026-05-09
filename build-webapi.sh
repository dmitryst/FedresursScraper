#!/bin/bash

IMAGE_NAME="dmitryst/s-lot-web-api"
VERSION=$1

if [ -z "$VERSION" ]; then
  echo "❌ Error: Version argument is required."
  exit 1
fi

echo "🏗️ Building WebApi v$VERSION..."

# Build & Tag using the specific Dockerfile
docker build -f Dockerfile.WebApi --build-arg BUILD_VERSION="$VERSION" -t $IMAGE_NAME:"$VERSION" .
if [ $? -ne 0 ]; then
    echo "❌ Build failed!"
    exit 1
fi
docker tag $IMAGE_NAME:"$VERSION" $IMAGE_NAME:latest

# Push
echo "🚀 Pushing images..."
docker push $IMAGE_NAME:"$VERSION"
docker push $IMAGE_NAME:latest

echo "✅ Done! WebApi v$VERSION image is pushed."