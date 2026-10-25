using System.Globalization;
using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Infrastructure.Data;

/// <summary>
/// Datos semilla idempotentes (solo si no hay usuarios): usuarios, la planilla Prog DP.xlsx (hoja agenda),
/// historia sintética, scripts, codificación y solicitudes comerciales. Es el mismo generador del mockup
/// (seedReqs); las fechas se desplazan por semanas completas para que la semana de la planilla sea la actual.
/// </summary>
public static class DbSeeder
{
    public const string DemoPassword = "demo123";
    static readonly DateOnly Cur0 = new(2026, 10, 19);

    static readonly (string Key, string Nombre, string Email, Rol Rol, string Eq)[] SeedUsers =
    [
        ("cm", "Carolina Méndez", "cmendez@empresa.cl", Rol.Lider, "proc"), ("rf", "Ricardo Fuentes", "rfuentes@empresa.cl", Rol.Lider, "cod"),
        ("vn", "Valeria Núñez", "vnunez@empresa.cl", Rol.Lider, "script"),
        ("mpa", "Maria Paz Ayala", "mayala@empresa.cl", Rol.Comercial, ""), ("wm", "Wiliam Muñoz", "wmunoz@empresa.cl", Rol.Comercial, ""),
        ("lm", "Lorena Montalvo Rosado", "lmontalvo@empresa.cl", Rol.Comercial, ""),
        ("ad", "Alexis Duran", "aduran@empresa.cl", Rol.Analista, "proc"), ("jm", "José Miguel Carreño", "jcarreno@empresa.cl", Rol.Analista, "proc+cod"),
        ("ap", "Andrea Pérez", "aperez@empresa.cl", Rol.Analista, "proc"), ("cg", "Cristian Gómez", "cgomez@empresa.cl", Rol.Analista, "proc+script"),
        ("as", "Andrés Salinas", "asalinas@empresa.cl", Rol.Analista, "proc"), ("lr", "Luis Ruiz", "lruiz@empresa.cl", Rol.Analista, "proc+script"),
        ("rs", "Rodrigo Salgado", "rsalgado@empresa.cl", Rol.Analista, "proc"),
        ("jp", "Javiera Pérez", "jperez@empresa.cl", Rol.Analista, "script"), ("th", "Tomás Herrera", "therrera@empresa.cl", Rol.Analista, "script"),
        ("fs", "Fernanda Silva", "fsilva@empresa.cl", Rol.Analista, "script"),
        ("mo", "Marcela Olivares", "molivares@empresa.cl", Rol.Analista, "cod"), ("pv", "Pablo Vergara", "pvergara@empresa.cl", Rol.Analista, "cod"),
        ("dr", "Daniela Rojas", "drojas@empresa.cl", Rol.Analista, "cod"),
    ];

    static readonly Dictionary<string, string> B = new()
    {
        ["U"] = "Ipsos UU (Ex Marketing Cualitativo)", ["C"] = "CEX Chile (Ex Loyalty)", ["H"] = "BHT", ["O"] = "Ops (Operaciones)",
    };
    const string MPA = "Maria Paz Ayala/Maria Paz Ayala", WM = "Wiliam Muñoz/Wiliam Muñoz", LC = "Lorena Montalvo Rosado/Camila Rivera",
        ZP = "Zina Pino/Paulina Montecinos", CT = "CATI (Enc. Telefónicas)", DP = "Data processing";

