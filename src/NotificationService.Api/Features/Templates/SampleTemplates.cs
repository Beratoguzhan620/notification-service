using Microsoft.EntityFrameworkCore;
using NotificationService.Core.Templates;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.Api.Features.Templates;

/// <summary>
/// Two ready-made templates in Turkish and English, created at startup when <c>Bootstrap:SampleTemplates</c>
/// is on, so a fresh stack can send something right away. Existing templates are never touched.
/// </summary>
internal static class SampleTemplates
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        if (!scope.ServiceProvider.GetRequiredService<IConfiguration>().GetValue("Bootstrap:SampleTemplates", false))
        {
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        if (!await db.Templates.AnyAsync(t => t.Key == "order-shipped", cancellationToken))
        {
            var shipped = Template.Create("order-shipped", "Sent when an order leaves the warehouse.", Languages.Turkish, now);
            shipped.AddVersion(Languages.Turkish,
                "Siparişiniz yola çıktı: {{orderNo}}",
                """
                Merhaba {{name}},

                {{orderNo}} numaralı siparişiniz {{carrier}} ile yola çıktı.
                Takip numarası: {{trackingNo}}

                İyi alışverişler.
                """,
                Layout("Siparişiniz yola çıktı",
                    "Merhaba {{name}},",
                    "<strong>{{orderNo}}</strong> numaralı siparişiniz <strong>{{carrier}}</strong> ile yola çıktı.",
                    "Takip numarası", "{{trackingNo}}",
                    "Bu e-postayı sipariş verdiğiniz için alıyorsunuz."),
                now);
            shipped.AddVersion(Languages.English,
                "Your order {{orderNo}} is on its way",
                """
                Hello {{name}},

                Your order {{orderNo}} has left with {{carrier}}.
                Tracking number: {{trackingNo}}

                Thank you for shopping with us.
                """,
                Layout("Your order is on its way",
                    "Hello {{name}},",
                    "Your order <strong>{{orderNo}}</strong> has left with <strong>{{carrier}}</strong>.",
                    "Tracking number", "{{trackingNo}}",
                    "You are receiving this email because you placed an order."),
                now);
            db.Templates.Add(shipped);
        }

        if (!await db.Templates.AnyAsync(t => t.Key == "password-changed", cancellationToken))
        {
            var password = Template.Create("password-changed", "Sent after a password change, as a security notice.", Languages.Turkish, now);
            password.AddVersion(Languages.Turkish,
                "Şifreniz değiştirildi",
                """
                Merhaba {{name}},

                Hesabınızın şifresi {{changedAt}} tarihinde değiştirildi.
                Bu değişikliği siz yapmadıysanız hemen bizimle iletişime geçin.
                """,
                Layout("Şifreniz değiştirildi",
                    "Merhaba {{name}},",
                    "Hesabınızın şifresi değiştirildi. Bu değişikliği siz yapmadıysanız hemen bizimle iletişime geçin.",
                    "Değişiklik zamanı", "{{changedAt}}",
                    "Bu bir güvenlik bildirimidir ve kapatılamaz."),
                now);
            password.AddVersion(Languages.English,
                "Your password was changed",
                """
                Hello {{name}},

                The password of your account was changed on {{changedAt}}.
                If this was not you, contact us right away.
                """,
                Layout("Your password was changed",
                    "Hello {{name}},",
                    "The password of your account was changed. If this was not you, contact us right away.",
                    "Changed at", "{{changedAt}}",
                    "This is a security notice."),
                now);
            db.Templates.Add(password);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A plain email layout with inline styles, which is what mail clients reliably render.</summary>
    private static string Layout(string title, string greeting, string message, string label, string value, string footer) => $"""
        <!doctype html>
        <html>
        <body style="margin:0;padding:24px 12px;background:#F7F6F3;font-family:'IBM Plex Sans',Arial,sans-serif;color:#1A1A1A;">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#FFFFFF;border:1px solid #E2DFD8;border-radius:10px;">
            <tr><td style="padding:28px 28px 8px;">
              <p style="margin:0 0 6px;font:500 12px 'IBM Plex Mono',monospace;letter-spacing:.04em;text-transform:uppercase;color:#2F5D80;">Notification Service</p>
              <h1 style="margin:0 0 18px;font-size:22px;font-weight:600;">{title}</h1>
              <p style="margin:0 0 12px;font-size:15px;line-height:1.5;">{greeting}</p>
              <p style="margin:0 0 20px;font-size:15px;line-height:1.5;">{message}</p>
            </td></tr>
            <tr><td style="padding:0 28px 24px;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border:1px solid #E2DFD8;border-radius:6px;background:#F7F6F3;">
                <tr><td style="padding:12px 14px;font-size:13px;color:#6B6862;">{label}</td></tr>
                <tr><td style="padding:0 14px 12px;font:500 16px 'IBM Plex Mono',monospace;color:#1A1A1A;">{value}</td></tr>
              </table>
            </td></tr>
            <tr><td style="padding:16px 28px;border-top:1px solid #E2DFD8;font-size:12px;color:#6B6862;">{footer}</td></tr>
          </table>
        </body>
        </html>
        """;
}
