using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace EcoPulse.Worker.Services;

public class TestAlertService : IHostedService
{
    private readonly ILogger<TestAlertService> _logger;

    public TestAlertService(ILogger<TestAlertService> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var to = Environment.GetEnvironmentVariable("ECO_ALERT_TO") ?? "simaynglu@gmail.com";
            var user = Environment.GetEnvironmentVariable("ECO_SMTP_USER") ?? "simaynglu@gmail.com";
            var appPass = Environment.GetEnvironmentVariable("ECO_SMTP_APP_PASS") ?? "lvvxrbpllcenmnym"; // Gmail uygulama şifresi

            const string GRAFANA_URL = "http://localhost:3000/d/ecopulse-dashboard?orgId=1";
            var building = "TEST_BUILDING";

            // 🔹 SVG tabanlı logo
            string ecoPulseLogo = @"
            <svg xmlns='http://www.w3.org/2000/svg' width='200' height='50'>
              <rect width='200' height='50' rx='8' ry='8' fill='#2b5797'/>
              <text x='15' y='32' font-family='Segoe UI, Arial' font-size='20' fill='white'>Eco</text>
              <text x='65' y='32' font-family='Segoe UI, Arial' font-size='20' fill='#5bc0de'>Pulse</text>
              <circle cx='150' cy='25' r='6' fill='#d9534f'>
                <animate attributeName='r' values='5;8;5' dur='1.5s' repeatCount='indefinite'/>
              </circle>
            </svg>";

            // 🔹 HTML içeriği
            var htmlBody = $@"
            <!DOCTYPE html>
            <html>
              <body style='font-family:Segoe UI, Arial, sans-serif; background:#f4f4f4; padding:20px;'>
                <div style='max-width:600px; margin:auto; background:white; border-radius:10px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,0.1);'>
                  <div style='background:#2b5797; color:white; padding:15px; text-align:center;'>
                    {ecoPulseLogo}
                    <h2 style='margin-top:10px;'>✅ EcoPulse Test Uyarısı</h2>
                  </div>
                  <div style='padding:20px;'>
                    <p>Merhaba,</p>
                    <p><strong>{building}</strong> için test e-postası başarıyla gönderilmiştir.</p>

                    <table style='width:100%; border-collapse:collapse; margin-top:10px;'>
                      <tr style='background:#e8f4fc;'>
                        <th style='text-align:left; padding:8px;'>Ölçüm</th>
                        <th style='text-align:left; padding:8px;'>Değer</th>
                      </tr>
                      <tr>
                        <td style='padding:8px;'>Enerji</td>
                        <td style='padding:8px; color:#d9534f; font-weight:bold;'>123.45 kWh</td>
                      </tr>
                      <tr>
                        <td style='padding:8px;'>Su</td>
                        <td style='padding:8px; color:#5bc0de; font-weight:bold;'>12.34 m³</td>
                      </tr>
                    </table>

                    <div style='margin-top:22px;'>
                      <a href='http://localhost:3000/d/b73dd31d-b971-4831-906c-a40c80bf544e/ecopulse-dashboar?orgId=1&from=now-6h&to=now&timezone=browser'
                         style='display:inline-block; padding:12px 18px; background:#2b5797; color:#fff; text-decoration:none;
                                border-radius:8px; font-weight:600;'>
                         Grafana Dashboard’u Aç
                      </a>
                    </div>

                    <p style='margin-top:20px;'>📅 Tarih: {DateTime.Now:dd.MM.yyyy HH:mm}</p>
                    <p>Bu mesaj <b>EcoPulse</b> sisteminin test gönderimidir.</p>
                  </div>
                  <div style='background:#2b5797; color:white; text-align:center; padding:10px;'>
                    <small>© 2025 EcoPulse Akıllı Enerji ve Su Platformu</small>
                  </div>
                </div>
              </body>
            </html>";

            var msg = new MailMessage
            {
                From = new MailAddress(user, "EcoPulse Alert System"),
                Subject = $"[TEST] ⚙️ EcoPulse Uyarı Sistemi",
                Body = htmlBody,
                IsBodyHtml = true
            };
            msg.To.Add(to);

            using var smtp = new SmtpClient("smtp.gmail.com", 587)
            {
                Credentials = new NetworkCredential(user, appPass),
                EnableSsl = true,
                UseDefaultCredentials = false,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 20000
            };

            await smtp.SendMailAsync(msg, cancellationToken);
            _logger.LogInformation("✅ Test alert e-postası (HTML + logo + Grafana butonlu) gönderildi: {to}", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Test alert e-postası gönderilemedi");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
