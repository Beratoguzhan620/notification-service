# Notification Service

[English](README.md) · **Türkçe**

Diğer sistemlerin kişilere e-postayla ya da uygulama içi gelen kutusuyla ulaşmak için çağırdığı bir servis. Sürümlü Türkçe ve İngilizce şablonları doldurur, her alıcının kanal tercihlerine uyar ve bildirimleri RabbitMQ worker'larıyla teslim eder. İstekler idempotent, mesajlar veritabanından outbox ile çıkıyor, başarısız teslimatlar yeniden deneniyor ve sonunda yeniden oynatılabilen bir dead-letter kuyruğuna düşüyor. Her bildirim istekten posta kutusuna kadar iz (trace) olarak izlenebiliyor.

[![CI](https://github.com/Beratoguzhan620/notification-service/actions/workflows/ci.yml/badge.svg)](https://github.com/Beratoguzhan620/notification-service/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![RabbitMQ 4.2](https://img.shields.io/badge/RabbitMQ-4.2-FF6600)
![PostgreSQL 17](https://img.shields.io/badge/PostgreSQL-17-336791)
![OpenTelemetry](https://img.shields.io/badge/OpenTelemetry-Jaeger-1F2A37)
![Lisans: MIT](https://img.shields.io/badge/lisans-MIT-green)

![Tanıtım: terminalde uçtan uca akış, gönderilen e-posta, yeniden denemeleriyle bir bildirimin izi ve kuyruklar](docs/screenshots/demo.gif)

## Deneme

```bash
docker compose up --build
```

Bu komut PostgreSQL'i, RabbitMQ'yu, Mailpit'i, Jaeger'ı, API'yi ve **iki worker'ı** başlatır. Sonra:

- API belgesi: http://localhost:8080/scalar. Yönetici anahtarı `ns_localadm_local-admin-key-change-me-in-the-env-file` (yerel varsayılan; `.env` dosyasından değiştirebilirsin).
- Posta kutusu (Mailpit): http://localhost:8025, bütün e-postalar buraya düşer.
- İzler (Jaeger): http://localhost:16686
- Kuyruklar (RabbitMQ): http://localhost:15672, `notifications` / `rabbit-local`.
- Bütün akış tek seferde: `tests/e2e/flow.sh` (`curl` ve `jq` gerekir).

Bir bildirim göndermek:

```bash
curl -X POST http://localhost:8080/api/notifications \
  -H "X-Api-Key: ns_localadm_local-admin-key-change-me-in-the-env-file" \
  -H "Idempotency-Key: order-1001-shipped" \
  -H "Content-Type: application/json" \
  -d '{
        "recipient": { "id": "customer-42", "email": "ayse@example.com" },
        "channel": "Email",
        "template": "order-shipped",
        "language": "tr",
        "variables": { "name": "Ayşe", "orderNo": "SIP-1001", "carrier": "Yurtiçi Kargo", "trackingNo": "YK123456789" }
      }'
```

## Özellikler

- **İstemciler ve anahtarlar:** servisi çağıran her sistemin kendi API anahtarı var; yalnızca özeti (hash) saklanır, anahtar bir kez gösterilir. Gönderici istemciler bildirim gönderir ve kendi bildirimlerini okur; yöneticiler ayrıca istemcileri ve şablonları yönetir, dead letter'larla ilgilenir. İlk yönetici anahtarı yapılandırmadan gelir.
- **Şablonlar:** `order-shipped` gibi bir anahtar ve her dil (`tr`, `en`) için sürümler: konu, düz metin ve HTML. `{{name}}` gibi yer tutucular sürüm kaydedilirken kontrol edilir; değeri eksik bir istek reddedilir ve eksikler tek tek sayılır. Değerler HTML gövdesinde kaçışlanır (escape) ve konuya yeni satır ekleyemez. Önizleme uç noktası örnek değerlerle şablonu doldurur. Compose iki örnek şablonla başlar.
- **Idempotent istekler:** `POST /api/notifications` bir `Idempotency-Key` ister. Aynı anahtar ve gövde aynı bildirimi döner (`Idempotent-Replayed: true`), aynı anda gelen on özdeş istekte bile. Aynı anahtarla farklı bir gövde reddedilir.
- **Tercihler:** istemci ve alıcı bazında e-posta ya da uygulama içi kanal kapatılabilir. Bu durumda bildirim gönderilmez, `Suppressed` olarak kaydedilir.
- **Teslimat:** sabit bir `Message-ID` ile SMTP üzerinden e-posta (yerelde Mailpit) ve okundu bilgisi, okunmamış sayısı ve "hepsini okundu yap" özelliği olan uygulama içi gelen kutusu.
- **Yeniden deneme ve dead letter:** retry kuyrukları üzerinden artan aralıklarla üç deneme (varsayılan önce 5 sn, sonra 30 sn), ardından dead-letter kuyruğu. Yöneticiler dead letter'ları son hatalarıyla listeler ve yeniden oynatır.
- **Geçmiş:** her durum değişikliği zamanı ve ayrıntısıyla: `Accepted → Queued → Sending → Delivered` ya da `Failed` (ardından yeni deneme gelir), `Suppressed`, `DeadLettered`.
- **İzleme (tracing):** her bildirim için HTTP isteğinden outbox aktarıcısına, RabbitMQ'ya, worker'a ve SMTP çağrısına kadar tek bir OpenTelemetry izi; Jaeger'da görülebilir.
- **İşletim:** API ve her worker için sağlık kontrolleri, correlation id'li ve e-posta adresleri maskelenmiş yapılandırılmış loglar, cursor ile sayfalanan listeler.

## Ekran görüntüleri

| E-posta (Türkçe) | E-posta (İngilizce) |
|---|---|
| ![Türkçe sipariş kargoda e-postası](docs/screenshots/email.png) | ![İngilizce sipariş kargoda e-postası](docs/screenshots/email-en.png) |
| **Yeniden denemeleriyle bir iz** | **Yeniden oynatılıp teslim edilen bir dead letter** |
| ![Üç denemeli Jaeger izi](docs/screenshots/trace-retries.png) | ![Yeniden oynatmanın ve teslimatın Jaeger izi](docs/screenshots/trace.png) |
| **Kuyruklar** | **Posta kutusu** |
| ![RabbitMQ kuyrukları](docs/screenshots/queues.png) | ![Mailpit gelen kutusu](docs/screenshots/mailpit.png) |
| **API belgesi** | |
| ![API belgesi](docs/screenshots/api-reference.png) | |

## Teknolojiler

- ASP.NET Core 10 minimal API, ProblemDetails, Scalar ile OpenAPI
- RabbitMQ 4.2 ve resmi .NET istemcisi (RabbitMQ.Client 7): quorum kuyrukları, publisher confirm, elle onay (ack), TTL'li retry kuyrukları ve dead-letter
- Entity Framework Core 10 ve PostgreSQL (Npgsql), migration'lar, `FOR UPDATE SKIP LOCKED`, `xmin` satır sürümleri
- SMTP için MailKit, yerel posta sunucusu olarak Mailpit
- OTLP ile Jaeger'a gönderilen OpenTelemetry izleri
- Serilog ile correlation id ve maskelenmiş kişisel veri
- xUnit, Testcontainers (gerçek PostgreSQL, RabbitMQ ve Mailpit), Compose'a karşı uçtan uca bir shell akışı
- GitHub Actions: uyarılar hata sayılır, testler, bekleyen migration kontrolü, iki worker'lı Compose duman testi, uçtan uca akış
- Docker / Docker Compose

## Mimari

```mermaid
flowchart LR
    caller([Çağıran sistem]) -- "X-Api-Key<br/>Idempotency-Key" --> api

    subgraph apiproc [NotificationService.Api]
        api[REST API<br/>istemci, şablon, tercih,<br/>bildirim, gelen kutusu, dead letter]
    end

    api -- "tek transaction:<br/>bildirim + geçmiş + outbox" --> db[(PostgreSQL)]

    subgraph workers [NotificationService.Worker × 2]
        relay[Outbox aktarıcısı]
        consumer[Teslimat tüketicisi]
    end

    db -- "SKIP LOCKED" --> relay
    relay -- "yayınla, onay bekle" --> deliver[[notifications.deliver]]
    deliver --> consumer
    consumer -- "başarısız: kopya + ack" --> retry[[notifications.retry.5000ms<br/>notifications.retry.30000ms]]
    retry -- "TTL dolar" --> deliver
    consumer -- "deneme hakkı bitti: reject" --> dead[[notifications.dead]]
    consumer -- "durum, işlenen id" --> db
    consumer -- "SMTP" --> mail[(Mailpit)]
    api & relay & consumer -. "OTLP" .-> jaeger[(Jaeger)]
```

Bir bildirimin durumları:

```mermaid
stateDiagram-v2
    [*] --> Accepted: istek outbox satırıyla kaydedildi
    Accepted --> Queued: aktarıcı yayınladı
    Queued --> Sending: bir worker sahiplenir
    Queued --> Suppressed: kanal kapalı
    Sending --> Delivered
    Sending --> Failed: deneme başarısız
    Failed --> Sending: bekleme süresinden sonra
    Sending --> DeadLettered: son deneme başarısız
    DeadLettered --> Accepted: yönetici yeniden oynatır
```

## Tasarım kararları

- **MassTransit değil, doğrudan RabbitMQ.Client.** Onaylar (ack), publisher confirm'ler, retry kuyrukları ve dead-letter exchange kodda açıkça yazılı; her garantinin kodda nerede durduğu gösterilebiliyor. MassTransit daha sonra, saga'larının gerçekten işe yarayacağı mikroservis projesinde kullanılacak.
- **Transactional outbox.** Bildirim, ilk geçmiş kaydı ve outbox satırı tek transaction'da yazılır. Böylece geri alınmış bir isteğin mesajı olmaz, mesajı olmayan kayıtlı bir istek de olmaz. Her worker'daki aktarıcı yayınlanmamış satırları `FOR UPDATE SKIP LOCKED` ile alır (iki worker birbirini beklemeden işi paylaşır), her mesajı onay (confirm) alarak yayınlar ve satırı ancak ondan sonra işaretler. Arada bir çökme olursa mesaj yeniden yayınlanır; hiçbir zaman kaybolmaz.
- **En az bir kez teslimat, iki katmanlı mükerrer kontrolü.** Tüketici, biten her mesajın id'sini son durumla aynı transaction'da kaydeder; burada bulunan bir mesaj id'si iş yapılmadan onaylanır. İkinci koruma bildirimin kendi durumudur. İkisi de gerekli: yeniden oynatılan bir bildirim tekrar aktif olur ve eski bir kopyayı yeni mesajdan ayıran yalnızca mesaj id'sidir.
- **SMTP sırasında açık transaction kalmaz.** Bir teslimat üç adımdır: sahiplenme (satırı kilitle, mükerrerleri ve tercihleri kontrol et, `Sending` durumuna geçir), gönderme, kaydetme. Gönderirken ölen bir worker `Sending` durumunda bir satır bırakır; bir süre (lease) dolunca başka bir worker onu devralabilir. O zamana kadar mesajın ikinci bir kopyası iki kez göndermek yerine retry kuyruğunda bekler.
- **Zamanlayıcısız yeniden deneme.** Başarısız mesaj, mesajları bekleme süresi dolunca teslimat kuyruğuna geri dead-letter edilen bir retry kuyruğuna kopyalanır. Asıl mesaj ancak RabbitMQ kopyayı onayladıktan sonra ack'lenir. Retry kuyrukları at-least-once dead-lettering kullanır; süresi dolan mesajlar geri dönerken kaybolamaz. Bekleme süresi kuyruk adının parçası olduğundan süreyi değiştirmek var olan bir kuyrukla çakışmaz.
- **Bir kez doldurulur.** Şablon istek geldiğinde doldurulur ve bildirimle birlikte saklanır. Her deneme tam olarak kabul edilen içeriği gönderir; şablon sonradan değişse bile geçmiş, içeriğin hangi şablon sürümünden geldiğini gösterir.
- **Kaçışlama gereken yerde.** Değerler HTML gövdesinde HTML olarak kodlanır, düz metinde olduğu gibi kalır, konuda tek satıra indirilir. Böylece bir değer ne HTML ne de e-posta başlığı ekleyebilir.
- **Anahtarlar SHA-256 ile özetlenir.** API anahtarları 32 rastgele bayttır; paroladaki gibi tahmin edilecek bir şey yoktur, yavaş bir hash yalnızca her isteği yavaşlatırdı. Anahtarın kısa öneki istemciyi bulur, karşılaştırma sabit sürede yapılır.
- **Tercihler istek anında değil, teslimatta kontrol edilir.** İstekten sonra ama teslimattan önce e-postayı kapatan bir alıcıya hiçbir şey gitmez.

## Komutlar

| Komut | Ne yapar |
|---|---|
| `docker compose up --build` | PostgreSQL, RabbitMQ, Mailpit, Jaeger, http://localhost:8080 üzerinde API ve iki worker |
| `docker compose up --build --scale worker=4` | Aynısı, dört worker ile |
| `tests/e2e/flow.sh` | Çalışan kuruluma karşı uçtan uca akış: e-posta, idempotency, gelen kutusu, tercihler, bir dead letter ve yeniden oynatılması, izler |
| `dotnet test` | Birim ve entegrasyon testleri (Testcontainers için Docker gerekir) |
| `dotnet run --project src/NotificationService.Api` | API'yi yerelde çalıştırır (aşağıdaki ayarlarla) |
| `dotnet run --project src/NotificationService.Worker` | Yerelde bir worker çalıştırır |
| `dotnet ef migrations add <Ad> --project src/NotificationService.Infrastructure --startup-project src/NotificationService.Api --output-dir Persistence/Migrations` | Migration ekler |

Yerelde çalıştırmak için ayarlar; repoya girmezler:

```bash
dotnet user-secrets --project src/NotificationService.Api set "ConnectionStrings:Notifications" "Host=localhost;Database=notifications;Username=…;Password=…"
dotnet user-secrets --project src/NotificationService.Api set "Bootstrap:AdminApiKey" "ns_<8 küçük harf ya da rakam>_<en az 32 karakter>"
dotnet user-secrets --project src/NotificationService.Worker set "ConnectionStrings:Notifications" "Host=localhost;Database=notifications;Username=…;Password=…"
dotnet user-secrets --project src/NotificationService.Worker set "ConnectionStrings:RabbitMq" "amqp://kullanici:parola@localhost:5672/"
```

Worker `Smtp:Host` ve `Smtp:Port` ayarlarını okur (varsayılan Mailpit'in `localhost:1025` adresi). `OTEL_EXPORTER_OTLP_ENDPOINT` tanımlıysa iki süreç de iz gönderir.

## Testler

122 test: 58 birim testi ve gerçek API'yi, outbox aktarıcısını ve tüketiciyi Testcontainers'taki PostgreSQL, RabbitMQ ve Mailpit'e karşı çalıştıran 64 entegrasyon testi. Bunlara ek olarak CI'da Compose kurulumuna karşı uçtan uca bir akış çalışır.

- **Teslimat:** bir e-posta, kaçışlanmış HTML ve sabit bir `Message-ID` ile istekten Mailpit posta kutusuna ulaşır; uygulama içi bildirimler adres istemez; kapatılmış bir kanal `Suppressed` olur ve hiçbir şey gönderilmez.
- **Hatalar:** sürekli hata veren bir adres üç kez denenir ve dead-letter olur (mesajı dead kuyruğundadır); geçici bir hata çalışana kadar yeniden denenir; bir dead letter yeniden oynatılır ve teslim edilir.
- **Tam olarak bir e-posta:** aynı mesajın yeniden teslim edilmesi, bir outbox satırının iki kez yayınlanması ve aynı anda gelen on özdeş isteğin her biri tek bildirim ve tek e-postayla sonuçlanır.
- **Kuyruklar:** retry kuyruğundaki bir mesaj bekleme süresinden sonra geri gelir; reddedilen ve okunamayan mesajlar dead kuyruğuna düşer.
- **İstekler:** istemci bazında idempotency, değişken sırası ve biçimlendirmenin isteği değiştirmemesi, eksik değişkenler, bilinmeyen şablonlar, dile göre yedek şablon, konuya başlık ekleme denemesi.
- **Erişim:** bozuk, bilinmeyen, değiştirilmiş ve iptal edilmiş anahtarlar; göndericilerin yönetici uç noktalarına ve başka istemcilerin bildirimlerine ve gelen kutularına erişememesi.
- **İzleme:** istek, yayınlama, işleme ve teslimat span'leri doğru ebeveynlerle tek bir izi paylaşır.
- **Uçtan uca (CI):** yukarıdaki akış Compose'a karşı çalışır; Mailpit'in chaos modu bütün alıcıları reddederek gerçek bir dead letter oluşturur ve izlerin Jaeger'a ulaştığı kontrol edilir.

## Yol haritası ve bilinen eksikler

- [background-jobs](https://github.com/Beratoguzhan620/background-jobs) raporlarının bu servis üzerinden gönderilmesi.
- Aynı kanal arayüzünün arkasında SMS ve push kanalları.
- İstemci bazında hız sınırları ve sessiz saatler.
- Eski `processed_messages` ve yayınlanmış outbox satırlarının zamanlanmış bir işle temizlenmesi.
- API anahtarları her istekte veritabanından okunuyor; kısa bir önbellek bunu azaltır, karşılığında iptal biraz geç etkili olur.

## Lisans

[MIT](LICENSE) · Berat Oğuzhan Turğut · [GitHub](https://github.com/Beratoguzhan620)
