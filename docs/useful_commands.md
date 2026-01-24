# Вызвать метод для получения кол-ва задач на классификацию в очереди

```bash
kubectl exec -it <имя-пода> -- /bin/sh
apt-get update && apt-get install -y curl
curl http://localhost:8080/api/admin/classification-queue-size
```


