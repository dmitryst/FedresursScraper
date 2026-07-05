## Выкатка парсера с помощью скрипта (в cloud k8s)

1. в Git Bash и перейти в директорию parser
```bash
cd /b/Projects/parser
```
2. запустить скрипт сборки образа:
```bash
./build-parser.sh 1.7.22
```
3. перейти в директорию k8s
```bash
cd /b/Projects/k8s
```
4. применить конфиги, если в них были изменения:
```bash
kubectl apply -f parser/configmap.yaml
```
5. установить нужную версию в deployment и применить его:
```bash
kubectl apply -f parser/deployment.yaml
```

## Выкатка WebApi с помощью скрипта (в cloud k8s)

1. В Git Bash и перейти в директорию parser
```bash
cd /b/Projects/parser
```
2. Запустить скрипт сборки образа:
```bash
./build-webapi.sh 1.0.25
```
3. Перейти в директорию k8s
```bash
cd /b/Projects/k8s
```
4. Применить конфиги, если в них были изменения:
```bash
kubectl apply -f web-api/configmap.yaml
```
5. Установить нужную версию в deployment и применить его:
```bash
kubectl apply -f web-api/deployment.yaml
```
6. Проверить логи сервиса:
```bash
kubectl logs -f deployment/web-api-deployment --timestamps
```