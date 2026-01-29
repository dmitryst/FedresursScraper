NSPD Request
https://github.com/Logar1t/NSPD-request

pynspd
https://github.com/yazmolod/pynspd

Создание секрета 

kubectl create secret generic proxy-credentials --from-literal=PROXY_USER=DmitrystepanovaD5 --from-literal=PROXY_PASS=0d31echK0 --from-literal=PROXY_HOST=193.23.50.203 --from-literal=PROXY_PORT=10083 --dry-run=client -o yaml | kubectl apply -f -

Проверка создания

kubectl get secret proxy-credentials -o go-template='{{.data.PROXY_HOST | base64decode}}'