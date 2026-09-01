using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Resilience;

/// <summary>
/// Modül 2 - "Secret Management: HashiCorp Vault".
/// appsettings.json'daki "Vault" bölümünü (Address, SecretPath) ve "Vault:Token"
/// (ortam değişkeni: VAULT__Token) kullanarak Vault'tan secret okur; okunan her
/// anahtarı doğrudan IConfiguration'a enjekte eder. Vault'taki anahtar isimleri
/// IConfiguration path formatında tutulur (örn. "ConnectionStrings:OrderDb"),
/// böylece aynı isimli appsettings.json değerini ŞEFFAFÇA override eder — kod
/// tarafında (Program.cs, DbContext vb.) HİÇBİR DEĞİŞİKLİK gerekmez.
///
///   1) DB Kaydı + Outbox Kaydı ... (ilgisiz)
///   .NET Servisi -> Vault'a Token ile kimlik doğrular -> secret/data/{servis} okunur
///   -> appsettings üzerine uygulanır
///
/// ÖNEMLİ: docker-compose.infra.yml'deki Vault dev sunucusu IN-MEMORY çalışır;
/// her yeniden başlatmada TÜM secret'lar kaybolur. Yeniden yüklemek için:
///   infra/vault/seed-secrets.sh   (Linux/Mac/WSL)
///   infra/vault/seed-secrets.cmd  (Windows cmd.exe)
///
/// Vault'a ulaşılamazsa (örn. henüz seed edilmediyse veya ayakta değilse)
/// uygulama BAŞLAMAYI REDDETMEZ — appsettings.json'daki (fallback) değerlerle
/// devam eder ve bir uyarı loglar. Bu, yerel geliştirmede Vault'suz da
/// çalışabilmeyi sağlar.
/// </summary>
public static class VaultExtensions
{
    private const string DefaultMountPoint = "secret";

    public static IHostApplicationBuilder AddVaultSecrets(this IHostApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var vaultAddress = configuration["Vault:Address"];
        var vaultToken = configuration["Vault:Token"];
        var secretPath = configuration["Vault:SecretPath"];

        if (string.IsNullOrWhiteSpace(vaultAddress) || string.IsNullOrWhiteSpace(secretPath))
        {
            Console.Error.WriteLine("[Vault] Vault:Address veya Vault:SecretPath tanımlı değil; Vault entegrasyonu atlanıyor, appsettings.json değerleri kullanılacak.");
            return builder;
        }

        if (string.IsNullOrWhiteSpace(vaultToken))
        {
            Console.Error.WriteLine("[Vault] Vault:Token tanımlı değil (VAULT__Token ortam değişkenini kontrol edin); Vault entegrasyonu atlanıyor.");
            return builder;
        }

        try
        {
            var authMethod = new VaultSharp.V1.AuthMethods.Token.TokenAuthMethodInfo(vaultToken);
            var vaultClientSettings = new VaultSharp.VaultClientSettings(vaultAddress, authMethod)
            {
                VaultServiceTimeout = TimeSpan.FromSeconds(5)
            };
            var vaultClient = new VaultSharp.VaultClient(vaultClientSettings);

            // "secret/data/order-service" -> mountPoint: "secret", path: "order-service"
            var normalizedPath = secretPath
                .Replace($"{DefaultMountPoint}/data/", string.Empty)
                .TrimStart('/');

            var secret = vaultClient.V1.Secrets.KeyValue.V2
                .ReadSecretAsync(path: normalizedPath, mountPoint: DefaultMountPoint)
                .GetAwaiter().GetResult();

            var vaultValues = secret.Data.Data
                .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());

            if (vaultValues.Count > 0)
            {
                configuration.AddInMemoryCollection(vaultValues!);
                Console.WriteLine($"[Vault] '{normalizedPath}' altından {vaultValues.Count} secret okundu ve appsettings üzerine uygulandı.");
            }
            else
            {
                Console.Error.WriteLine($"[Vault] '{normalizedPath}' altında hiç secret bulunamadı. Seed script'ini çalıştırdınız mı? (infra/vault/seed-secrets.sh|.cmd)");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Vault] Secret okunamadı ('{secretPath}'): {ex.Message}. appsettings.json'daki (fallback) değerlerle devam ediliyor.");
        }

        return builder;
    }
}
