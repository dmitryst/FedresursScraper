## Выкатка парсера с помощью скрипта (в cloud k8s)

1. в Git Bash и перейти в директорию parser
```bash
cd /b/Projects/parser
```
2. запустить скрипт сборки образа:
```bash
./build-parser.sh 1.7.7
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

1. запустить Git Bash и перейти в папку командой cd /b/Projects/parser
2. запустить скрипт bash ./build-webapi.sh 1.0.7