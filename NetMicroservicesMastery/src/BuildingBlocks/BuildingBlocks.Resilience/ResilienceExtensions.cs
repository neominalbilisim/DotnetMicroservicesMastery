using System;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.Retry;
using Polly.Timeout;

namespace BuildingBlocks.Resilience;

/// <summary>
/// Modül 2 - "Resiliency Patterns (Polly Entegrasyonu)".
/// HttpClientFactory üzerinden Retry, Circuit Breaker ve Fallback politikalarını
/// tüm servis-arası HTTP çağrılarına merkezi olarak uygulayan extension noktası.
///
///   İstemci İsteği -> Retry (N deneme) -> Circuit Breaker (Açık/Kapalı) -> Fallback
///
/// Pipeline sırası (dıştan içe): Fallback -> Retry -> Circuit Breaker -> Timeout.
/// Fallback EN DIŞTA olmalıdır ki, içerideki Retry/CircuitBreaker/Timeout'un
/// TÜMÜ tükendiğinde son çare olarak devreye girebilsin.
/// </summary>
public static class ResilienceExtensions
{
    public static IHttpClientBuilder AddStandardResiliency(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler("standard-resiliency", pipelineBuilder =>
        {
            // 1) Fallback — Retry+CircuitBreaker+Timeout'un TÜMÜ başarısız
            //    olursa (ör. bağımlı servis tamamen erişilemezse), istemciye
            //    ham bir exception/500 yerine kontrollü, öngörülebilir bir
            //    "servis şu an kullanılamıyor" cevabı döner.
            pipelineBuilder.AddFallback(new FallbackStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<Exception>()
                    .HandleResult(response => !response.IsSuccessStatusCode),
                FallbackAction = _ =>
                {
                    var fallbackResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent(
                            "{\"title\":\"Bağımlı servise şu an ulaşılamıyor (fallback cevabı)\",\"status\":503}",
                            Encoding.UTF8, "application/json")
                    };
                    return Outcome.FromResultAsValueTask(fallbackResponse);
                }
            });

            // 2) Retry — "Retry storm" riskine karşı EXPONENTIAL BACKOFF + JITTER
            //    kullanılır (sabit aralıklı retry, zaten yoğun olan bir servise
            //    senkronize dalgalar halinde ek yük bindirebilir).
            pipelineBuilder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(200)
            });

            // 3) Circuit Breaker — 30 saniyelik pencerede en az 5 istekten
            //    %50'si hata verirse devreyi 15 saniye boyunca açar; bu süre
            //    boyunca istekler bağımlı servise HİÇ gönderilmez (ona nefes
            //    alma/toparlanma fırsatı tanır) ve doğrudan Fallback'e düşer.
            pipelineBuilder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(15)
            });

            // 4) Per-attempt Timeout — her TEK deneme en fazla 5 saniye sürebilir
            //    (toplam Retry süresi bundan bağımsız olarak katlanabilir).
            pipelineBuilder.AddTimeout(TimeSpan.FromSeconds(5));
        });

        return builder;
    }
}