    // Planilla Prog DP.xlsx · hoja agenda (49 filas únicas): día, BU, técnica, cliente, proyecto, job, entregable, descripción, comercial, analista
    static readonly string[][] SeedDp =
    [
        ["21","U","Intercepción","Procter & Gamble","Pampers_In Home Product Test","26-066509-02-02","Otros","Proceso","Andrea Contreras Andrade/Andrea Contreras Andrade","ad"],
        ["20","C",CT,"Teleton","Sat. Institutos Teletón 2026","26-033416-01-02","Proceso de datos","Tablas Teleton 2026","Francisco Sastre/Lorena Montalvo Rosado","jm"],
        ["20","C",CT,"Teleton","Sat. Institutos Teletón 2026","26-033416-01-02","Proceso de datos","SPSS Teleton 2026","Francisco Sastre/Lorena Montalvo Rosado","jm"],
        ["20","C",CT,"Teleton","Sat. Institutos Teletón 2026","26-033416-01-02","Base de datos cliente Excel","BD Cliente Teleton 2026","Francisco Sastre/Lorena Montalvo Rosado","jm"],
        ["20","C",CT,"Caja Los Andes","1. CLA Global Muestra Aleatoria Mensual","25-098794-09-07","Proceso de datos","Tablas Estadísticas",LC,"ap"],
        ["20","C",CT,"Caja Los Andes","1. CLA Global Muestra Aleatoria Mensual","25-098794-09-07","Proceso de datos","SPSS medición mensual",LC,"ap"],
        ["20","C",DP,"Caja Los Andes","CLA Yeats to date","25-098794-09-09","Proceso de datos","SPSS YTD 2026 Global Booster",LC,"ap"],
        ["20","C",CT,"Caja Los Andes","CLA Estudio Booster Canales Mensual","25-098794-09-08","Proceso de datos","Tablas Global Booster mensual",LC,"ap"],
        ["20","C",CT,"Caja Los Andes","CLA Estudio Booster Canales Mensual","25-098794-09-08","Proceso de datos","SPSS Global Booster mensual",LC,"ap"],
        ["20","C",CT,"Caja Los Andes","CLA Estudio Booster Canales Mensual","25-098794-09-08","Base de datos cliente Excel","BD Cliente Global Booster mensual",LC,"ap"],
        ["22","C",CT,"Banco Estado","BancoEstado_B2B Sitio web","26-038992-02-02","Proceso de datos","Proceso Sitio Web B2B","Catalina Martinez/Victoria Reyes","cg"],
        ["22","C",CT,"Banco Estado","BancoEstado_B2B Sitio web","26-038992-02-02","Base de datos cliente Excel","BD Cliente Sitio Web B2B","Catalina Martinez/Victoria Reyes","cg"],
        ["22","C",CT,"Banco Estado","BancoEstado_B2B Sitio web","26-038992-02-02","Otros","SPSS Sitio Web B2B","Catalina Martinez/Victoria Reyes","cg"],
        ["19","H","IDP","TELETON","Teleton imagen ola 2","26-006844-03","Base de datos","procesamiento",MPA,"as"],
        ["19","H","IDP","TELETON","Teleton imagen ola 2","26-006844-03","Proceso estándar","procesamiento",MPA,"as"],
        ["20","H","IIS","BANCO DE CHILE","Tracking Banco de Chile Ola septiembre","26-000802-01","Base de datos","procesamiento trimestral (Julio-Agosto-Septiembre)","Arantxa Navarro/Arantxa Navarro","cg"],
        ["20","H","IIS","BANCO DE CHILE","Tracking Banco de Chile Ola septiembre","26-000802-01","Proceso estándar","procesamiento trimestral (Julio-Agosto-Septiembre)","Arantxa Navarro/Arantxa Navarro","cg"],
        ["19","H","IDP","ITAU","Post test Itau ola 7","26-016395-01","Base de datos","procesamiento",MPA,"cg"],
        ["19","H","IDP","ITAU","Post test Itau ola 7","26-016395-01","Proceso estándar","procesamiento",MPA,"cg"],
        ["22","H","Qualtrics","LATAM","Latam Cargo","26-068174-01","Base de datos","procesamiento","Tomas Quiroz/Tomas Quiroz","lr"],
        ["22","H","Qualtrics","LATAM","Latam Cargo","26-068174-01","Proceso estándar","procesamiento","Tomas Quiroz/Tomas Quiroz","lr"],
        ["22","H","IIS","LATAM","Tracking Latam Ola Octubre","26-000797-01","Base de datos","procesamiento","Luis Ide/Luis Ide","rs"],
        ["22","H","IIS","LATAM","Tracking Latam Ola Octubre","26-000797-01","Proceso estándar","procesamiento","Luis Ide/Luis Ide","rs"],
        ["21","H","IDP","MALL PLAZA","Post test Mall Plaza Hito 3 (Peru)","26-032312-12","Base de datos","procesamiento",MPA,"as"],
        ["21","H","IDP","MALL PLAZA","Post test Mall Plaza Hito 3 (Peru)","26-032312-12","Proceso estándar","procesamiento",MPA,"as"],
        ["21","H","IDP","MALL PLAZA","Post test Mall Plaza Hito 3 (Colombia)","26-032312-11","Base de datos","procesamiento",MPA,"lr"],
        ["21","H","IDP","MALL PLAZA","Post test Mall Plaza Hito 3 (Colombia)","26-032312-11","Proceso estándar","procesamiento",MPA,"lr"],
        ["23","H","IDP","MALL PLAZA","Post test Mall Plaza Hito 3 (Chile)","26-032312-10","Base de datos","procesamiento",MPA,"ap"],
        ["23","H","IDP","MALL PLAZA","Post test Mall Plaza Hito 3 (Chile)","26-032312-10","Proceso estándar","procesamiento",MPA,"ap"],
        ["19","H","IDP","SURA","Post test AFP Capital","26-082961-01","Base de datos","procesamiento","Florencia Gajardo/Florencia Gajardo","as"],
        ["19","H","IDP","SURA","Post test AFP Capital","26-082961-01","Proceso estándar","procesamiento","Florencia Gajardo/Florencia Gajardo","as"],
        ["21","C",DP,"METLIFE","Deep Dive 2026 CL","26-048425-01-07","Proceso de datos","Base Preliminar",ZP,"rs"],
        ["21","C",DP,"METLIFE","Deep Dive 2026 CL","26-048425-01-07","Proceso de datos","Base Final",ZP,"rs"],
        ["21","C",DP,"METLIFE","Deep Dive 2026 CL","26-048425-01-07","Otros","Clean Data",ZP,"rs"],
        ["21","C",DP,"METLIFE","Deep Dive 2026 CL","26-048425-01-07","Otros","Tablas NPS",ZP,"rs"],
        ["21","C",DP,"METLIFE","Deep Dive 2026 CL","26-048425-01-07","Base de datos cliente SPSS","BD estructura cliente",ZP,"rs"],
        ["19","O",DP,"NESTLE","Milo Ceres Alienation Test CL","26-067771-01-01","Otros","Clean Data","Paulina Montecinos/Paulina Montecinos","as"],
        ["19","O",DP,"NESTLE","Milo Ceres Alienation Test CL","26-067771-01-01","Proceso de datos","Base de datos y proceso estándar","Paulina Montecinos/Paulina Montecinos","as"],
        ["19","O",DP,"NESTLE","Milo Ceres Alienation Test CL","26-067771-01-01","Base de datos cliente","BD estructura cliente","Paulina Montecinos/Paulina Montecinos","as"],
        ["21","O",CT,"Banco de Chile","BCH_Inmobiliaria","25-097810-56-01","Otros","Validación Script",WM,"cg"],
        ["22","O",CT,"Banco de Chile","BCH_Corporaciones","25-097810-57-01","Otros","Validación Script",WM,"as"],
        ["19","O",CT,"Banco de Chile","BCH_GGEE","25-097810-55-01","Otros","Validación Script",WM,"rs"],
        ["23","O",CT,"Banco de Chile","BCH_Cash","25-097810-51-01","Otros","Validación Script",WM,"lr"],
        ["23","O",CT,"Banco de Chile","BCH_Leasing","25-097810-52-01","Otros","Validación Script",WM,"cg"],
        ["23","O",CT,"Banco de Chile","BCH_Comex","25-097810-53-01","Otros","Validación Script",WM,"as"],
        ["22","O",CT,"Banco de Chile","BCH_Factoring","25-097810-54-01","Otros","Validación Script",WM,"ap"],
        ["20","O",CT,"CHILEVALORA","Chilevalora empleadores","26-069068-01-01","Otros","Validación Script",WM,"lr"],
        ["21","O",CT,"Banco Estado","Mipymes Banco Estado 2026","26-052263-01-01","Otros","Validación Script","Nelson Reyes/Nelson Reyes","ap"],
        ["23","O",CT,"DIFEM","Difem 2026","26-066910-01-01","Otros","Validación Script","Camila Squicciarini/Camila Squicciarini","rs"],
    ];

