## Временно выключаем мониторинг

# 1. Выключаем сам Prometheus (самый тяжелый компонент):
Поскольку Prometheus управляется оператором, стандартный kubectl scale на него не сработает. Нужно отредактировать ресурс Prometheus:

```bash
kubectl patch prometheus monitoring-kube-prometheus-prometheus -n monitoring --type='merge' -p '{"spec": {"replicas": 0}}'
```

# 2. Выключаем Grafana:

```bash
kubectl scale deployment monitoring-grafana -n monitoring --replicas=0
```

# 3. Выключаем Prometheus Operator и Alertmanager:

```bash
kubectl scale deployment monitoring-kube-prometheus-operator -n monitoring --replicas=0
kubectl patch alertmanager monitoring-kube-prometheus-alertmanager -n monitoring --type='merge' -p '{"spec": {"replicas": 0}}'
```

# 4. Выключаем Kube-State-Metrics:

```bash
kubectl scale deployment monitoring-kube-state-metrics -n monitoring --replicas=0
```

После этого Node Exporter (DaemonSet) можно оставить работать, он потребляет копейки (~20-30 МБ памяти), либо удалить его через kubectl delete daemonset monitoring-prometheus-node-exporter -n monitoring.

Как включить обратно, когда понадобятся:
Просто выполните те же команды, заменив 0 на 1.