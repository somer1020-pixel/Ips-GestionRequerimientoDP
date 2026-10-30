using System.Text.RegularExpressions;

namespace GestionRequerimientoDP.Domain;

/// <summary>Estado de una cuenta: las solicitudes de acceso (registro o cuenta AD nueva) quedan pendientes hasta que un líder las aprueba.</summary>
public enum Aprobacion { Aprobado, Pendiente }

/// <summary>Cómo se creó el usuario.</summary>
public enum OrigenUsuario { Admin, Registro, ActiveDirectory }

/// <summary>Registro de eventos de autenticación y administración de cuentas.</summary>
public class AuditoriaAcceso
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    /// <summary>Correo, RUT o cuenta Windows tal como se usó.</summary>
    public string Usuario { get; set; } = "";
    public string Accion { get; set; } = "";
    /// <summary>Local, ActiveDirectory, Registro, Admin.</summary>
    public string Metodo { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Detalle { get; set; } = "";
}

/// <summary>Token de recuperación de contraseña: se guarda solo su hash, vence y es de un solo uso.</summary>
public class PasswordResetToken
{
    public long Id { get; set; }
    public int UsuarioId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public string Ip { get; set; } = "";
}

/// <summary>RUT chileno: normalización "12345678-K", validación módulo 11 y formato con puntos.</summary>
public static class Rut
{
    /// <summary>"11.111.111-1", "111111111" o "11111111-k" → "11111111-1" / "…-K". Vacío si no tiene forma de RUT.</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var c = raw.Trim().Replace(".", "").Replace(" ", "").ToUpperInvariant();
        string body, dv;
        if (c.Contains('-'))
        {
            var p = c.Split('-');
            if (p.Length != 2 || p[1].Length != 1) return "";
            (body, dv) = (p[0], p[1]);
        }
        else
        {
            if (c.Length < 2 || c.Length > 10) return "";
            (body, dv) = (c[..^1], c[^1..]);
        }
        body = body.TrimStart('0');
        if (body.Length is 0 or > 9 || !body.All(char.IsDigit) || !(char.IsDigit(dv[0]) || dv == "K")) return "";
        return $"{body}-{dv}";
    }

    public static bool IsValid(string? raw)
    {
        var n = Normalize(raw);
        return n.Length > 0 && n[^1..] == CalculateDv(n[..^2]);
    }

    /// <summary>¿Lo ingresado en "Correo o RUT" parece un RUT?</summary>
    public static bool IsCandidate(string? input) =>
        !string.IsNullOrWhiteSpace(input) && !input.Contains('@') && Normalize(input) is { Length: >= 8 };

    public static string CalculateDv(string body)
    {
        int sum = 0, m = 2;
        for (var i = body.Length - 1; i >= 0; i--)
        {
            sum += (body[i] - '0') * m;
            m = m == 7 ? 2 : m + 1;
        }
        var r = 11 - sum % 11;
        return r == 11 ? "0" : r == 10 ? "K" : r.ToString();
    }

    /// <summary>"11111111-1" → "11.111.111-1".</summary>
    public static string Format(string? raw)
    {
        var n = Normalize(raw);
        if (n.Length == 0) return raw ?? "";
        var body = n[..^2];
        var withDots = string.Join(".", Enumerable.Range(0, (body.Length + 2) / 3)
            .Select(i => body.Substring(Math.Max(0, body.Length - 3 * (i + 1)), Math.Min(3, body.Length - 3 * i))).Reverse());
        return $"{withDots}-{n[^1..]}";
    }
}

/// <summary>Política de contraseñas (de Ipsos.Bhe.Auth): 8–100 caracteres, mayúscula, minúscula, número y símbolo, sin el usuario ni el correo.</summary>
public static partial class PoliticaContrasena
{
    public const int Minimo = 8;
    public const int Maximo = 100;
    public const string Resumen = "Mínimo 8 caracteres, con al menos una mayúscula, una minúscula, un número y un símbolo (! @ # $ % * - _).";

    [GeneratedRegex("[A-Z]")] private static partial Regex Mayus();
    [GeneratedRegex("[a-z]")] private static partial Regex Minus();
    [GeneratedRegex("[0-9]")] private static partial Regex Digito();
    [GeneratedRegex(@"[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?~`]")] private static partial Regex Simbolo();

    public static List<string> Validar(string? password, string? email = null, string? nombre = null)
    {
        var e = new List<string>();
        if (string.IsNullOrEmpty(password)) return ["La contraseña es obligatoria."];
        if (password.Length < Minimo) e.Add($"Debe tener al menos {Minimo} caracteres.");
        if (password.Length > Maximo) e.Add($"No puede superar los {Maximo} caracteres.");
        if (!Mayus().IsMatch(password)) e.Add("Debe incluir al menos una letra mayúscula.");
        if (!Minus().IsMatch(password)) e.Add("Debe incluir al menos una letra minúscula.");
        if (!Digito().IsMatch(password)) e.Add("Debe incluir al menos un número.");
        if (!Simbolo().IsMatch(password)) e.Add("Debe incluir al menos un símbolo (ej. ! @ # $ % * - _).");
        if (email?.Split('@')[0] is { Length: >= 4 } local && password.Contains(local, StringComparison.OrdinalIgnoreCase))
            e.Add("No puede contener la parte inicial de tu correo.");
        if (nombre?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is { Length: >= 4 } n && password.Contains(n, StringComparison.OrdinalIgnoreCase))
            e.Add("No puede contener tu nombre.");
        return e;
    }
}
