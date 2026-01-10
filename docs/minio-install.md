helm install minio minio/minio --namespace storage --create-namespace --set replicas=1 --set rootUser=admin --set rootPassword=admin1234 --set persistence.enabled=true --set persistence.size=3Gi --set resources.requests.memory=256Mi --set resources.limits.memory=1Gi --set buckets=null --set users=null --set policies=null

helm upgrade minio minio/minio -n storage --set mode=standalone --set replicas=1 --set persistence.enabled=true --reuse-values

NAME: minio
LAST DEPLOYED: Sat Jan 10 19:31:17 2026
NAMESPACE: storage
STATUS: deployed
REVISION: 1
DESCRIPTION: Install complete
TEST SUITE: None
NOTES:
MinIO can be accessed via port 9000 on the following DNS name from within your cluster:
minio.storage.cluster.local

To access MinIO from localhost, run the below commands:

  1. export POD_NAME=$(kubectl get pods --namespace storage -l "release=minio" -o jsonpath="{.items[0].metadata.name}")

  2. kubectl port-forward $POD_NAME 9000 --namespace storage

Read more about port forwarding here: http://kubernetes.io/docs/user-guide/kubectl/kubectl_port-forward/

You can now access MinIO server on http://localhost:9000. Follow the below steps to connect to MinIO server with mc client:

  1. Download the MinIO mc client - https://min.io/docs/minio/linux/reference/minio-mc.html#quickstart

  2. export MC_HOST_minio-local=http://$(kubectl get secret --namespace storage minio -o jsonpath="{.data.rootUser}" | base64 --decode):$(kubectl get secret --namespace storage minio -o jsonpath="{.data.rootPassword}" | base64 --decode)@localhost:9000

  3. mc ls minio-local