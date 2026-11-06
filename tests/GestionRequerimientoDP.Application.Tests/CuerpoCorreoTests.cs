using GestionRequerimientoDP.Infrastructure.Email;
using MimeKit;

namespace GestionRequerimientoDP.Application.Tests;

public class CuerpoCorreoTests
{
    const string Texto = "Hola Cristian,\n\nLos siguientes requerimientos:\n• Mié 4 nov · 26-069756-02 · Titulados AIEP — Base de datos\n• Jue 5 nov · 26-026535-04-01 · <SSTT> & Costos\n  sangría\n\nVer y responder: https://srv/PlanificacionOps/r/12";

    [Fact]
    public void Cada_linea_es_un_salto_real_en_html_y_se_escapa_el_contenido()
    {
        var html = CuerpoCorreo.Html(Texto);
        Assert.Contains("Base de datos<br>\n• Jue 5 nov", html); // cada requerimiento en su propia línea
        Assert.Contains("&lt;SSTT&gt; &amp; Costos", html);
        Assert.Contains("&nbsp;&nbsp;sangr", html);
        Assert.Contains("<a href=\"https://srv/PlanificacionOps/r/12\">", html);
    }

    [Fact]
    public void Envia_texto_y_html_como_alternativas()
    {
        var body = Assert.IsType<MultipartAlternative>(CuerpoCorreo.Construir(Texto));
        Assert.Equal(Texto, body.TextBody);
        Assert.Contains("<br>", body.HtmlBody);
    }
}