    // analista, estado, fecha, cliente, proyecto, job, descripción, comercial, creado, okAt
    static readonly string?[][] SeedSc =
    [
        ["jp","en_curso","2026-10-21","Banco Estado","Mipymes Banco Estado 2026","26-052263-01-01","Script CATI con cuotas","Nelson Reyes","2026-10-19 09:20",null],
        ["jp","pendiente","2026-10-22","DIFEM","Difem 2026","26-066910-01-01","Ajuste de filtros P12–P18","Camila Squicciarini","2026-10-20 08:45",null],
        ["jp","ok","2026-10-17","CHILEVALORA","Chilevalora empleadores","26-069068-01-01","Programación encuesta","Wiliam Muñoz","2026-10-15 11:00","2026-10-17 16:30"],
        ["th","en_curso","2026-10-21","LATAM","Latam Cargo","26-068174-01","Encuesta Qualtrics módulo NPS","Tomas Quiroz","2026-10-19 14:10",null],
        ["th","ok","2026-10-16","METLIFE","Deep Dive 2026 CL","26-048425-01-07","Script de limpieza","Zina Pino","2026-10-15 10:00","2026-10-16 18:00"],
        ["fs","ok","2026-10-19","Banco de Chile","BCH_Factoring","25-097810-54-01","Script CATI","Wiliam Muñoz","2026-10-16 15:30","2026-10-19 11:20"],
        ["lr","en_curso","2026-10-20","CHILEVALORA","Chilevalora empleadores","26-069068-01-01","Ajuste script CATI ola 2","Wiliam Muñoz","2026-10-19 12:00",null],
        ["cg","pendiente","2026-10-22","Banco de Chile","BCH_Inmobiliaria","25-097810-56-01","Programación script CATI","Wiliam Muñoz","2026-10-20 10:30",null],
        ["fs","ok","2026-10-20","ITAU","Post test Itau ola 7","26-016395-01","Cuestionario online","Maria Paz Ayala","2026-10-19 16:00","2026-10-20 09:50"],
    ];

