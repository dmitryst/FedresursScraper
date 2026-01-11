# Настройка доступа к MinIO (Kubernetes) для локальной разработки

Это руководство описывает, как настроить локальное окружение для корректного отображения изображений лотов, ссылки на которые в базе данных указывают на внутренний адрес кластера: `http://minio.storage.svc.cluster.local:9000`.

## Проблема

1.  В базе данных (dump с продакшена) URL картинок хранятся в формате:
    `http://minio.storage.svc.cluster.local:9000/bucket-name/image.jpg`
2.  При локальном запуске фронтенда браузер пытается загрузить эти URL.
3.  Локальный компьютер не может разрешить DNS-имя `minio.storage.svc.cluster.local` и не имеет прямого доступа к сети кластера.

## Решение

Мы используем комбинацию **kubectl port-forward** и подмены DNS через файл **hosts**.

### Шаг 1. Проброс порта (Port-Forwarding)

Эта команда создает туннель с вашего `localhost:9000` напрямую к сервису MinIO в Kubernetes.

Запустите в терминале:

```bash
# Формат: kubectl port-forward svc/<service-name> <local-port>:<remote-port> -n <namespace>
kubectl port-forward svc/minio 9000:9000 -n storage
```

### Шаг 2. Настройка DNS (hosts)
Нам нужно "обмануть" компьютер, заставив его считать, что домен minio.storage.svc.cluster.local — это наш локальный хост (127.0.0.1).

Для Windows
1. Откройте Блокнот (Notepad) от имени администратора.

2. Откройте файл: C:\Windows\System32\drivers\etc\hosts

3. Добавьте в конец файла строку:

```text
127.0.0.1 minio.storage.svc.cluster.local
```

4. Сохраните файл.