# Вызвать метод для получения кол-ва задач на классификацию в очереди

```bash
kubectl exec -it <имя-пода> -- /bin/sh
apt-get update && apt-get install -y curl
curl http://localhost:8080/api/admin/classification-queue-size
```

# Сброс IsEnriched в false для повторного автоматического дообогащения

```bash
# Сбросить конкретные торги МЭТС
curl -X POST "http://localhost:8080/api/admin/reset-mets-enrichment?tradeNumber=192052-%D0%9C%D0%AD%D0%A2%D0%A1-1"

# Сбросить все торги МЭТС без фото
curl -X POST "http://localhost:8080/api/admin/reset-mets-enrichment"

# Сбросить торги МЭТС по дате создания (от и до включительно)
curl -X POST "http://localhost:8080/api/admin/reset-mets-enrichment?fromDate=2026-01-23&toDate=2026-01-23"
```

# Проверить метод получения координат сервисом rosreestr-service (из другого пода)

```bash
kubectl exec -it <имя-пода> -- /bin/sh
apt-get update && apt-get install -y curl
curl http://rosreestr-service/coordinates/40:08:020401:94
```