    static readonly decimal[] Hrs = [1.5m, 2, 3, 4, 2.5m, 6, 1, 3.5m];

    // día, cliente, proyecto, job, entregable, descripción, comercial, analista
    static readonly string[][] SeedCod =
    [
        ["19","Teleton","Sat. Institutos Teletón 2026","26-033416-01-02","Codificación preguntas abiertas","P5, P9 y P14 abiertas","Francisco Sastre","mo"],
        ["20","Caja Los Andes","1. CLA Global Muestra Aleatoria Mensual","25-098794-09-07","Codificación preguntas abiertas","Motivos de satisfacción P3","Lorena Montalvo Rosado","mo"],
        ["21","Banco Estado","BancoEstado_B2B Sitio web","26-038992-02-02","Codificación preguntas abiertas","Sugerencias sitio web","Catalina Martinez","pv"],
        ["20","LATAM","Tracking Latam Ola Octubre","26-000797-01","Codificación de marcas","Recordación espontánea de marcas","Luis Ide","pv"],
        ["22","METLIFE","Deep Dive 2026 CL","26-048425-01-07","Codificación preguntas abiertas","Verbatims NPS","Zina Pino","dr"],
        ["22","Procter & Gamble","Pampers_In Home Product Test","26-066509-02-02","Codificación preguntas abiertas","Likes / dislikes producto","Andrea Contreras Andrade","dr"],
        ["23","ITAU","Post test Itau ola 7","26-016395-01","Codificación de ocupación","Ocupación y rubro","Maria Paz Ayala","jm"],
        ["23","MALL PLAZA","Post test Mall Plaza Hito 3 (Chile)","26-032312-10","Libro de códigos","Actualización libro de códigos","Maria Paz Ayala","mo"],
        ["21","NESTLE","Milo Ceres Alienation Test CL","26-067771-01-01","Codificación preguntas abiertas","Abiertas de concepto","Paulina Montecinos",""],
    ];

    // código, solicitante, tipo, fecha, cliente, proyecto, job, entregables, descripción, SL, técnica, creada, nota
    static readonly (string Cod, string By, Equipo Tipo, string Fecha, string Cliente, string Proyecto, string Job, string[] Ents, string Desc, string SL, string Tec, string At, string Nota)[] SeedSol =
    [
        ("S-1041", "mpa", Equipo.Procesamiento, "2026-10-27", "BANCO DE CHILE", "Tracking Banco de Chile Ola octubre", "26-000802-02", ["Base de datos", "Proceso estándar"], "Procesamiento mensual octubre", "BHT", "IIS", "2026-10-22 11:15", "Materiales llegan el lunes 26 en la mañana."),
        ("S-1042", "wm", Equipo.Script, "2026-10-27", "Banco de Chile", "BCH_Pymes", "25-097810-58-01", ["Programación de script"], "Script CATI nuevo segmento Pymes", "", "", "2026-10-23 09:40", ""),
        ("S-1043", "lm", Equipo.Codificacion, "2026-10-28", "Caja Los Andes", "CLA Estudio Booster Canales Mensual", "25-098794-09-08", ["Codificación preguntas abiertas"], "Abiertas P7 y P11 (motivos de no recomendación)", "Ops (Operaciones)", "Codificación", "2026-10-23 15:05", ""),
        ("S-1044", "mpa", Equipo.Procesamiento, "2026-10-29", "MALL PLAZA", "Post test Mall Plaza Hito 4 (Chile)", "26-032312-13", ["Base de datos", "Proceso estándar", "Base de datos cliente Excel"], "Procesamiento hito 4", "BHT", "IDP", "2026-10-24 08:30", "Mismo formato que Hito 3."),
    ];

