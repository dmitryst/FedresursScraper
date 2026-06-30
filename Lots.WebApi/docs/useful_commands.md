# Устанавливаем curl + вызываем метод сброса флагов нормализации для повторного прогона worker'ом

```bash
kubectl exec deployment/web-api-deployment -- bash -c 'apt-get update -qq && apt-get install -y -qq curl && curl -s -X POST http://localhost:8080/api/admin/vehicle-reset-normalization -H "X-Admin-Api-Key: $AdminSettings__ApiKey"'
```

Перезапуск пода:

```bash
kubectl rollout restart deployment/web-api-deployment
```

Дожидаемся готовности:

```bash
kubectl rollout status deployment/web-api-deployment
```

kubectl exec deployment/web-api-deployment -- bash -c 'apt-get update -qq && apt-get install -y -qq curl && curl -s -X POST http://localhost:8080/api/admin/vehicle-reset-unmatched-extraction -H "X-Admin-Api-Key: $AdminSettings__ApiKey"'
