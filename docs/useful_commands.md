# Вызвать метод для получения кол-ва задач на классификацию в очереди

```bash
kubectl exec -it <имя-пода> -- /bin/sh
apt-get update && apt-get install -y curl
curl http://localhost:8080/api/admin/classification-queue-size
```

# Сброс IsEnriched в false для повторного автоматического дообогащения

```bash
# Сбросить конкретные торги
POST /api/admin/reset-mets-enrichment?tradeNumber=190006-МЭТС-1

# Сбросить все торги МЭТС без фото
POST /api/admin/reset-mets-enrichment
```

