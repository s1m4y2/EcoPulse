# ⚡ EcoPulse – Akıllı Enerji ve Su Tüketim Analiz Platformu

🔥 **Gerçek zamanlı tüketim takibi • 📊 ML.NET ile enerji & su tahmini • 📈 Prometheus + Grafana izleme • 🚨 E-posta & Alertmanager uyarı sistemi**

EcoPulse, binalardaki **elektrik ve su tüketim verilerini toplayan**, analiz eden ve **geleceğe yönelik tahminler üreten** akıllı bir enerji yönetim platformudur.

Sistem; gerçek zamanlı ölçüm alma, tüketim anomali tespiti, günlük tahmin üretimi ve Slack/E-posta ile otomatik uyarı gönderme yeteneklerine sahiptir.

---



## 🚀 Özellikler

### 🔹 Gerçek Zamanlı Tüketim Kaydı

* API, bina bazlı enerji (kWh) ve su (m³) verilerini alır.
* Worker servisi, bu verileri Prometheus metriklerine taşır.

### 🔹 ML.NET ile Tahmin

* 1 gün ileri enerji tüketimi tahmini
* 1 gün ileri su tüketimi tahmini
* Time Series Forecasting modelleri kullanılır (ML.NET).

### 🔹 Anomali Tespiti

* Tüketim eşikleri aşıldığında alarm üretir.
* Prometheus Gauge metrikleri güncellenir.
* Gmail SMTP veya Slack uyarıları tetiklenir.

### 🔹 Grafana Dashboard

* Enerji & su tüketimi trendleri
* Tahmin grafikleri
* Bina bazlı analiz
* Gerçek zamanlı metrik ekranı

### 🔹 Prometheus + Alertmanager

* Enerji ve su için eşik denetimi
* Kritik eşiklerde otomatik uyarı akışı

---

## 🧱 Mimari

```
┌────────────────────┐      POST /readings       ┌────────────────────┐
│   Ingestor (CSV)   │ ───────────────────────▶  │     EcoPulse API   │
└────────────────────┘                           └────────────────────┘
        ▲                                                    │
        │                                                    ▼
        │                                        SQL Server Database
        │                                                    │
        ▼                                                    ▼
┌────────────────────┐      ML.NET Tahmin       ┌────────────────────┐
│ EcoPulse Worker ML │ ───────────────────────▶ │    Forecasts Table  │
│ (Forecast + Alert) │                           └────────────────────┘
└────────────────────┘
        │
        │  /metrics
        ▼
┌────────────────────┐
│     Prometheus     │
└────────────────────┘
        │
        ▼
┌────────────────────┐
│      Grafana       │
└────────────────────┘
```

---

## 🛠 Kullanılan Teknolojiler

| Alan              | Teknoloji                               |
| ----------------- | --------------------------------------- |
| Backend           | .NET 8, Minimal API, Dapper             |
| ML                | ML.NET Time Series Forecasting          |
| Metrics           | Prometheus                              |
| Visualization     | Grafana                                 |
| Alerts            | Alertmanager, Gmail SMTP, Slack Webhook |
| Data Pipeline     | CSV Ingestor, Worker Services           |
| Database          | SQL Server 2022                         |
| DevOps            | Docker Compose                          |
| Secret Management | .env (EP_SQL, EP_SMTP_PASS vb.)         |

---

## 📁 Proje Yapısı

```
EcoPulse/
│── EcoPulse.Api/           # REST API + Swagger + Prometheus
│── EcoPulse.Worker/        # ML Tahmin + Alert yönetimi
│── EcoPulse.Ingestor/      # CSV’den veri yükleyen işlemci
│── EcoPulse.Analytics/     # Model eğitimi (ML.NET)
│── EcoPulse.Common/        # Ortak modeller + Database helper
│── grafana-provisioning/   # Dashboard & Datasource
│── docker-compose.yml      # Prometheus + Grafana
│── .env                    # SİZİN GİZLİ BİLGİLERİNİZ (GitHub'a yüklenmez)
└── .gitignore
```

---

## 🔑 Secret Yönetimi (ENV)

Projedeki tüm gizli bilgiler `.env` dosyasındadır ve GitHub’a yüklenmez.

**Örnek .env:**

```
EP_SQL=Server=...;Database=EcoPulse;...
EP_SQL_MASTER=Server=...;Database=master;...
EP_SMTP_USER=...
EP_SMTP_PASS=...
EP_SMTP_TO=...
EP_SMTP_HOST=smtp.gmail.com
EP_SMTP_PORT=587
```

API & Worker başlangıcında:

```csharp
var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
Env.Load(envPath);
```

---

## 🚀 Kurulum

### 1️⃣ Gerekli Araçlar

* .NET 8 SDK
* SQL Server 2022
* Docker Desktop
* Git

### 2️⃣ ENV dosyası oluştur

Proje köküne `.env` ekleyin:

```
EP_SQL=...
EP_SQL_MASTER=...
EP_SMTP_USER=...
EP_SMTP_PASS=...
```

### 3️⃣ Projeyi Çalıştırma

#### 🔥 Tüm sistemi birlikte çalıştırma (API + Worker + Ingestor)

Visual Studio → **Solution → Set Startup Projects → Multiple → Start (hepsi)** → **F5**

#### 🔥 Prometheus + Grafana (Docker)

```
cd ops
docker compose up -d
```

* Prometheus → [http://localhost:9090](http://localhost:9090)
* Grafana → [http://localhost:3000](http://localhost:3000) (admin / admin)

---

## 🧪 API Endpointleri

### 🔹 GET /health

Sistemin sağlık durumunu döner.

### 🔹 POST /api/readings

Enerji ve su ölçümü kaydeder:

```
{
  "BuildingId": "C1_M1",
  "Timestamp": "2025-11-01T12:00:00",
  "EnergyKWh": 45.6,
  "WaterM3": 1.2
}
```

### 🔹 GET /api/forecasts?building=C1_M1

Tahmin sonuçlarını döner.

### 🔹 GET /metrics

Prometheus metriklerini döner.

---

## 📣 Uyarı Mekanizması

### E-posta Uyarıları

Tüketim eşikleri aşıldığında Gmail SMTP ile otomatik bildirim gönderilir.

### Prometheus + Alertmanager Uyarıları

Kritik tüketim durumları alert olarak tetiklenir.

---

## 🌟 Katkı

Pull request’lere açıktır. Yeni algoritmalar, dashboardlar veya geliştirmeler ekleyebilirsiniz.

## 📄 Lisans

MIT Lisansı.

```

---

💙 **Hazır!**  
README artık projenin içinde, tek parça, kopmayan, profesyonel bir şekilde duruyor.

Ekran görüntüleri eklersen, README’yi daha da premium hale getirebilirim.
```