    static readonly Dictionary<string, (string Cod, string By, string At)> SolTag = new()
    {
        ["26-016395-01"] = ("S-1035", "mpa", "2026-10-14 10:20"), ["25-098794-09-09"] = ("S-1036", "lm", "2026-10-14 12:00"),
        ["26-069068-01-01"] = ("S-1037", "wm", "2026-10-15 09:10"),
    };

    public static async Task<bool> SeedAsync(IAppDbContext db, IClock clock, IPasswordService passwords, CancellationToken ct = default)
    {
        if (await db.Usuarios.AnyAsync(ct)) return false;

        var shift = (Fechas.MondayOf(clock.Today).DayNumber - Cur0.DayNumber) / 7 * 7;
        var cur = Cur0.AddDays(shift);
        DateOnly D(string s) => DateOnly.ParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(shift);
        DateOnly Oct(string day) => new DateOnly(2026, 10, int.Parse(day)).AddDays(shift);
        static DateTimeOffset At(DateOnly d, int h, int m) => Fechas.FromLocal(d, h, m);
        DateTimeOffset Ts(string s) => At(D(s), int.Parse(s[11..13]), int.Parse(s[14..16]));
        static Estado Est(string e) => e switch { "ok" => Estado.Ok, "en_curso" => Estado.EnCurso, _ => Estado.Pendiente };

        // Usuarios (contraseña de demostración común; en producción usar Windows Auth o cambiarla).
        var hash = passwords.Hash(DemoPassword);
        var users = new Dictionary<string, Usuario>();
        foreach (var (key, nombre, email, rol, eq) in SeedUsers)
        {
            var u = new Usuario { Nombre = nombre, Email = email, Rol = rol, Activo = true, PasswordHash = hash };
            foreach (var t in eq.Split('+', StringSplitOptions.RemoveEmptyEntries))
                u.Equipos.Add(new UsuarioEquipo { Equipo = t switch { "script" => Equipo.Script, "cod" => Equipo.Codificacion, _ => Equipo.Procesamiento } });
            db.Usuarios.Add(u);
            users[key] = u;
            await db.SaveChangesAsync(ct); // ids en el orden del mockup
        }
        int? Uid(string? k) => string.IsNullOrEmpty(k) ? null : users[k].Id;

        var seq = 100;
        var outList = new List<(int Seq, Requerimiento R)>();
        void Push(Requerimiento r) => outList.Add((++seq, r));

        for (var i = 0; i < SeedDp.Length; i++)
        {
            var x = SeedDp[i];
            var fecha = Oct(x[0]);
            var estado = x[0] == "19" ? Estado.Ok : x[0] == "20" ? Estado.EnCurso : Estado.Pendiente;
            if (x[5] == "26-082961-01") estado = Estado.Pendiente; // "Post test AFP Capital": ejemplo de atraso sin gestión
            Requerimiento Base() => new()
            {
                Tipo = Equipo.Procesamiento, Pais = "CL", SL = B[x[1]], Tecnica = x[2], Cliente = x[3], Proyecto = x[4], Job = x[5], Entregable = x[6],
                Descripcion = x[7], Comercial = x[8], AnalistaId = Uid(x[9]),
            };
            var r0 = Base();
            r0.Fecha = fecha; r0.Estado = estado;
            r0.MaterialesAt = estado != Estado.Pendiente || i % 2 == 1 ? fecha.AddDays(-2) : null;
            r0.Horas = estado == Estado.Ok ? Hrs[i % 8] : null;
            r0.Creado = Ts("2026-10-15 16:00");
            r0.Actualizado = estado == Estado.EnCurso ? Ts("2026-10-20 09:00") : Ts("2026-10-15 16:00");
            if (estado == Estado.Ok) { r0.OkAt = Ts("2026-10-19 18:10"); r0.OkById = r0.AnalistaId; }
            Push(r0);
            for (var k = 1; k <= 8; k++)
            {
                if ((i + k) % 3 == 0) continue;
                var f = fecha.AddDays(-7 * k);
                var lt = (i * 7 + k) % 9 == 0;
                var okAt = At(lt ? f.AddDays(1) : f, 15 + (i + k) % 3, (i * 13 + k * 7) % 60);
                var r = Base();
                r.Fecha = f; r.Estado = Estado.Ok; r.MaterialesAt = f.AddDays(-2); r.Horas = Hrs[(i + k) % 8];
                r.Creado = At(f.AddDays(-4), 16, 0); r.Actualizado = okAt; r.OkAt = okAt; r.OkById = r.AnalistaId;
                Push(r);
            }
        }

        Requerimiento Script(string?[] x, DateOnly fecha) => new()
        {
            Tipo = Equipo.Script, Fecha = fecha, Pais = "CL", SL = "", Tecnica = "", Cliente = x[3]!, Proyecto = x[4]!, Job = x[5]!,
            Entregable = "Programación de script", Descripcion = x[6]!, Comercial = x[7]!, AnalistaId = Uid(x[0]),
        };
        for (var j = 0; j < SeedSc.Length; j++)
        {
            var x = SeedSc[j];
            var r = Script(x, D(x[2]!));
            r.MaterialesAt = r.Fecha.AddDays(-3);
            r.Horas = x[9] != null ? Hrs[(j + 3) % 8] + 1 : null;
            r.Estado = Est(x[1]!);
            r.Creado = Ts(x[8]!); r.Actualizado = r.Creado;
            if (x[9] is { } ok) { r.OkAt = Ts(ok); r.OkById = r.AnalistaId; }
            Push(r);
        }
        for (var j = 0; j < SeedSc.Length; j++)
        {
            var x = SeedSc[j];
            for (var k = 1; k <= 6; k++)
            {
                if ((j + k) % 2 == 1) continue;
                var f = D(x[2]!).AddDays(-7 * k);
                var okAt = At((j + k) % 5 == 0 ? f.AddDays(2) : f, 12, (j * 11 + k) % 60);
                var r = Script(x, f);
                r.MaterialesAt = f.AddDays(-3); r.Horas = Hrs[(j + k) % 8] + 1; r.Estado = Estado.Ok;
                r.Creado = At(f.AddDays(-3), 10, 0); r.Actualizado = okAt; r.OkAt = okAt; r.OkById = r.AnalistaId;
                Push(r);
            }
        }

        for (var i = 0; i < SeedCod.Length; i++)
        {
            var x = SeedCod[i];
            var fecha = Oct(x[0]);
            var estado = x[0] == "19" ? Estado.Ok : x[0] == "20" ? Estado.EnCurso : Estado.Pendiente;
            Requerimiento Base() => new()
            {
                Tipo = Equipo.Codificacion, Pais = "CL", SL = "Ops (Operaciones)", Tecnica = "Codificación", Cliente = x[1], Proyecto = x[2], Job = x[3],
                Entregable = x[4], Descripcion = x[5], Comercial = x[6], AnalistaId = Uid(x[7]),
            };
            var r0 = Base();
            r0.Fecha = fecha; r0.Estado = estado;
            r0.MaterialesAt = estado != Estado.Pendiente || i % 2 == 1 ? fecha.AddDays(-1) : null;
            if (estado == Estado.Ok) { r0.Registros = 1240; r0.Metodo = MetodoCodificacion.IA; r0.OkAt = Ts("2026-10-19 17:30"); r0.OkById = r0.AnalistaId; }
            r0.Creado = Ts("2026-10-15 16:00");
            r0.Actualizado = estado == Estado.EnCurso ? Ts("2026-10-20 09:00") : Ts("2026-10-15 16:00");
            Push(r0);
            if (r0.AnalistaId is null) continue;
            for (var k = 1; k <= 6; k++)
            {
                if ((i + k) % 3 == 0) continue;
                var f = fecha.AddDays(-7 * k);
                var lt = (i * 5 + k) % 8 == 0;
                var okAt = At(lt ? f.AddDays(1) : f, 14 + (i + k) % 3, (i * 17 + k * 5) % 60);
                var r = Base();
                r.Fecha = f; r.Estado = Estado.Ok; r.MaterialesAt = f.AddDays(-1); r.Registros = 300 + (i * 97 + k * 53) % 1500;
                r.Metodo = (i + k) % 3 == 1 ? MetodoCodificacion.Manual : MetodoCodificacion.IA;
                r.Creado = At(f.AddDays(-4), 16, 0); r.Actualizado = okAt; r.OkAt = okAt; r.OkById = r.AnalistaId;
                Push(r);
            }
        }

        // ~20 % de entregas históricas con OK tardío, clasificadas de forma determinística.
        foreach (var (idn, r) in outList)
        {
            if (r.Estado == Estado.Ok && r.Fecha < cur && r.OkAt is { } ok && !Reglas.IsLateOk(r) && idn % 6 == 0)
            {
                var l = TimeZoneInfo.ConvertTime(ok, Fechas.Chile);
                r.OkAt = At(r.Fecha.AddDays(1 + idn % 3), l.Hour, l.Minute);
            }
            if (Reglas.IsLateOk(r))
            {
                var k = idn % 10;
                var resp = r.Tipo == Equipo.Script ? (k < 5 ? AtrasoResponsable.Operaciones : AtrasoResponsable.Comercial)
                    : k < 4 ? AtrasoResponsable.Operaciones : k < 7 ? AtrasoResponsable.Comercial : AtrasoResponsable.Campo;
                var mot = Catalogos.MOTIVOS[resp];
                r.AtrasoResp = resp;
                r.AtrasoMotivo = mot[idn % mot.Count];
                r.AtrasoCampo = resp == AtrasoResponsable.Campo ? (Reglas.CampoFrom(r.Tecnica) is { Length: > 0 } c ? c : Catalogos.CAMPOS[idn % Catalogos.CAMPOS.Count]) : "";
                if (resp == AtrasoResponsable.Comercial && r.AtrasoMotivo == Catalogos.MotivoMaterialesTarde) r.MaterialesAt = r.Fecha;
            }
        }

        // Requerimientos existentes que vinieron de solicitudes comerciales (ya asignadas).
        var sols = new Dictionary<string, Solicitud>();
        foreach (var (_, r) in outList)
        {
            if (!SolTag.TryGetValue(r.Job, out var t) || r.Fecha < cur) continue;
            if (!sols.TryGetValue(t.Cod, out var s))
                sols[t.Cod] = s = new Solicitud { Codigo = t.Cod, SolicitanteId = users[t.By].Id, CreadaAt = Ts(t.At) };
            r.Solicitud = s;
        }

        db.Requerimientos.AddRange(outList.Select(x => x.R));
        foreach (var s in sols.Values) db.Solicitudes.Add(s);

        // Solicitudes nuevas sin asignar, con su aviso a los líderes ya enviado.
        var allUsers = users.Values.ToList();
        foreach (var x in SeedSol)
        {
            var at = Ts(x.At);
            var s = new Solicitud { Codigo = x.Cod, SolicitanteId = users[x.By].Id, CreadaAt = at, Nota = x.Nota };
            foreach (var e in x.Ents)
                s.Requerimientos.Add(new Requerimiento
                {
                    Tipo = x.Tipo, Fecha = D(x.Fecha), Pais = "CL", SL = x.SL, Tecnica = x.Tec, Cliente = x.Cliente, Proyecto = x.Proyecto, Job = x.Job,
                    Entregable = e, Descripcion = x.Desc, Comercial = users[x.By].Nombre, Estado = Estado.Pendiente, Creado = at, Actualizado = at, Nota = x.Nota,
                });
            db.Solicitudes.Add(s);
            await db.SaveChangesAsync(ct);
            var mail = MailBuilder.Solicitud(Reglas.TeamLeadersOf(allUsers, x.Tipo), users[x.By], s.Codigo, s.Requerimientos, s.Nota).ToEntity(at);
            mail.Status = NotificacionStatus.Enviado;
            mail.SentAt = at;
            mail.Error = "Datos de ejemplo";
            db.Notificaciones.Add(mail);
        }
        await db.SaveChangesAsync(ct);
        return true;
    }
}
