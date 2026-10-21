# JWT Learning API

.NET 10, ASP.NET Core Controllers, EF Core ve PostgreSQL ile JWT öğrenme projesi.
Tek iş servisi `IUserService` / `UserService`'tir; JWT üretimi de bu servistedir.

## Mimari

```text
Domain          ← Application
   ↑                   ↑
Infrastructure  ←     API (composition root)
```

- **Domain:** User ve RefreshToken; framework bağımlılığı yok.
- **Application:** IUserService, istek/yanıt DTO'ları ve iş hataları. Infrastructure'a bağımlı değil.
- **Infrastructure:** EF Core DbContext, PostgreSQL migration, PasswordHasher ve UserService implementasyonu.
- **API:** UsersController, DI, JWT doğrulama, Swagger, rate limiting ve ProblemDetails.

EF Core veri erişimini zaten kapsadığı için yalnızca yönlendirme yapan ek bir repository katmanı eklenmedi. Controller iş mantığı içermez.

## Çalıştırma

Gereksinimler: .NET SDK **10.0.402**, Docker Compose ve Python 3.

```bash
bash scripts/dev-setup.sh
bash scripts/dev-start.sh
```

Kurulum rastgele PostgreSQL parolası ve JWT imza anahtarıyla `.env` oluşturur (Git tarafından yok sayılır), PostgreSQL'i başlatır, kilitli bağımlılıkları yükler, derler ve migration uygular. Var olan `.env` korunur. API geliştirme ortamında **5080** portunda çalışır; Swagger yolu **/swagger**.

`.env` gizlidir: paylaşmayın veya commit etmeyin. Başka makinede SDK PATH'te olmalıdır; `/workspace/.dotnet` sadece bulut ortamındaki ek arama yoludur.

Şema değişiklikleri için:

```bash
set -a; source .env; set +a
dotnet ef migrations add MigrationName --project src/JwtToken.Infrastructure --startup-project src/JwtToken.Api
dotnet run --project src/JwtToken.Api -- --migrate
```

Migration ayrı bir dağıtım adımıdır; normal API başlangıcı veritabanı şemasını değiştirmez.

## Swagger ile deneme

1. `POST /api/users/register`: `{"email":"demo@example.com","password":"Learning-JWT!2026"}` ile kullanıcı oluşturun.
2. `POST /api/users/login`: aynı bilgilerle giriş yapın. `accessToken` ve `refreshToken` döner.
3. Swagger **Authorize** düğmesine yalnızca `accessToken` değerini girin; Bearer öneki otomatik eklenir.
4. `GET /api/users/me`: token sahibinin kullanıcı bilgilerini görün. Token olmadan 401 döner.
5. `POST /api/users/refresh`: `{"refreshToken":"..."}` gönderin. Yeni access ve refresh token döner; eskisini saklamayın.
6. `POST /api/users/logout`: son refresh token ile oturumu iptal edin.

| Uç | Başarı | Önemli hata |
| --- | --- | --- |
| POST /api/users/register | 201 | 400 doğrulama, 409 mevcut e-posta |
| POST /api/users/login | 200 | 401 hatalı kimlik bilgisi |
| POST /api/users/refresh | 200 | 401 geçersiz/süresi dolmuş/iptal token |
| POST /api/users/logout | 204 | Geçersiz token için de idempotent 204 |
| GET /api/users/me | 200 | 401 eksik/geçersiz token |

Anonim kimlik doğrulama uçları IP başına toplam 20 istek/dakika ile sınırlıdır (429). Swagger yalnızca Development ortamında açıktır.

## JWT nasıl çalışıyor?

Login sırasında parola ASP.NET Core PasswordHasher ile doğrulanır (salt içeren PBKDF2, 210.000 iterasyon). Parola veya parola özeti token'a konmaz. JWT `sub` (user ID), `jti` (tekil token ID), `iat`, `nbf`, `exp`, `iss`, `aud` alanlarını taşır. JWT **şifreli değildir**; içeriği okunabilir. HS256 imzası değişiklikleri tespit eder.

Access token 10 dakika geçerlidir. JwtBearer middleware imza, izin verilen algoritma, issuer, audience ve süreyi doğrular (30 saniye saat toleransı). Controller'daki `[Authorize]` bu doğrulama tamamlanmadan erişime izin vermez. `me`, kullanıcı ID'sini doğrulanmış `sub` claim'inden alır.

Refresh token JWT değildir: kriptografik rastgele 64 bayttır, 7 gün geçerlidir. Veritabanında yalnızca SHA-256 özeti tutulur. Her yenilemede eski token iptal edilir ve aynı oturum ailesinde yeni token oluşturulur. Eski token tekrar kullanılırsa o ailenin aktif token'ları da iptal edilir. PostgreSQL transaction ve aile başına advisory lock, eşzamanlı refresh/logout işlemlerini sıralar. İstemci aynı refresh isteğini paralel göndermemeli; ağ hatasından sonra eski token ile yeniden denemek de güvenlik gereği oturumu kapatabilir. Her yeni token'ın süresi yenilendiğinden bu örnekte mutlak oturum süresi sınırı yoktur.

Logout refresh ailesini kapatır. Daha önce verilen access token süresi dolana kadar geçerlidir; anlık access token iptali için ayrıca denylist veya oturum durumu kontrolü gerekir. Eski refresh kayıtları yeniden kullanım tespiti için tutulur; büyüyen gerçek sistemlerde uygun saklama ve temizleme politikası gerekir.

## Gerçek projeye taşıma

Bu proje kendi kullanıcılarını yöneten tek API için güvenli temelleri gösterir; tam bir kimlik sağlayıcı değildir. Üretimde HTTPS kullanın, anahtarı secret manager'da tutun ve anahtar rotasyonu planlayın. Birden çok servis için asimetrik imza/JWKS ve OAuth2/OIDC sağlayıcısı değerlendirin. MFA, e-posta doğrulama, parola sıfırlama, dağıtık rate limiting ve güvenlik olay kayıtları ayrıca tasarlanmalıdır. Reverse proxy arkasında güvenilen proxy/IP yapılandırmasını açıkça yapın.

Swagger token'ı elle taşımak içindir. Tarayıcı istemcilerinde refresh token'ı JavaScript/localStorage yerine Secure + HttpOnly cookie veya BFF ile koruyun; cookie kullanılıyorsa CSRF korumasını da ekleyin. Bu örneğin JSON token yanıtları HTTPS üzerinden güvenilir istemcinin güvenli saklaması varsayımıyla tasarlanmıştır.

## Doğrulama

API çalışırken, ayrı terminalde:

```bash
set -a; source .env; set +a
python3 scripts/smoke-test.py
```

Script gerçek PostgreSQL üzerinden kayıt/giriş, hatalı parola, token olmadan erişim, bozuk imza, geçmiş süre, yanlış issuer/audience, refresh rotasyonu, tekrar kullanım tespiti, eşzamanlı yenileme ve logout davranışını 24 HTTP kontrolüyle doğrular. Her çalıştırma ayrı test kullanıcısı oluşturur; kısa sürede tekrar çalıştırırsanız rate limit nedeniyle bir sonraki pencereyi bekleyin. Token veya parola yazdırmaz.
